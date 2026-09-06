using Widgets.Weather.Domain;

namespace Widgets.Weather.Tests;

public class WmoCodeMapTests
{
    [Fact]
    public void Todos_os_codigos_0_a_99_resolvem_sem_lancar()
    {
        for (var code = 0; code <= 99; code++)
        {
            var condition = WmoCodeMap.Describe(code);
            Assert.False(string.IsNullOrWhiteSpace(condition.Description));
        }
    }

    [Fact]
    public void Codigo_fora_da_tabela_cai_em_Unknown_em_vez_de_lancar()
    {
        // Um código novo na API não pode derrubar o widget.
        Assert.Equal(WeatherIconKind.Unknown, WmoCodeMap.Describe(4).Icon);
        Assert.Equal(WeatherIconKind.Unknown, WmoCodeMap.Describe(-1).Icon);
        Assert.Equal(WeatherIconKind.Unknown, WmoCodeMap.Describe(12345).Icon);
    }

    [Theory]
    [InlineData(0, WeatherIconKind.Clear)]
    [InlineData(2, WeatherIconKind.PartlyCloudy)]
    [InlineData(3, WeatherIconKind.Overcast)]
    [InlineData(45, WeatherIconKind.Fog)]
    [InlineData(48, WeatherIconKind.Fog)]
    [InlineData(53, WeatherIconKind.Drizzle)]
    [InlineData(57, WeatherIconKind.FreezingRain)]
    [InlineData(63, WeatherIconKind.Rain)]
    [InlineData(67, WeatherIconKind.FreezingRain)]
    [InlineData(73, WeatherIconKind.Snow)]
    [InlineData(77, WeatherIconKind.SnowGrains)]
    [InlineData(81, WeatherIconKind.Showers)]
    [InlineData(86, WeatherIconKind.SnowShowers)]
    [InlineData(95, WeatherIconKind.Thunderstorm)]
    [InlineData(99, WeatherIconKind.ThunderstormHail)]
    public void Cada_faixa_mapeia_para_a_familia_certa(int code, WeatherIconKind expected)
        => Assert.Equal(expected, WmoCodeMap.Describe(code).Icon);

    [Fact]
    public void Codigos_pares_de_chuva_congelante_nao_sao_confundidos_com_chuva()
    {
        // 66/67 ficam no meio da faixa de chuva (61..65) e são o erro clássico
        // de quem copia a tabela WMO pela metade.
        Assert.Equal(WeatherIconKind.FreezingRain, WmoCodeMap.Describe(66).Icon);
        Assert.Equal(WeatherIconKind.FreezingRain, WmoCodeMap.Describe(67).Icon);
        Assert.Equal(WeatherIconKind.Rain, WmoCodeMap.Describe(65).Icon);
    }

    [Fact]
    public void Descricoes_estao_em_portugues()
    {
        Assert.Equal("Céu limpo", WmoCodeMap.Describe(0).Description);
        Assert.Equal("Encoberto", WmoCodeMap.Describe(3).Description);
        Assert.Equal("Trovoada", WmoCodeMap.Describe(95).Description);
    }
}
