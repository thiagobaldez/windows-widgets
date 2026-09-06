using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Widgets.Weather.Api;
using Widgets.Weather.Domain;

namespace Widgets.Weather;

public enum WeatherStatus { Loading, Ok, Stale, Error }

/// <summary>
/// Estado do widget de tempo. Vive fora da View de propósito: quando o
/// explorer.exe reinicia, o host recria a janela e a View, mas os dados já
/// carregados e o agendamento continuam aqui.
/// </summary>
public sealed class WeatherViewModel : INotifyPropertyChanged, IDisposable
{
    /// <summary>Acima disso, o dado em cache é apresentado como desatualizado.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(2);

    private readonly OpenMeteoClient _api = new();
    private readonly IpLocationProvider _ipLocation = new();
    private readonly WeatherCache _cache;
    private readonly DispatcherTimer _timer;
    private RefreshPolicy _policy;
    private readonly CultureInfo _culture = new("pt-BR");

    private CancellationTokenSource? _inFlight;
    private WeatherSnapshot? _snapshot;
    private WeatherStatus _status = WeatherStatus.Loading;
    private string _statusMessage = "Carregando...";
    private string _staleBadge = "";

    /// <summary>Previsão de hoje. Fonte de nascer/pôr do sol.</summary>
    private DailyForecast? _today;

    /// <summary>Hoje, mas só quando a faixa é utilizável (mín != máx).</summary>
    private DailyForecast? _todayRange;
    private bool _disposed;

    public WeatherViewModel(WeatherOptions options, WeatherCache? cache = null)
    {
        Options = options;
        _cache = cache ?? new WeatherCache();
        _policy = BuildPolicy(options);

        _timer = new DispatcherTimer();
        _timer.Tick += (_, _) => _ = RefreshAsync();

        // Mostra o cache na hora; a rede vem depois.
        var cached = _cache.TryLoad();
        if (cached is not null) Apply(cached, fromCache: true);
    }

    public WeatherOptions Options { get; }

    /// <summary>
    /// Dias formatados para o layout MÉDIO (4). Preparar aqui evita converters
    /// e chamadas de método no XAML, e mantém a formatação pt-BR num lugar só.
    /// </summary>
    public ObservableCollection<DailyItem> Daily { get; } = new();

    /// <summary>
    /// Dias para o layout GRANDE (6). Duas coleções em vez de uma filtrada:
    /// XAML não tem "pegue os primeiros N", e um converter para isso seria
    /// mais código do que manter duas listas.
    /// </summary>
    public ObservableCollection<DailyItem> DailyExtended { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public WeatherSnapshot? Snapshot
    {
        get => _snapshot;
        private set
        {
            _snapshot = value;
            RaiseAll();
        }
    }

    public WeatherStatus Status
    {
        get => _status;
        private set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowsStaleBadge)); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set { _statusMessage = value; OnPropertyChanged(); }
    }

    public bool ShowsStaleBadge => Status is WeatherStatus.Stale or WeatherStatus.Error;

    /// <summary>Texto do selo de ressalva. Diz a causa real, não um genérico "desatualizado".</summary>
    public string StaleBadge
    {
        get => _staleBadge;
        private set { _staleBadge = value; OnPropertyChanged(); }
    }

    // --- Faixa de temperatura de hoje (o elemento-assinatura) ---

    /// <summary>Só há faixa quando a previsão de hoje veio e mín != máx.</summary>
    public bool HasTodayRange => _todayRange is not null;

    public string TodayMin => _todayRange is null ? "" : Degrees(_todayRange.MinC);
    public string TodayMax => _todayRange is null ? "" : Degrees(_todayRange.MaxC);

    /// <summary>
    /// Posição do marcador na barra, expressa como pesos de coluna em estrela.
    /// Fracionar por GridLength evita ter que medir a barra em code-behind.
    /// </summary>
    public GridLength RangeLeft { get; private set; } = new(0, GridUnitType.Star);
    public GridLength RangeRight { get; private set; } = new(1, GridUnitType.Star);

    public string LocationName => _snapshot?.LocationName ?? Options.LocationName ?? "—";

    /// <summary>
    /// Só a cidade, sem estado nem país. No layout pequeno o rótulo completo
    /// não cabe e corta em reticências — e ali o estado é ruído: quem olha o
    /// widget sabe em que estado mora.
    /// </summary>
    public string LocationShort
    {
        get
        {
            var full = LocationName;
            var comma = full.IndexOf(',');
            return comma > 0 ? full[..comma].Trim() : full;
        }
    }

    /// <summary>Leitura completa com unidade. Usada em diagnóstico e tooltips.</summary>
    public string Temperature => Format(_snapshot?.TemperatureC);

    // Valor e unidade separados: num instrumento a unidade é subordinada ao
    // número, e repetir "°C" em cada célula da previsão é ruído.
    public string TemperatureValue =>
        _snapshot is null ? "—" : $"{Math.Round(Convert(_snapshot.TemperatureC)):0}";
    public string TemperatureUnit => Options.UseFahrenheit ? "°F" : "°C";

    public string ApparentTemperature =>
        _snapshot is null ? "—" : $"Sensação {Degrees(_snapshot.ApparentC)}";
    public string Description => _snapshot?.Condition.Description ?? "";
    public string Humidity => _snapshot is null ? "—" : $"{_snapshot.HumidityPercent}%";
    public string WindValue => _snapshot is null ? "—" : $"{_snapshot.WindKmh:0}";
    public string WindUnit => "km/h";
    public WeatherIconKindProxy Icon => new(
        _snapshot?.Condition.Icon ?? WeatherIconKind.Unknown, _snapshot?.IsDay ?? true);

    public string UpdatedAt => _snapshot is null
        ? ""
        : $"Atualizado {_snapshot.FetchedAt.ToLocalTime().ToString("HH:mm", _culture)}";

    // --- Nascer/pôr do sol (só o layout grande usa) ---

    public bool HasSunTimes => _today?.Sunrise is not null && _today?.Sunset is not null;
    public string Sunrise => _today?.Sunrise?.ToString("HH:mm", _culture) ?? "";
    public string Sunset => _today?.Sunset?.ToString("HH:mm", _culture) ?? "";

    private static RefreshPolicy BuildPolicy(WeatherOptions options)
        => new(TimeSpan.FromMinutes(Math.Max(1, options.RefreshMinutes)));

    /// <summary>
    /// Reaplica as opções depois que o usuário salva as configurações: o
    /// intervalo novo entra em vigor e a unidade é reformatada na hora, sem
    /// esperar a próxima requisição nem reiniciar o app.
    /// </summary>
    public void ApplyOptions()
    {
        _policy = BuildPolicy(Options);

        // Reformata o que já está na tela (troca de °C para °F, por exemplo).
        if (_snapshot is not null) Apply(_snapshot, fromCache: false);

        _ = RefreshAsync();
    }

    /// <summary>Primeira carga: resolve a localização se ainda não houver uma.</summary>
    public async Task StartAsync()
    {
        if (!Options.HasCoordinates)
        {
            var located = await _ipLocation.TryResolveAsync().ConfigureAwait(true);
            if (located is not null)
            {
                Options.Latitude = located.Latitude;
                Options.Longitude = located.Longitude;
                Options.LocationName = located.Name;
                Options.RaiseChanged();
            }
            else
            {
                Status = WeatherStatus.Error;
                StatusMessage = "Não foi possível detectar a localização. " +
                                "Escolha a cidade nas configurações.";
                return;
            }
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    public async Task RefreshAsync()
    {
        if (_disposed || !Options.HasCoordinates) return;

        // Uma atualização por vez: cliques repetidos no menu não empilham requisições.
        _inFlight?.Cancel();
        var cts = new CancellationTokenSource();
        _inFlight = cts;

        var succeeded = false;
        try
        {
            if (_snapshot is null)
            {
                Status = WeatherStatus.Loading;
                StatusMessage = "Carregando...";
            }

            // Sem forecastDays explícito: o default do cliente já cobre o
            // layout grande (6 dias além de hoje).
            var snapshot = await _api.GetAsync(
                Options.Latitude!.Value, Options.Longitude!.Value,
                Options.LocationName ?? "—", ct: cts.Token).ConfigureAwait(true);

            if (cts.IsCancellationRequested) return;

            _cache.Save(snapshot);
            Apply(snapshot, fromCache: false);
            succeeded = true;
        }
        catch (OperationCanceledException)
        {
            return;   // substituída por outra atualização; não conta como falha
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WeatherViewModel] atualização falhou: {ex.Message}");

            // Com dado antigo na mão, degrada em vez de apagar a tela.
            Status = _snapshot is null ? WeatherStatus.Error : WeatherStatus.Stale;
            StatusMessage = _snapshot is null
                ? "Sem conexão com o serviço de previsão."
                : "Sem conexão — mostrando a última leitura.";
            StaleBadge = "sem conexão";
        }
        finally
        {
            if (ReferenceEquals(_inFlight, cts)) _inFlight = null;
            cts.Dispose();
            Schedule(succeeded);
        }
    }

    private void Schedule(bool succeeded)
    {
        if (_disposed) return;
        _timer.Stop();
        _timer.Interval = _policy.Next(succeeded);
        _timer.Start();
    }

    private void Apply(WeatherSnapshot snapshot, bool fromCache)
    {
        // Hoje é decidido pelo CALENDÁRIO, não pela posição no array. Um
        // snapshot buscado às 23:57 tem Daily[0] = ontem depois da virada da
        // meia-noite; usar o índice mostraria a faixa de ontem rotulada "hoje"
        // e deixaria o dia de hoje sobrando na tira.
        var today = DateOnly.FromDateTime(DateTime.Today);

        BuildTodayRange(snapshot, today);
        Snapshot = snapshot;

        // A tira começa DEPOIS de hoje: hoje já é a barra de faixa logo acima,
        // e repetir a mesma mín/máx em dois lugares é ruído.
        Daily.Clear();
        DailyExtended.Clear();
        foreach (var day in snapshot.Daily.Where(d => d.Date > today).Take(6))
        {
            var item = new DailyItem(
                FormatWeekday(day),
                day.Condition.Icon,
                Degrees(day.MaxC),
                Degrees(day.MinC),
                day.PrecipitationProbability is int p ? $"{p}%" : null);

            DailyExtended.Add(item);
            if (Daily.Count < 4) Daily.Add(item);
        }

        if (fromCache && snapshot.IsStale(DateTimeOffset.Now, StaleAfter))
        {
            Status = WeatherStatus.Stale;
            StatusMessage = "Mostrando a última leitura salva.";
            StaleBadge = "dado antigo";
        }
        else
        {
            Status = WeatherStatus.Ok;
            StatusMessage = "";
            StaleBadge = "";
        }
    }

    /// <summary>
    /// Onde a temperatura atual cai dentro da mínima e da máxima de hoje.
    /// A atual pode estourar a faixa prevista, então o valor é limitado a [0,1].
    ///
    /// <para>Se o snapshot não contém o dia de hoje (cache velho de mais de um
    /// dia), a barra some. Mostrar a faixa de outro dia rotulada "hoje" seria
    /// pior que não mostrar nada.</para>
    /// </summary>
    private void BuildTodayRange(WeatherSnapshot snapshot, DateOnly today)
    {
        var entry = snapshot.Daily.FirstOrDefault(d => d.Date == today);
        _today = entry;

        if (entry is null || entry.MaxC <= entry.MinC)
        {
            _todayRange = null;
            RangeLeft = new GridLength(0, GridUnitType.Star);
            RangeRight = new GridLength(1, GridUnitType.Star);
            return;
        }

        _todayRange = entry;
        var position = Math.Clamp(
            (snapshot.TemperatureC - entry.MinC) / (entry.MaxC - entry.MinC), 0d, 1d);

        RangeLeft = new GridLength(position, GridUnitType.Star);
        RangeRight = new GridLength(1 - position, GridUnitType.Star);
    }

    private double Convert(double celsius)
        => Options.UseFahrenheit ? celsius * 9 / 5 + 32 : celsius;

    /// <summary>Com unidade: "10°C".</summary>
    private string Format(double? celsius)
        => celsius is null ? "—" : $"{Math.Round(Convert(celsius.Value)):0}{TemperatureUnit}";

    /// <summary>Só o grau: "10°". Para onde a unidade já está estabelecida pelo contexto.</summary>
    private string Degrees(double? celsius)
        => celsius is null ? "—" : $"{Math.Round(Convert(celsius.Value)):0}°";

    private string FormatWeekday(DailyForecast day)
    {
        var date = day.Date.ToDateTime(TimeOnly.MinValue);
        if (date.Date == DateTime.Today) return "Hoje";
        var name = _culture.DateTimeFormat.GetAbbreviatedDayName(date.DayOfWeek);
        return _culture.TextInfo.ToTitleCase(name.Replace(".", ""));
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(Snapshot));
        OnPropertyChanged(nameof(LocationName));
        OnPropertyChanged(nameof(LocationShort));
        OnPropertyChanged(nameof(Temperature));
        OnPropertyChanged(nameof(ApparentTemperature));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(Humidity));
        OnPropertyChanged(nameof(WindValue));
        OnPropertyChanged(nameof(TemperatureValue));
        OnPropertyChanged(nameof(TemperatureUnit));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(UpdatedAt));
        OnPropertyChanged(nameof(HasTodayRange));
        OnPropertyChanged(nameof(TodayMin));
        OnPropertyChanged(nameof(TodayMax));
        OnPropertyChanged(nameof(RangeLeft));
        OnPropertyChanged(nameof(RangeRight));
        OnPropertyChanged(nameof(HasSunTimes));
        OnPropertyChanged(nameof(Sunrise));
        OnPropertyChanged(nameof(Sunset));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _inFlight?.Cancel();
        _inFlight?.Dispose();
    }
}

/// <summary>Par ícone+período, para a View ligar as duas propriedades de uma vez.</summary>
public readonly record struct WeatherIconKindProxy(WeatherIconKind Kind, bool IsDay);

/// <summary>Um dia da previsão, já formatado para exibição.</summary>
public sealed record DailyItem(
    string Weekday, WeatherIconKind Icon, string Max, string Min, string? Precipitation = null)
{
    /// <summary>
    /// A API não devolve probabilidade para os dias além do alcance do modelo.
    /// Nesses casos o campo some: um "0%" falso afirmaria o que ninguém afirmou.
    /// </summary>
    public bool HasPrecipitation => !string.IsNullOrEmpty(Precipitation);
}

