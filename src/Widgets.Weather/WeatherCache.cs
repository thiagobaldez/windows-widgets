using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Widgets.Weather.Domain;

namespace Widgets.Weather;

/// <summary>
/// Guarda a última leitura boa em disco.
///
/// <para>Serve para o widget abrir com conteúdo mesmo sem rede: em vez de um
/// retângulo vazio piscando, mostra o último estado conhecido com marcação de
/// desatualizado.</para>
/// </summary>
public sealed class WeatherCache
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly string _path;

    public WeatherCache(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WindowsWidgets", "cache", "weather.json");
    }

    public WeatherSnapshot? TryLoad()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            return JsonSerializer.Deserialize<WeatherSnapshot>(File.ReadAllText(_path), Json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WeatherCache] leitura falhou: {ex.Message}");
            return null;
        }
    }

    public void Save(WeatherSnapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(snapshot, Json));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            // Cache é otimização: falhar aqui não pode quebrar a atualização.
            Debug.WriteLine($"[WeatherCache] escrita falhou: {ex.Message}");
        }
    }
}
