using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Widgets.Weather.Api;

/// <summary>
/// Descobre a cidade por IP, só na primeira execução. Depois disso as
/// coordenadas vêm das configurações salvas.
///
/// <para>Provedor único de propósito: <c>ipapi.co</c> foi descartado por estar
/// bloqueado por DNS na máquina de desenvolvimento. Encadear provedores de IP
/// aumenta a superfície de falha sem ganho real — se este falhar, o usuário
/// escolhe a cidade na mão.</para>
/// </summary>
public sealed class IpLocationProvider
{
    private const string Endpoint = "https://ipwho.is/";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>Retorna null quando não dá para determinar — nunca lança.</summary>
    public async Task<GeoLocation?> TryResolveAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await Http.GetStringAsync(Endpoint, ct).ConfigureAwait(false);
            return Parse(json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[IpLocationProvider] falhou: {ex.Message}");
            return null;
        }
    }

    /// <summary>Público para teste sem rede.</summary>
    public static GeoLocation? Parse(string json)
    {
        var dto = JsonSerializer.Deserialize<IpWhoResponse>(json);
        if (dto is null || !dto.Success || string.IsNullOrWhiteSpace(dto.City)) return null;

        return new GeoLocation(
            GeoLocation.ShortLabel(dto.City, dto.Region), dto.Latitude, dto.Longitude);
    }

    private sealed class IpWhoResponse
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("city")] public string? City { get; set; }
        [JsonPropertyName("region")] public string? Region { get; set; }
        [JsonPropertyName("country")] public string? Country { get; set; }
        [JsonPropertyName("latitude")] public double Latitude { get; set; }
        [JsonPropertyName("longitude")] public double Longitude { get; set; }
    }
}
