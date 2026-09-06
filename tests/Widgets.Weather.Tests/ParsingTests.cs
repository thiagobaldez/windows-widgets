using System;
using System.Globalization;
using System.IO;
using System.Threading;
using Widgets.Weather.Api;
using Widgets.Weather.Domain;

namespace Widgets.Weather.Tests;

/// <summary>
/// Exercita o parsing contra as respostas REAIS capturadas das APIs
/// (tests/Widgets.Weather.Tests/fixtures). JSON inventado à mão testa a nossa
/// suposição do formato, não o formato de verdade.
/// </summary>
public class ParsingTests
{
    private static string Fixture(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name));

    [Fact]
    public void Resposta_real_da_OpenMeteo_vira_um_snapshot_completo()
    {
        var fetchedAt = DateTimeOffset.Parse("2026-09-05T21:30:00-03:00");
        var snapshot = OpenMeteoClient.Parse(
            Fixture("forecast-sao-paulo.json"), "São Paulo, SP", fetchedAt);

        Assert.Equal("São Paulo, SP", snapshot.LocationName);

        Assert.Equal(fetchedAt, snapshot.FetchedAt);

        // Uma requisição só traz condição atual E previsão de 7 dias.
        Assert.Equal(7, snapshot.Daily.Count);
        Assert.InRange(snapshot.HumidityPercent, 0, 100);
        Assert.InRange(snapshot.TemperatureC, -60, 60);
        Assert.NotNull(snapshot.Condition.Description);
    }

    [Fact]
    public void Cada_dia_da_previsao_tem_minima_menor_ou_igual_a_maxima()
    {
        var snapshot = OpenMeteoClient.Parse(
            Fixture("forecast-sao-paulo.json"), "São Paulo, SP", DateTimeOffset.Now);

        Assert.All(snapshot.Daily, day => Assert.True(day.MinC <= day.MaxC,
            $"{day.Date}: mínima {day.MinC} maior que a máxima {day.MaxC}"));
    }

    [Fact]
    public void Arrays_diarios_de_tamanhos_diferentes_param_no_menor_comum()
    {
        // A API pode devolver menos dias que o pedido. Percorrer pelo array
        // mais longo estouraria o índice.
        const string json = """
        {
          "current": {"temperature_2m":11.1,"apparent_temperature":8.0,"weather_code":0,
                      "is_day":0,"relative_humidity_2m":63,"wind_speed_10m":11.3},
          "daily": {
            "time":["2026-09-05","2026-09-06","2026-09-07"],
            "weather_code":[3,3],
            "temperature_2m_max":[18.2,12.3,15.0],
            "temperature_2m_min":[8.3,4.1,3.2]
          }
        }
        """;

        var snapshot = OpenMeteoClient.Parse(json, "Teste", DateTimeOffset.Now);

        Assert.Equal(2, snapshot.Daily.Count);
    }

    [Fact]
    public void Campos_do_layout_grande_saem_da_mesma_requisicao()
    {
        var snapshot = OpenMeteoClient.Parse(
            Fixture("forecast-sao-paulo.json"), "São Paulo, SP", DateTimeOffset.Now);

        var today = snapshot.Daily[0];
        Assert.NotNull(today.Sunrise);
        Assert.NotNull(today.Sunset);
        Assert.True(today.Sunrise < today.Sunset, "sol nasce antes de se pôr");
        Assert.All(snapshot.Daily, d =>
            Assert.True(d.PrecipitationProbability is null or >= 0 and <= 100));
    }

    [Fact]
    public void Url_pede_os_campos_do_layout_grande()
    {
        var url = OpenMeteoClient.BuildUrl(-23.5505, -46.6333, OpenMeteoClient.DefaultForecastDays);

        Assert.Contains("precipitation_probability_max", url);
        Assert.Contains("sunrise", url);
        Assert.Contains("sunset", url);
        Assert.Contains("forecast_days=7", url);
    }

    [Fact]
    public void Campos_opcionais_ausentes_viram_null_e_nao_zero()
    {
        // Zero falso seria pior que ausência: "0% de chuva" é uma afirmação,
        // "não sei" não é.
        const string json = """
        {
          "current": {"temperature_2m":11.1,"apparent_temperature":8.0,"weather_code":0,
                      "is_day":1,"relative_humidity_2m":63,"wind_speed_10m":11.3},
          "daily": {"time":["2026-09-05"],"weather_code":[3],
                    "temperature_2m_max":[18.2],"temperature_2m_min":[8.3]}
        }
        """;

        var day = OpenMeteoClient.Parse(json, "Teste", DateTimeOffset.Now).Daily[0];

        Assert.Null(day.PrecipitationProbability);
        Assert.Null(day.Sunrise);
        Assert.Null(day.Sunset);
    }

    [Fact]
    public void Dia_solto_com_chuva_null_nao_derruba_o_parsing()
    {
        // A API devolve null nos dias além do alcance do modelo de precipitação.
        const string json = """
        {
          "current": {"temperature_2m":11.1,"apparent_temperature":8.0,"weather_code":0,
                      "is_day":1,"relative_humidity_2m":63,"wind_speed_10m":11.3},
          "daily": {"time":["2026-09-05","2026-09-06"],"weather_code":[3,3],
                    "temperature_2m_max":[18.2,12.3],"temperature_2m_min":[8.3,4.1],
                    "precipitation_probability_max":[20,null],
                    "sunrise":["2026-09-05T06:45",null],
                    "sunset":["2026-09-05T18:22",null]}
        }
        """;

        var days = OpenMeteoClient.Parse(json, "Teste", DateTimeOffset.Now).Daily;

        Assert.Equal(20, days[0].PrecipitationProbability);
        Assert.Equal(new TimeOnly(6, 45), days[0].Sunrise);
        Assert.Null(days[1].PrecipitationProbability);
        Assert.Null(days[1].Sunrise);
    }

    [Fact]
    public void Bloco_daily_ausente_devolve_lista_vazia_em_vez_de_lancar()
    {
        const string json = """
        {"current":{"temperature_2m":11.1,"apparent_temperature":8.0,"weather_code":0,
                    "is_day":1,"relative_humidity_2m":63,"wind_speed_10m":11.3}}
        """;

        var snapshot = OpenMeteoClient.Parse(json, "Teste", DateTimeOffset.Now);

        Assert.Empty(snapshot.Daily);
        Assert.True(snapshot.IsDay);
    }

    [Fact]
    public void Resposta_sem_bloco_current_e_erro_explicito()
    {
        var ex = Assert.Throws<FormatException>(
            () => OpenMeteoClient.Parse("""{"daily":{}}""", "Teste", DateTimeOffset.Now));

        Assert.Contains("current", ex.Message);
    }

    [Fact]
    public void Url_usa_ponto_decimal_mesmo_com_cultura_pt_BR()
    {
        // Em pt-BR o separador é vírgula e a API rejeita "-29,68".
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("pt-BR");
            var url = OpenMeteoClient.BuildUrl(-23.5505, -46.6333, 5);

            Assert.Contains("latitude=-23.5505", url);
            Assert.Contains("longitude=-46.6333", url);
            Assert.DoesNotContain(",", url.Split('?')[1].Split('&')[0]);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Fact]
    public void Geocoding_real_rotula_com_estado_e_pais()
    {
        // O ranqueamento não é por proximidade nem por população pura: buscar
        // "Toledo" devolve Ohio (EUA), Filipinas e Espanha antes de Toledo (PR),
        // mesmo o Paraná tendo mais habitantes que a Toledo espanhola. Sem
        // estado e país na lista, o usuário escolhe a cidade errada.
        var results = GeocodingClient.Parse(Fixture("geocoding-toledo.json"));

        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.Contains("—", r.Name));
        Assert.Contains(results, r => r.Name.Contains("Brasil"));
    }

    [Fact]
    public void Ipwho_real_vira_uma_localizacao()
    {
        var location = IpLocationProvider.Parse(Fixture("ipwho.json"));

        Assert.NotNull(location);
        Assert.False(string.IsNullOrWhiteSpace(location!.Name));
        Assert.InRange(location.Latitude, -90, 90);
        Assert.InRange(location.Longitude, -180, 180);
    }

    [Fact]
    public void Localizacao_por_IP_omite_o_pais_mas_a_busca_por_cidade_mantem()
    {
        // O país vindo do ipwho.is vem em inglês ("Brazil") e misturaria idiomas
        // no cabeçalho. Já na busca por cidade ele é o que desambigua.
        var byIp = IpLocationProvider.Parse(Fixture("ipwho.json"));
        Assert.NotNull(byIp);
        Assert.DoesNotContain("Brazil", byIp!.Name);

        var bySearch = GeocodingClient.Parse(Fixture("geocoding-toledo.json"));
        Assert.Contains(bySearch, r => r.Name.Contains("Brasil"));
    }

    [Fact]
    public void Ipwho_com_success_false_devolve_null()
    {
        Assert.Null(IpLocationProvider.Parse("""{"success":false,"message":"Reserved range"}"""));
    }

    [Fact]
    public void Snapshot_fica_stale_depois_do_prazo()
    {
        var now = DateTimeOffset.Parse("2026-09-05T12:00:00-03:00");
        var snapshot = OpenMeteoClient.Parse(
            Fixture("forecast-sao-paulo.json"), "Teste", now.AddHours(-3));

        Assert.True(snapshot.IsStale(now, TimeSpan.FromHours(2)));
        Assert.False(snapshot.IsStale(now, TimeSpan.FromHours(4)));
    }
}

