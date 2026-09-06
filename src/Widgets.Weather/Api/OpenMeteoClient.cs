using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Widgets.Weather.Domain;

namespace Widgets.Weather.Api;

/// <summary>
/// Cliente da Open-Meteo. Uma única requisição traz condição atual e previsão
/// diária — não existe motivo para bater em dois endpoints.
/// Sem chave de API, sem cadastro.
/// </summary>
public sealed class OpenMeteoClient
{
    private const string Endpoint = "https://api.open-meteo.com/v1/forecast";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// 7 dias por padrão: o layout grande mostra 6 dias além de hoje. Os
    /// layouts menores ignoram o excedente — pedir menos não economizaria nada
    /// e exigiria lógica de API por tamanho de widget.
    /// </summary>
    public const int DefaultForecastDays = 7;

    public async Task<WeatherSnapshot> GetAsync(
        double latitude, double longitude, string locationName,
        int forecastDays = DefaultForecastDays, CancellationToken ct = default)
    {
        var url = BuildUrl(latitude, longitude, forecastDays);
        var json = await Http.GetStringAsync(url, ct).ConfigureAwait(false);
        return Parse(json, locationName, DateTimeOffset.Now);
    }

    internal static string BuildUrl(double latitude, double longitude, int forecastDays)
    {
        // InvariantCulture obrigatório: em pt-BR o separador decimal é vírgula
        // e a API rejeita "‑29,68".
        var lat = latitude.ToString(CultureInfo.InvariantCulture);
        var lon = longitude.ToString(CultureInfo.InvariantCulture);

        return $"{Endpoint}?latitude={lat}&longitude={lon}" +
               "&current=temperature_2m,apparent_temperature,weather_code,is_day," +
               "relative_humidity_2m,wind_speed_10m" +
               "&daily=weather_code,temperature_2m_max,temperature_2m_min," +
               "precipitation_probability_max,sunrise,sunset" +
               $"&timezone=auto&forecast_days={forecastDays}";
    }

    /// <summary>
    /// Traduz a resposta da API para o modelo do widget. Público para os testes
    /// poderem exercitar o parsing com fixtures reais, sem rede.
    /// </summary>
    public static WeatherSnapshot Parse(string json, string locationName, DateTimeOffset fetchedAt)
    {
        var dto = JsonSerializer.Deserialize<ForecastResponse>(json)
                  ?? throw new FormatException("Resposta vazia da Open-Meteo.");

        if (dto.Current is null)
            throw new FormatException("Resposta da Open-Meteo sem o bloco 'current'.");

        return new WeatherSnapshot(
            LocationName: locationName,
            TemperatureC: dto.Current.Temperature,
            ApparentC: dto.Current.ApparentTemperature,
            HumidityPercent: dto.Current.Humidity,
            WindKmh: dto.Current.WindSpeed,
            WeatherCode: dto.Current.WeatherCode,
            IsDay: dto.Current.IsDay != 0,
            Daily: BuildDaily(dto.Daily),
            FetchedAt: fetchedAt);
    }

    /// <summary>
    /// Os arrays de <c>daily</c> são paralelos e independentes. A API pode
    /// devolver menos dias que o pedido (ou arrays de tamanhos diferentes),
    /// então percorre-se só até o menor comprimento comum.
    /// </summary>
    private static IReadOnlyList<DailyForecast> BuildDaily(DailyBlock? daily)
    {
        if (daily?.Time is null || daily.WeatherCode is null ||
            daily.MaxTemperature is null || daily.MinTemperature is null)
            return Array.Empty<DailyForecast>();

        var count = Math.Min(daily.Time.Length,
                    Math.Min(daily.WeatherCode.Length,
                    Math.Min(daily.MaxTemperature.Length, daily.MinTemperature.Length)));

        var result = new List<DailyForecast>(count);
        for (var i = 0; i < count; i++)
        {
            if (!DateOnly.TryParse(daily.Time[i], CultureInfo.InvariantCulture, out var date))
                continue;

            result.Add(new DailyForecast(
                date,
                daily.WeatherCode[i],
                daily.MinTemperature[i],
                daily.MaxTemperature[i],
                // Campos opcionais: a API pode omitir o array inteiro ou trazer
                // null num dia solto (probabilidade de chuva além do alcance
                // do modelo). Ausência não pode virar exceção nem zero falso.
                At(daily.PrecipitationProbability, i),
                ParseTime(At(daily.Sunrise, i)),
                ParseTime(At(daily.Sunset, i))));
        }
        return result;
    }

    private static T? At<T>(T?[]? array, int index) where T : struct
        => array is not null && index < array.Length ? array[index] : null;

    private static string? At(string?[]? array, int index)
        => array is not null && index < array.Length ? array[index] : null;

    /// <summary>
    /// A API devolve nascer/pôr como ISO local ("2026-09-05T06:42"), já no
    /// fuso pedido por <c>timezone=auto</c> — só a hora interessa.
    /// </summary>
    private static TimeOnly? ParseTime(string? isoLocal)
    {
        if (string.IsNullOrWhiteSpace(isoLocal)) return null;
        return DateTime.TryParse(isoLocal, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var value)
            ? TimeOnly.FromDateTime(value)
            : null;
    }

    private sealed class ForecastResponse
    {
        [JsonPropertyName("current")] public CurrentBlock? Current { get; set; }
        [JsonPropertyName("daily")] public DailyBlock? Daily { get; set; }
    }

    private sealed class CurrentBlock
    {
        [JsonPropertyName("temperature_2m")] public double Temperature { get; set; }
        [JsonPropertyName("apparent_temperature")] public double ApparentTemperature { get; set; }
        [JsonPropertyName("weather_code")] public int WeatherCode { get; set; }
        [JsonPropertyName("is_day")] public int IsDay { get; set; }
        [JsonPropertyName("relative_humidity_2m")] public int Humidity { get; set; }
        [JsonPropertyName("wind_speed_10m")] public double WindSpeed { get; set; }
    }

    private sealed class DailyBlock
    {
        [JsonPropertyName("time")] public string[]? Time { get; set; }
        [JsonPropertyName("weather_code")] public int[]? WeatherCode { get; set; }
        [JsonPropertyName("temperature_2m_max")] public double[]? MaxTemperature { get; set; }
        [JsonPropertyName("temperature_2m_min")] public double[]? MinTemperature { get; set; }

        [JsonPropertyName("precipitation_probability_max")]
        public int?[]? PrecipitationProbability { get; set; }

        [JsonPropertyName("sunrise")] public string?[]? Sunrise { get; set; }
        [JsonPropertyName("sunset")] public string?[]? Sunset { get; set; }
    }
}
