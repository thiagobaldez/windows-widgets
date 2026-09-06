using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Widgets.Weather.Api;

/// <summary>Busca de cidades pela API de geocoding da Open-Meteo (sem chave).</summary>
public sealed class GeocodingClient
{
    private const string Endpoint = "https://geocoding-api.open-meteo.com/v1/search";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public async Task<IReadOnlyList<GeoLocation>> SearchAsync(
        string query, int count = 8, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<GeoLocation>();

        var url = $"{Endpoint}?name={WebUtility.UrlEncode(query)}&count={count}&language=pt&format=json";
        var json = await Http.GetStringAsync(url, ct).ConfigureAwait(false);
        return Parse(json);
    }

    /// <summary>Público para teste sem rede.</summary>
    public static IReadOnlyList<GeoLocation> Parse(string json)
    {
        var dto = JsonSerializer.Deserialize<SearchResponse>(json);
        if (dto?.Results is null) return Array.Empty<GeoLocation>();

        var list = new List<GeoLocation>(dto.Results.Length);
        foreach (var r in dto.Results)
        {
            if (string.IsNullOrWhiteSpace(r.Name)) continue;
            list.Add(new GeoLocation(
                GeoLocation.Label(r.Name, r.Admin1, r.Country), r.Latitude, r.Longitude));
        }
        return list;
    }

    private sealed class SearchResponse
    {
        [JsonPropertyName("results")] public Result[]? Results { get; set; }
    }

    private sealed class Result
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("latitude")] public double Latitude { get; set; }
        [JsonPropertyName("longitude")] public double Longitude { get; set; }
        [JsonPropertyName("admin1")] public string? Admin1 { get; set; }
        [JsonPropertyName("country")] public string? Country { get; set; }
    }
}
