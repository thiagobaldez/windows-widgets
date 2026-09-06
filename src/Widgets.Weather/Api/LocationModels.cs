namespace Widgets.Weather.Api;

/// <summary>Um lugar resolvido, pronto para virar coordenadas de consulta.</summary>
public sealed record GeoLocation(string Name, double Latitude, double Longitude)
{
    /// <summary>
    /// Rótulo desambiguado. O geocoding da Open-Meteo ranqueia por população,
    /// não por proximidade: buscar "Santa Maria" devolve Oaxaca de Juárez (MX)
    /// antes de Santa Maria (BR). Sem estado e país na lista, o usuário escolhe
    /// a cidade errada.
    /// </summary>
    public static string Label(string name, string? admin1, string? country)
    {
        if (!string.IsNullOrWhiteSpace(admin1) && !string.IsNullOrWhiteSpace(country))
            return $"{name} — {admin1}, {country}";
        if (!string.IsNullOrWhiteSpace(country))
            return $"{name} — {country}";
        return name;
    }

    /// <summary>
    /// Rótulo sem país, para a localização detectada por IP. Ali o país é ruído
    /// (é o do próprio usuário) e vem em inglês do provedor — "Santa Maria,
    /// Rio Grande do Sul, Brazil" mistura idiomas no meio de uma interface em
    /// português. Na busca por cidade o país continua sendo exibido, porque lá
    /// ele é o que desambigua.
    /// </summary>
    public static string ShortLabel(string name, string? admin1)
        => string.IsNullOrWhiteSpace(admin1) ? name : $"{name}, {admin1}";
}
