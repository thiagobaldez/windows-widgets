using System;
using System.Diagnostics;

namespace WidgetHost.Desktop;

public enum AttachMode
{
    /// <summary>Ainda não anexado.</summary>
    None,

    /// <summary>Filho do SHELLDLL_DefView. Único modo ancorado que renderiza no Win11 26100.</summary>
    DefView,

    /// <summary>Filho do WorkerW irmão do Progman — a técnica clássica (Win7..Win10).</summary>
    WorkerW,

    /// <summary>Top-level empurrado para o fundo da z-order. Sempre funciona, mas some no Win+D.</summary>
    BottomMost,
}

/// <summary>
/// Prende janelas na área de trabalho: acima do papel de parede e atrás de
/// todas as janelas normais.
///
/// A escada de modos é resultado de medição, não de teoria (ver Fase 0 do plano):
/// no Windows 11 build 26100 o <c>Progman</c> carrega WS_EX_NOREDIRECTIONBITMAP,
/// e janelas filhas dele ou do <c>WorkerW</c> reportam IsWindowVisible=true mas
/// não aparecem na tela. O <c>SHELLDLL_DefView</c> renderiza e recebe mouse.
/// </summary>
public sealed class DesktopLayer
{
    public AttachMode Mode { get; private set; } = AttachMode.None;

    /// <summary>Janela-alvo da ancoragem. IntPtr.Zero quando o modo é BottomMost.</summary>
    public IntPtr Target { get; private set; } = IntPtr.Zero;

    /// <summary>
    /// Anexa a janela na área de trabalho, descendo a escada até um modo que
    /// passe na verificação de sanidade.
    /// </summary>
    public AttachMode Attach(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
            throw new ArgumentException("HWND inválido", nameof(hwnd));

        var progman = EnsureWallpaperLayer();

        var defView = progman == IntPtr.Zero
            ? IntPtr.Zero
            : NativeMethods.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);

        if (TryParent(hwnd, defView, AttachMode.DefView)) return Mode;
        if (TryParent(hwnd, FindClassicWorkerW(progman), AttachMode.WorkerW)) return Mode;

        AttachBottomMost(hwnd);
        return Mode;
    }

    /// <summary>
    /// Confere se a ancoragem continua de pé. Retorna false quando o alvo morreu
    /// (reinício do explorer.exe) ou a janela se soltou.
    /// </summary>
    public bool IsHealthy(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd)) return false;

        if (Mode == AttachMode.BottomMost) return true;

        if (Target == IntPtr.Zero || !NativeMethods.IsWindow(Target)) return false;
        return NativeMethods.GetAncestor(hwnd, NativeMethods.GA_PARENT) == Target;
    }

    /// <summary>
    /// Move/redimensiona a janela usando coordenadas da TELA VIRTUAL, convertendo
    /// para o espaço do alvo quando necessário.
    ///
    /// Depois do SetParent as coordenadas passam a ser relativas ao client do
    /// alvo, cuja origem é o canto da tela virtual — que em setup multi-monitor
    /// costuma ser negativo (ex.: -1920,0). Sem essa conversão o widget cai no
    /// monitor errado.
    /// </summary>
    public void MoveToScreen(IntPtr hwnd, int screenX, int screenY, int widthPx, int heightPx)
    {
        var x = screenX;
        var y = screenY;

        if (Mode != AttachMode.BottomMost && Target != IntPtr.Zero &&
            NativeMethods.GetWindowRect(Target, out var tr))
        {
            x -= tr.Left;
            y -= tr.Top;
        }

        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, widthPx, heightPx,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    /// <summary>Posição atual da janela em coordenadas da tela virtual.</summary>
    public bool TryGetScreenRect(IntPtr hwnd, out System.Drawing.Rectangle rect)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out var r))
        {
            rect = System.Drawing.Rectangle.Empty;
            return false;
        }
        rect = System.Drawing.Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        return true;
    }

    /// <summary>
    /// Pede ao Progman para criar a camada de wallpaper. As três variantes de
    /// wParam/lParam cobrem as diferenças entre builds do Windows.
    /// </summary>
    private static IntPtr EnsureWallpaperLayer()
    {
        var progman = NativeMethods.FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return IntPtr.Zero;

        NativeMethods.SendMessageTimeout(progman, NativeMethods.WM_SPAWN_WORKER,
            IntPtr.Zero, IntPtr.Zero, NativeMethods.SMTO_NORMAL, 1000, out _);
        NativeMethods.SendMessageTimeout(progman, NativeMethods.WM_SPAWN_WORKER,
            new IntPtr(0x0D), new IntPtr(0x01), NativeMethods.SMTO_NORMAL, 1000, out _);
        NativeMethods.SendMessageTimeout(progman, NativeMethods.WM_SPAWN_WORKER,
            new IntPtr(0x0D), new IntPtr(0x00), NativeMethods.SMTO_NORMAL, 1000, out _);

        return progman;
    }

    /// <summary>
    /// Técnica clássica: o WorkerW que é IRMÃO do top-level que contém o
    /// SHELLDLL_DefView. Não existe no Win11 26100, mas existe em builds antigas.
    /// </summary>
    private static IntPtr FindClassicWorkerW(IntPtr progman)
    {
        if (progman == IntPtr.Zero) return IntPtr.Zero;

        IntPtr found = IntPtr.Zero;
        NativeMethods.EnumWindowsProc callback = (top, _) =>
        {
            if (NativeMethods.FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
            {
                var sibling = NativeMethods.FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
                if (sibling != IntPtr.Zero) found = sibling;
            }
            return true;
        };
        NativeMethods.EnumWindows(callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return found;
    }

    private bool TryParent(IntPtr hwnd, IntPtr target, AttachMode mode)
    {
        if (target == IntPtr.Zero || !NativeMethods.IsWindow(target)) return false;

        // O retorno do SetParent NÃO serve como teste de sucesso: para uma janela
        // top-level ele devolve o HWND da área de trabalho (#32769), que é
        // não-nulo justamente quando deu certo.
        NativeMethods.SetParent(hwnd, target);

        // Sem WS_CHILD o Windows trata o alvo como owner e a janela continua
        // top-level. Aplicar o estilo e reanexar.
        var style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
        if ((style & NativeMethods.WS_CHILD) == 0)
        {
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_STYLE,
                (style & ~NativeMethods.WS_POPUP) | NativeMethods.WS_CHILD);
            NativeMethods.SetParent(hwnd, target);
        }

        var ok = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_PARENT) == target;
        Debug.WriteLine($"[DesktopLayer] {mode} -> alvo 0x{target.ToInt64():X} " +
                        $"({NativeMethods.ClassOf(target)}) ok={ok}");

        if (!ok) return false;

        Mode = mode;
        Target = target;
        return true;
    }

    private void AttachBottomMost(IntPtr hwnd)
    {
        var ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
            ex | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_BOTTOM, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

        Mode = AttachMode.BottomMost;
        Target = IntPtr.Zero;
        Debug.WriteLine("[DesktopLayer] fallback BottomMost");
    }

    /// <summary>
    /// Vai direto para o fallback, sem tentar ancorar. Usado quando a ancoragem
    /// se mostrou instável em tempo de execução.
    /// </summary>
    public AttachMode AttachBottomMostOnly(IntPtr hwnd)
    {
        AttachBottomMost(hwnd);
        return Mode;
    }

    /// <summary>Reempurra para o fundo. Só faz sentido no modo BottomMost.</summary>
    public void PushToBottom(IntPtr hwnd)
    {
        if (Mode != AttachMode.BottomMost) return;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_BOTTOM, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }
}
