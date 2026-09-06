using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Widgets.Weather.Domain;

/// <summary>
/// Modelo próprio do widget. Os DTOs da Open-Meteo são traduzidos para cá na
/// fronteira da API — a View nunca toca no formato de terceiro.
/// </summary>
public sealed record WeatherSnapshot(
    string LocationName,
    double TemperatureC,
    double ApparentC,
    int HumidityPercent,
    double WindKmh,
    int WeatherCode,
    bool IsDay,
    IReadOnlyList<DailyForecast> Daily,
    DateTimeOffset FetchedAt)
{
    // Derivada de WeatherCode: guardar no cache seria duplicar a fonte da verdade.
    [JsonIgnore]
    public WeatherCondition Condition => WmoCodeMap.Describe(WeatherCode);

    /// <summary>Ficou velho demais para ser apresentado sem ressalva.</summary>
    public bool IsStale(DateTimeOffset now, TimeSpan maxAge) => now - FetchedAt > maxAge;
}

/// <summary>
/// Um dia da previsão. Os campos opcionais só aparecem no layout grande, mas
/// vêm na mesma requisição — pedir menos não economizaria nada e exigiria
/// lógica de API por tamanho de widget.
/// </summary>
public sealed record DailyForecast(
    DateOnly Date,
    int WeatherCode,
    double MinC,
    double MaxC,
    int? PrecipitationProbability = null,
    TimeOnly? Sunrise = null,
    TimeOnly? Sunset = null)
{
    [JsonIgnore]
    public WeatherCondition Condition => WmoCodeMap.Describe(WeatherCode);
}
