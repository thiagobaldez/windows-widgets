using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using WidgetHost.Desktop;
using WidgetHost.Widgets;

namespace WidgetHost.Shell;

/// <summary>
/// Ícone na bandeja. É a única forma de chegar no app: os widgets não aparecem
/// na barra de tarefas nem no Alt+Tab.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _modeItem;

    public event EventHandler? RefreshAllRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler<bool>? StartWithWindowsChanged;
    public event EventHandler<WidgetType>? AddWidgetRequested;

    public TrayIcon(IReadOnlyList<WidgetType> types)
    {
        var menu = new ContextMenuStrip();

        // Submenu de tipos: é por aqui que se coloca uma segunda instância do
        // mesmo widget (duas cidades, por exemplo).
        var add = new ToolStripMenuItem("Adicionar widget");
        foreach (var type in types)
        {
            var captured = type;
            add.DropDownItems.Add(new ToolStripMenuItem(captured.DisplayName,
                null, (_, _) => AddWidgetRequested?.Invoke(this, captured)));
        }
        menu.Items.Add(add);

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(new ToolStripMenuItem("Atualizar tudo",
            null, (_, _) => RefreshAllRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(new ToolStripMenuItem("Configurações...",
            null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty)));

        menu.Items.Add(new ToolStripSeparator());

        _startupItem = new ToolStripMenuItem("Iniciar com o Windows") { CheckOnClick = true };
        _startupItem.CheckedChanged += (_, _) => StartWithWindowsChanged?.Invoke(this, _startupItem.Checked);
        menu.Items.Add(_startupItem);

        // Diagnóstico: qual modo de ancoragem acabou valendo. Sem isso, investigar
        // "o widget sumiu" numa máquina diferente vira adivinhação.
        _modeItem = new ToolStripMenuItem("Ancoragem: —") { Enabled = false };
        menu.Items.Add(_modeItem);

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(new ToolStripMenuItem("Sair",
            null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty)));

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Visible = true,
            Text = "Windows Widgets",
            ContextMenuStrip = menu,
        };
    }

    /// <summary>Reflete o estado real do registro, não o que está salvo no JSON.</summary>
    public void SetStartupChecked(bool value)
    {
        if (_startupItem.Checked != value) _startupItem.Checked = value;
    }

    public void SetAttachMode(AttachMode mode) => _modeItem.Text = $"Ancoragem: {mode}";

    /// <summary>
    /// Carrega o app.ico embarcado pedindo o tamanho de ícone pequeno do
    /// sistema: o .ico tem quadros de 16 a 256, e deixar o Windows escalar um
    /// 32x32 para a bandeja borra os raios do sol.
    /// </summary>
    private static Icon LoadIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/app.ico", UriKind.Absolute);
            using var stream = System.Windows.Application.GetResourceStream(uri)?.Stream;
            if (stream is not null) return new Icon(stream, SystemInformation.SmallIconSize);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TrayIcon] falha ao carregar app.ico: {ex.Message}");
        }

        return SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
