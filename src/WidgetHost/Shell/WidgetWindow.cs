using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using WidgetHost.Desktop;
using Widgets.Abstractions;

namespace WidgetHost.Shell;

/// <summary>
/// Moldura genérica de um widget: janela transparente, sem chrome, fora da
/// barra de tarefas, arrastável, redimensionável por escala e com menu de
/// contexto. Não sabe nada sobre o conteúdo que hospeda.
/// </summary>
public sealed class WidgetWindow : Window
{
    /// <summary>Limites de escala. Abaixo o texto some; acima vira pôster.</summary>
    public const double MinScale = 0.6;
    public const double MaxScale = 2.5;

    /// <summary>Área clicável do grip. Generosa de propósito — o alvo visual é menor.</summary>
    private const double GripHitSize = 34;

    /// <summary>Tamanho do desenho dentro da área clicável.</summary>
    private const double GripGlyphSize = 12;

    /// <summary>
    /// Afastamento do canto. O cartão tem canto arredondado: encostado na
    /// quina, o desenho cai em pixel transparente e fica invisível.
    /// </summary>
    private const double GripInset = 7;

    /// <summary>Visível sempre, discreto. Acende no hover.</summary>
    private const double GripIdleOpacity = 0.38;

    private readonly DesktopLayer _layer;
    private readonly Size _designSize;
    private readonly IReadOnlyList<WidgetSize> _supportedSizes;
    private readonly Border _grip;

    private bool _dragging;
    private NativeMethods.POINT _dragCursorStart;
    private int _dragWindowStartX, _dragWindowStartY;

    private bool _resizing;
    private NativeMethods.POINT _resizeCursorStart;
    private double _resizeScaleStart;

    public event EventHandler? RefreshRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? CloseRequested;
    public event EventHandler? LockToggled;

    /// <summary>Disparado ao soltar o arrasto, com a posição final em pixels da tela virtual.</summary>
    public event EventHandler<(int X, int Y)>? Moved;

    /// <summary>Disparado ao soltar o grip, com a escala final.</summary>
    public event EventHandler<double>? Scaled;

    /// <summary>
    /// O usuário escolheu outra variante de layout. Quem responde é o
    /// controller: trocar de layout significa recriar a janela com outra View
    /// e outro tamanho de projeto.
    /// </summary>
    public event EventHandler<WidgetSize>? SizeVariantRequested;

    public WidgetWindow(
        DesktopLayer layer,
        FrameworkElement view,
        Size designSize,
        double scale,
        WidgetSize currentSize,
        IReadOnlyList<WidgetSize> supportedSizes)
    {
        _layer = layer;
        _designSize = designSize;
        _supportedSizes = supportedSizes;
        CurrentSize = currentSize;
        Scale = ClampScale(scale);

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;   // o redimensionamento é nosso, pelo grip
        Topmost = false;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;

        // Fora da tela até a ancoragem terminar, para não piscar na posição errada.
        Left = -32000;
        Top = -32000;
        ApplyScaleToWindowSize();

        _grip = BuildGrip();

        // A View recebe o tamanho de PROJETO fixo, e o Viewbox só escala esse
        // resultado. Sem isso o Viewbox mede o filho com largura infinita, e um
        // layout que depende da largura disponível (rodapé com um item à
        // esquerda e outro à direita no mesmo Grid) se sobrepõe: cada um pede o
        // que quer, ninguém reserva espaço para os dois.
        view.Width = designSize.Width;
        view.Height = designSize.Height;

        // Viewbox escala TUDO uniformemente — tipografia, ícones, espaçamentos.
        // É o que faz um widget novo ganhar zoom de graça, sem implementar
        // nenhuma regra de reflow.
        Content = new Grid
        {
            Children =
            {
                new Viewbox { Stretch = Stretch.Uniform, Child = view },
                _grip,
            },
        };

        ContextMenu = BuildMenu();

        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;

        UpdateGripVisibility();
    }

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    public double Scale { get; private set; }

    /// <summary>Variante de layout que esta janela está exibindo.</summary>
    public WidgetSize CurrentSize { get; }

    public bool Locked { get; set; }

    private static string LabelFor(WidgetSize size) => size switch
    {
        WidgetSize.Small => "Pequeno",
        WidgetSize.Large => "Grande",
        _ => "Médio",
    };

    /// <summary>True enquanto o usuário arrasta ou redimensiona. Reposicionar
    /// nessas horas brigaria com o mouse.</summary>
    public bool IsDragging => _dragging || _resizing;

    /// <summary>Tamanho atual convertido de DIPs para pixels físicos.</summary>
    public (int Width, int Height) PixelSize
    {
        get
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            return ((int)Math.Round(Width * dpi.DpiScaleX), (int)Math.Round(Height * dpi.DpiScaleY));
        }
    }

    public static double ClampScale(double scale)
        => double.IsFinite(scale) ? Math.Clamp(scale, MinScale, MaxScale) : 1.0;

    private void ApplyScaleToWindowSize()
    {
        Width = Math.Round(_designSize.Width * Scale);
        Height = Math.Round(_designSize.Height * Scale);
    }

    private Border BuildGrip()
    {
        // Três traços diagonais crescentes — a convenção que todo mundo já
        // reconhece como "arraste para redimensionar".
        var lines = new GeometryGroup();
        for (var i = 1; i <= 3; i++)
        {
            var offset = i * (GripGlyphSize / 3);
            lines.Children.Add(new LineGeometry(
                new Point(GripGlyphSize - offset, GripGlyphSize),
                new Point(GripGlyphSize, GripGlyphSize - offset)));
        }

        var glyph = new Path
        {
            Data = lines,
            Stroke = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, GripInset, GripInset),
        };

        var grip = new Border
        {
            Width = GripHitSize,
            Height = GripHitSize,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = Brushes.Transparent,   // precisa de fundo para receber hit-test
            Cursor = Cursors.SizeNWSE,
            Opacity = GripIdleOpacity,
            Child = glyph,
        };

        // Acende no hover do próprio grip. Não depende de MouseEnter da janela:
        // um alvo que só existe depois de acertá-lo não é um alvo.
        grip.MouseEnter += (_, _) => UpdateGripVisibility();
        grip.MouseLeave += (_, _) => UpdateGripVisibility();
        grip.MouseLeftButtonDown += OnGripMouseDown;
        return grip;
    }

    private void UpdateGripVisibility()
    {
        if (Locked)
        {
            _grip.Visibility = Visibility.Collapsed;
            return;
        }

        _grip.Visibility = Visibility.Visible;
        _grip.Opacity = _grip.IsMouseOver || _resizing ? 1.0 : GripIdleOpacity;
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();

        var refresh = new MenuItem { Header = "Atualizar agora" };
        refresh.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(refresh);

        var settings = new MenuItem { Header = "Configurações..." };
        settings.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(settings);

        menu.Items.Add(new Separator());

        // Variantes de LAYOUT — cada uma mostra um conjunto diferente de
        // informação. Só aparecem as que o widget realmente oferece.
        if (_supportedSizes.Count > 1)
        {
            var layout = new MenuItem { Header = "Tamanho" };
            foreach (var value in _supportedSizes)
            {
                var captured = value;
                var item = new MenuItem
                {
                    Header = LabelFor(captured),
                    IsCheckable = true,
                    IsChecked = captured == CurrentSize,
                };
                item.Click += (_, _) => SizeVariantRequested?.Invoke(this, captured);
                layout.Items.Add(item);
            }
            menu.Items.Add(layout);
        }

        // Zoom é outra coisa: mesmo layout, maior ou menor.
        var zoom = new MenuItem { Header = "Zoom" };
        foreach (var (label, value) in new[]
                 {
                     ("75%", 0.75),
                     ("100%", 1.0),
                     ("150%", 1.5),
                     ("200%", 2.0),
                 })
        {
            var captured = value;
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => SetScale(captured, persist: true);
            zoom.Items.Add(item);
        }
        menu.Items.Add(zoom);

        var locked = new MenuItem { Header = "Bloquear posição", IsCheckable = true };
        locked.Loaded += (_, _) => locked.IsChecked = Locked;
        locked.Click += (_, _) =>
        {
            Locked = locked.IsChecked;
            UpdateGripVisibility();
            LockToggled?.Invoke(this, EventArgs.Empty);
        };
        menu.Items.Add(locked);

        menu.Items.Add(new Separator());

        var close = new MenuItem { Header = "Fechar widget" };
        close.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(close);

        return menu;
    }

    /// <summary>Aplica uma escala nova mantendo o canto superior esquerdo ancorado.</summary>
    public void SetScale(double scale, bool persist)
    {
        if (Locked) return;

        Scale = ClampScale(scale);
        ApplyScaleToWindowSize();

        if (_layer.TryGetScreenRect(Handle, out var rect))
        {
            var (w, h) = PixelSize;
            _layer.MoveToScreen(Handle, rect.X, rect.Y, w, h);
        }

        if (persist) Scaled?.Invoke(this, Scale);
    }

    // O arrasto é feito na mão, em coordenadas de tela, em vez de DragMove():
    // depois do SetParent a janela é filha do shell, e DragMove opera no espaço
    // de coordenadas do pai — que aqui tem origem negativa em multi-monitor.
    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Locked || _resizing) return;
        if (!NativeMethods.GetCursorPos(out _dragCursorStart)) return;
        if (!_layer.TryGetScreenRect(Handle, out var r)) return;

        _dragWindowStartX = r.X;
        _dragWindowStartY = r.Y;
        _dragging = true;
        CaptureMouse();
    }

    private void OnGripMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Locked) return;
        if (!NativeMethods.GetCursorPos(out _resizeCursorStart)) return;

        _resizeScaleStart = Scale;
        _resizing = true;
        UpdateGripVisibility();
        CaptureMouse();

        // Sem isso o arrasto da janela inteira começaria junto.
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!NativeMethods.GetCursorPos(out var now)) return;

        if (_resizing)
        {
            // A largura manda: aspecto travado, então a altura acompanha.
            var dpi = VisualTreeHelper.GetDpi(this);
            var startWidthPx = _designSize.Width * _resizeScaleStart * dpi.DpiScaleX;
            var newWidthPx = startWidthPx + (now.X - _resizeCursorStart.X);

            SetScale(newWidthPx / (_designSize.Width * dpi.DpiScaleX), persist: false);
            return;
        }

        if (!_dragging) return;

        var (w, h) = PixelSize;
        _layer.MoveToScreen(Handle,
            _dragWindowStartX + (now.X - _dragCursorStart.X),
            _dragWindowStartY + (now.Y - _dragCursorStart.Y),
            w, h);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_resizing)
        {
            _resizing = false;
            ReleaseMouseCapture();
            UpdateGripVisibility();
            Scaled?.Invoke(this, Scale);
            return;
        }

        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();

        if (_layer.TryGetScreenRect(Handle, out var r))
            Moved?.Invoke(this, (r.X, r.Y));
    }
}
