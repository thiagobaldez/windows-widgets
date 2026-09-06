using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace WidgetHost.Config;

/// <summary>
/// Lê e grava as configurações em %APPDATA%\WindowsWidgets\settings.json.
/// A gravação é atômica: fechar o app no meio de um save não pode deixar um
/// JSON pela metade.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        // Tamanho gravado como "Medium", não como 1: número no JSON quebraria
        // silenciosamente se a ordem do enum mudasse.
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    public SettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WindowsWidgets", "settings.json");
    }

    public string FilePath => _path;

    /// <summary>
    /// Carrega e migra. Arquivo ausente ou corrompido devolve o default — um
    /// JSON quebrado não pode impedir o app de subir.
    /// </summary>
    public HostSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new HostSettings();

            var root = JsonNode.Parse(File.ReadAllText(_path))?.AsObject();
            if (root is null) return new HostSettings();

            var before = root.ToJsonString();
            var migrated = SettingsMigration.Upgrade(root, () => Guid.NewGuid().ToString("N"));
            var settings = JsonSerializer.Deserialize<HostSettings>(migrated.ToJsonString(), Json)
                           ?? new HostSettings();

            // Grava a versão nova na hora: se o app morrer antes do próximo
            // save, a migração não precisa rodar de novo (e gerar outros ids).
            if (!ReferenceEquals(before, null) && before != migrated.ToJsonString()) Save(settings);

            return settings;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsStore] falha ao ler '{_path}': {ex.Message}. Usando default.");
            return new HostSettings();
        }
    }

    public void Save(HostSettings settings)
    {
        var dir = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(dir);

        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json));

        // File.Move com overwrite é atômico no mesmo volume.
        File.Move(temp, _path, overwrite: true);
    }
}
