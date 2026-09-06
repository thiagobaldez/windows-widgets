using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Threading;
using WidgetHost.Config;
using WidgetHost.Desktop;
using Widgets.Abstractions;
using Forms = System.Windows.Forms;

namespace WidgetHost.Shell;

/// <summary>
/// Dono do ciclo de vida de UMA instância de widget na tela: cria a janela,
/// ancora na área de trabalho, posiciona, persiste posição e escala e — o
/// ponto crítico — RECRIA tudo quando o explorer.exe reinicia.
///
/// <para>Quando o explorer morre, o SHELLDLL_DefView vai junto, e o Windows
/// destrói as janelas filhas dele. Nossa janela simplesmente deixa de existir.
/// Reanexar não resolve: é preciso construir uma janela nova. Por isso o
/// <see cref="IWidget"/> guarda o estado e a View é descartável.</para>
/// </summary>
public sealed class WidgetController : IDisposable
{
    /// <summary>Depois de tantas recriações seguidas, desiste da ancoragem e fica no fallback.</summary>
    private const int MaxRecreatesBeforeFallback = 3;

    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(3);

    private readonly IWidget _widget;
    private readonly WidgetInstanceSettings _settings;
    private readonly Action _persist;
    private readonly int _cascadeIndex;
    private readonly DesktopLayer _layer = new();

    private WidgetWindow? _window;
    private DispatcherTimer? _watchdog;
    private int _consecutiveRecreates;
    private bool _giveUpAnchoring;
    private bool _disposed;

    /// <param name="cascadeIndex">
    /// Quantas instâncias já existiam quando esta foi criada. Só desloca o
    /// posicionamento inicial, para duas instâncias novas do mesmo tipo não
    /// nascerem exatamente uma em cima da outra. Não é persistido: depois que a
    /// posição está salva, ele não importa mais.
    /// </param>
    public WidgetController(
        IWidget widget, WidgetInstanceSettings settings, Action persist, int cascadeIndex = 0)
    {
        _widget = widget;
        _settings = settings;
        _persist = persist;
        _cascadeIndex = cascadeIndex;
    }

    public AttachMode Mode => _layer.Mode;
    public IWidget Widget => _widget;
    public WidgetInstanceSettings Settings => _settings;

    /// <summary>O usuário fechou este widget. O host remove a instância.</summary>
    public event EventHandler? Closed;

    public void Start()
    {
        CreateWindow();

        _watchdog = new DispatcherTimer { Interval = WatchdogInterval };
        _watchdog.Tick += OnWatchdogTick;
        _watchdog.Start();
    }

    public void Stop()
    {
        _watchdog?.Stop();
        _watchdog = null;
        DestroyWindow();
    }

    private void CreateWindow()
    {
        var size = _widget.SupportedSizes.Contains(_settings.Size)
            ? _settings.Size
            : _widget.SupportedSizes[0];

        var window = new WidgetWindow(
            _layer,
            _widget.CreateView(size),
            _widget.DesignSizeFor(size),
            _settings.Scale,
            size,
            _widget.SupportedSizes)
        {
            Locked = _settings.Locked,
        };

        window.SizeVariantRequested += (_, requested) => ChangeSize(requested);

        window.RefreshRequested += (_, _) => _widget.Refresh();
        window.SettingsRequested += (_, _) => _widget.ShowSettings();
        window.CloseRequested += (_, _) => Closed?.Invoke(this, EventArgs.Empty);
        window.LockToggled += (_, _) =>
        {
            _settings.Locked = window.Locked;
            _persist();
        };
        window.Moved += (_, pos) =>
        {
            _settings.X = pos.X;
            _settings.Y = pos.Y;
            _persist();
        };
        window.Scaled += (_, scale) =>
        {
            _settings.Scale = scale;
            _persist();
        };

        window.Show();

        var hwnd = window.Handle;
        var mode = _giveUpAnchoring ? _layer.AttachBottomMostOnly(hwnd) : _layer.Attach(hwnd);
        Debug.WriteLine($"[WidgetController] '{_settings.Type}/{_settings.InstanceId}' ancorado como {mode}");

        _window = window;
        ApplyPosition();
    }

    /// <summary>
    /// Troca a variante de layout. Não dá para trocar a View no lugar: cada
    /// variante tem tamanho de projeto próprio, então a janela é reconstruída.
    ///
    /// <para>O zoom volta a 100%: o usuário pediu outro layout, não o layout
    /// novo esticado pelo zoom que servia ao anterior.</para>
    /// </summary>
    private void ChangeSize(WidgetSize size)
    {
        if (_settings.Size == size) return;

        _settings.Size = size;
        _settings.Scale = 1.0;
        _persist();

        DestroyWindow();
        CreateWindow();
    }

    private void DestroyWindow()
    {
        if (_window is null) return;

        try
        {
            _window.Content = null;   // solta a View para poder recriá-la
            _window.Close();
        }
        catch (Exception ex)
        {
            // Esperado quando o HWND já foi destruído junto com o shell.
            Debug.WriteLine($"[WidgetController] fechar janela morta: {ex.Message}");
        }

        _window = null;
    }

    /// <summary>
    /// A posição salva SEMPRE vence. Recriar a janela não pode reescrever a
    /// escolha do usuário, e a recriação acontece justamente quando o shell
    /// está reiniciando — o pior momento possível para confiar nas métricas de
    /// monitor e recalcular um lugar "melhor".
    ///
    /// <para>Se a posição salva estiver mesmo fora de tela, quem conserta é o
    /// <see cref="RescueIfOffScreen"/>, depois que o shell se estabiliza.</para>
    /// </summary>
    private void ApplyPosition()
    {
        if (_window is null) return;

        var (w, h) = _window.PixelSize;

        if (_settings.X is int savedX && _settings.Y is int savedY)
        {
            _layer.MoveToScreen(_window.Handle, savedX, savedY, w, h);
            return;
        }

        if (!TryGetDefaultPosition(w, h, _cascadeIndex, out var x, out var y)) return;

        _layer.MoveToScreen(_window.Handle, x, y, w, h);
        _settings.X = x;
        _settings.Y = y;
        _persist();
    }

    /// <summary>
    /// Canto superior direito da tela primária, deslocado em cascata para
    /// instâncias novas não nascerem exatamente uma em cima da outra.
    /// Devolve false quando as métricas do shell não são confiáveis — melhor
    /// não posicionar agora e tentar de novo no próximo tick do que aceitar um
    /// valor absurdo.
    /// </summary>
    private static bool TryGetDefaultPosition(
        int widthPx, int heightPx, int cascadeIndex, out int x, out int y)
    {
        x = y = 0;

        var work = Forms.Screen.PrimaryScreen?.WorkingArea ?? default;
        if (work.Width < widthPx || work.Height < heightPx) return false;

        const int margin = 24;
        const int cascadeStep = 36;
        var offset = cascadeStep * Math.Max(0, cascadeIndex);

        var candidate = (X: work.Right - widthPx - margin - offset, Y: work.Top + margin + offset);

        // O teste que realmente importa: o ponto calculado cai numa tela?
        if (!IsOnAnyScreen(candidate.X, candidate.Y, widthPx, heightPx))
        {
            // Cascata longa demais saiu da tela; volta para o canto sem deslocamento.
            candidate = (work.Right - widthPx - margin, work.Top + margin);
            if (!IsOnAnyScreen(candidate.X, candidate.Y, widthPx, heightPx)) return false;
        }

        (x, y) = candidate;
        return true;
    }

    /// <summary>
    /// Resgata o widget se ele acabou fora de qualquer monitor — troca de
    /// resolução, monitor desconectado. Só age com o shell saudável e o usuário
    /// sem arrastar, para não brigar com nenhum dos dois.
    /// </summary>
    private void RescueIfOffScreen()
    {
        if (_window is null || _window.IsDragging) return;
        if (!_layer.TryGetScreenRect(_window.Handle, out var rect)) return;
        if (IsOnAnyScreen(rect.X, rect.Y, rect.Width, rect.Height)) return;
        if (!TryGetDefaultPosition(rect.Width, rect.Height, 0, out var x, out var y)) return;

        Debug.WriteLine($"[WidgetController] '{_settings.Type}' estava fora de tela em " +
                        $"({rect.X},{rect.Y}); movido para ({x},{y})");

        _layer.MoveToScreen(_window.Handle, x, y, rect.Width, rect.Height);
        _settings.X = x;
        _settings.Y = y;
        _persist();
    }

    private static bool IsOnAnyScreen(int x, int y, int w, int h)
    {
        var rect = new System.Drawing.Rectangle(x, y, w, h);
        foreach (var screen in Forms.Screen.AllScreens)
        {
            var visible = System.Drawing.Rectangle.Intersect(screen.WorkingArea, rect);
            // Exige um pedaço agarrável de verdade, não um pixel na borda.
            if (visible.Width >= 80 && visible.Height >= 40) return true;
        }
        return false;
    }

    private void OnWatchdogTick(object? sender, EventArgs e)
    {
        if (_disposed || _window is null) return;

        var hwnd = _window.Handle;

        if (_layer.IsHealthy(hwnd))
        {
            _consecutiveRecreates = 0;
            _layer.PushToBottom(hwnd);   // no-op fora do modo BottomMost
            RescueIfOffScreen();
            return;
        }

        _consecutiveRecreates++;
        if (_consecutiveRecreates > MaxRecreatesBeforeFallback && !_giveUpAnchoring)
        {
            _giveUpAnchoring = true;
            Debug.WriteLine($"[WidgetController] '{_settings.Type}': ancoragem instável, " +
                            $"caindo para BottomMost em definitivo");
        }

        Debug.WriteLine($"[WidgetController] '{_settings.Type}': janela perdida, recriando " +
                        $"(tentativa {_consecutiveRecreates})");
        DestroyWindow();
        CreateWindow();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _widget.Dispose();
    }
}
