using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace WidgetHost.Startup;

/// <summary>
/// Liga/desliga o início automático pela chave Run do HKCU. Não precisa de
/// privilégio de administrador.
/// </summary>
public static class StartupRegistrar
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WindowsWidgets";

    /// <summary>
    /// Lê o estado REAL do registro. O usuário pode ter removido a entrada por
    /// fora (msconfig, Gerenciador de Tarefas), então o JSON não é fonte de verdade.
    /// </summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string v && v.Contains(ExecutablePath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[StartupRegistrar] leitura falhou: {ex.Message}");
            return false;
        }
    }

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null) return false;

            if (enabled)
                key.SetValue(ValueName, $"\"{ExecutablePath}\"");
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[StartupRegistrar] escrita falhou: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Caminho do .exe. Em publish single-file o Assembly.Location vem vazio,
    /// então a fonte confiável é o processo.
    /// </summary>
    private static string ExecutablePath =>
        Process.GetCurrentProcess().MainModule?.FileName ?? Environment.ProcessPath ?? "";
}
