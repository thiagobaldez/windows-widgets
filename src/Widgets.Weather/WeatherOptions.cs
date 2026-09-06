using System;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Widgets.Weather;

/// <summary>
/// Configuração do widget de tempo. O host guarda isso como um bloco JSON
/// opaco em <c>settings.json</c>; a tradução de/para JSON mora aqui, para o
/// host não precisar conhecer nenhum campo de previsão do tempo.
/// </summary>
public sealed class WeatherOptions
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? LocationName { get; set; }
    public bool UseFahrenheit { get; set; }
    public int RefreshMinutes { get; set; } = 15;

    [JsonIgnore]
    public bool HasCoordinates => Latitude.HasValue && Longitude.HasValue;

    /// <summary>Disparado quando algo muda e precisa ser persistido pelo host.</summary>
    public event EventHandler? Changed;

    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Bloco ausente ou corrompido vira o default — nunca lança.</summary>
    public static WeatherOptions FromJson(JsonObject? json)
    {
        if (json is null) return new WeatherOptions();

        try
        {
            return JsonSerializer.Deserialize<WeatherOptions>(json.ToJsonString(), Json)
                   ?? new WeatherOptions();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WeatherOptions] JSON inválido, usando default: {ex.Message}");
            return new WeatherOptions();
        }
    }

    public JsonObject ToJson()
        => JsonNode.Parse(JsonSerializer.Serialize(this, Json))?.AsObject() ?? new JsonObject();
}
