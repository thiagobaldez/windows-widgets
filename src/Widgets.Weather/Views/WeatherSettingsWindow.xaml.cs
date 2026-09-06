using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using Widgets.Weather.Api;

namespace Widgets.Weather.Views;

public partial class WeatherSettingsWindow : Window
{
    private sealed record IntervalOption(string Label, int Minutes)
    {
        public override string ToString() => Label;
    }

    private static readonly IntervalOption[] Intervals =
    {
        new("5 minutos", 5),
        new("10 minutos", 10),
        new("15 minutos", 15),
        new("30 minutos", 30),
        new("1 hora", 60),
    };

    private readonly WeatherOptions _options;
    private readonly GeocodingClient _geocoding = new();
    private CancellationTokenSource? _search;

    public WeatherSettingsWindow(WeatherOptions options)
    {
        _options = options;
        InitializeComponent();

        CurrentLocation.Text = string.IsNullOrWhiteSpace(options.LocationName)
            ? "Nenhuma cidade definida."
            : $"Atual: {options.LocationName}";

        CelsiusOption.IsChecked = !options.UseFahrenheit;
        FahrenheitOption.IsChecked = options.UseFahrenheit;

        IntervalBox.ItemsSource = Intervals;
        IntervalBox.SelectedItem =
            Intervals.FirstOrDefault(i => i.Minutes == options.RefreshMinutes) ?? Intervals[2];

        Loaded += (_, _) => SearchBox.Focus();
    }

    /// <summary>True quando o usuário salvou e algo mudou de fato.</summary>
    public bool Saved { get; private set; }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) _ = SearchAsync();
    }

    private void OnSearchClick(object sender, RoutedEventArgs e) => _ = SearchAsync();

    private async System.Threading.Tasks.Task SearchAsync()
    {
        var query = SearchBox.Text.Trim();
        if (query.Length < 2)
        {
            ShowStatus("Digite ao menos 2 letras.");
            return;
        }

        // Buscas rápidas em sequência: a última manda.
        _search?.Cancel();
        var cts = new CancellationTokenSource();
        _search = cts;

        SearchButton.IsEnabled = false;
        ShowStatus("Buscando...");
        try
        {
            var results = await _geocoding.SearchAsync(query, ct: cts.Token).ConfigureAwait(true);
            if (cts.IsCancellationRequested) return;

            Results.ItemsSource = results;
            if (results.Count == 0) ShowStatus("Nenhuma cidade encontrada.");
            else
            {
                HideStatus();
                Results.SelectedIndex = 0;
            }
        }
        catch (OperationCanceledException)
        {
            // substituída por outra busca
        }
        catch (Exception ex)
        {
            ShowStatus($"Falha na busca: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_search, cts)) _search = null;
            cts.Dispose();
            SearchButton.IsEnabled = true;
        }
    }

    private void ShowStatus(string text)
    {
        SearchStatus.Text = text;
        SearchStatus.Visibility = Visibility.Visible;
    }

    private void HideStatus() => SearchStatus.Visibility = Visibility.Collapsed;

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        // Cidade só muda se o usuário escolheu uma; salvar sem selecionar nada
        // mantém a localização atual em vez de apagá-la.
        if (Results.SelectedItem is GeoLocation picked)
        {
            _options.Latitude = picked.Latitude;
            _options.Longitude = picked.Longitude;
            _options.LocationName = picked.Name;
        }

        _options.UseFahrenheit = FahrenheitOption.IsChecked == true;
        if (IntervalBox.SelectedItem is IntervalOption interval)
            _options.RefreshMinutes = interval.Minutes;

        _options.RaiseChanged();
        Saved = true;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    protected override void OnClosed(EventArgs e)
    {
        _search?.Cancel();
        _search?.Dispose();
        base.OnClosed(e);
    }
}
