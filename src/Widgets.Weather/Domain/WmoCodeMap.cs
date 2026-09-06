namespace Widgets.Weather.Domain;

/// <summary>Família visual da condição. Define qual ícone é desenhado.</summary>
public enum WeatherIconKind
{
    Clear,
    MainlyClear,
    PartlyCloudy,
    Overcast,
    Fog,
    Drizzle,
    FreezingRain,
    Rain,
    Showers,
    Snow,
    SnowGrains,
    SnowShowers,
    Thunderstorm,
    ThunderstormHail,
    Unknown,
}

public sealed record WeatherCondition(WeatherIconKind Icon, string Description);

/// <summary>
/// Tradução dos códigos WMO 4677 que a Open-Meteo devolve em
/// <c>weather_code</c>. Cobre 0..99: qualquer código fora da tabela cai em
/// <see cref="WeatherIconKind.Unknown"/> — um código novo da API não pode
/// derrubar o widget.
/// </summary>
public static class WmoCodeMap
{
    public static WeatherCondition Describe(int code) => code switch
    {
        0 => new(WeatherIconKind.Clear, "Céu limpo"),
        1 => new(WeatherIconKind.MainlyClear, "Predominantemente limpo"),
        2 => new(WeatherIconKind.PartlyCloudy, "Parcialmente nublado"),
        3 => new(WeatherIconKind.Overcast, "Encoberto"),

        45 => new(WeatherIconKind.Fog, "Nevoeiro"),
        48 => new(WeatherIconKind.Fog, "Nevoeiro com geada"),

        51 => new(WeatherIconKind.Drizzle, "Garoa fraca"),
        53 => new(WeatherIconKind.Drizzle, "Garoa moderada"),
        55 => new(WeatherIconKind.Drizzle, "Garoa forte"),
        56 => new(WeatherIconKind.FreezingRain, "Garoa congelante fraca"),
        57 => new(WeatherIconKind.FreezingRain, "Garoa congelante forte"),

        61 => new(WeatherIconKind.Rain, "Chuva fraca"),
        63 => new(WeatherIconKind.Rain, "Chuva moderada"),
        65 => new(WeatherIconKind.Rain, "Chuva forte"),
        66 => new(WeatherIconKind.FreezingRain, "Chuva congelante fraca"),
        67 => new(WeatherIconKind.FreezingRain, "Chuva congelante forte"),

        71 => new(WeatherIconKind.Snow, "Neve fraca"),
        73 => new(WeatherIconKind.Snow, "Neve moderada"),
        75 => new(WeatherIconKind.Snow, "Neve forte"),
        77 => new(WeatherIconKind.SnowGrains, "Grãos de neve"),

        80 => new(WeatherIconKind.Showers, "Pancadas fracas"),
        81 => new(WeatherIconKind.Showers, "Pancadas moderadas"),
        82 => new(WeatherIconKind.Showers, "Pancadas violentas"),
        85 => new(WeatherIconKind.SnowShowers, "Pancadas de neve fracas"),
        86 => new(WeatherIconKind.SnowShowers, "Pancadas de neve fortes"),

        95 => new(WeatherIconKind.Thunderstorm, "Trovoada"),
        96 => new(WeatherIconKind.ThunderstormHail, "Trovoada com granizo fraco"),
        99 => new(WeatherIconKind.ThunderstormHail, "Trovoada com granizo forte"),

        _ => new(WeatherIconKind.Unknown, "Condição desconhecida"),
    };
}
