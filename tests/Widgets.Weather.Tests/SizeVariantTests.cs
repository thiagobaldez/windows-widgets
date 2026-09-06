using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Widgets.Abstractions;
using Widgets.Weather;
using Widgets.Weather.Domain;

namespace Widgets.Weather.Tests;

/// <summary>
/// As três variantes mostram conjuntos DIFERENTES de informação — não é zoom.
/// Se as coleções por trás delas saírem erradas, o layout grande mente por
/// omissão e o médio repete o que já está na barra.
/// </summary>
public class SizeVariantTests : IDisposable
{
    private readonly string _cachePath =
        Path.Combine(Path.GetTempPath(), $"weather-variant-{Guid.NewGuid():N}.json");

    /// <param name="startOffsetDays">
    /// Onde a previsão começa em relação a hoje. -1 simula o snapshot buscado
    /// ontem à noite que sobreviveu à virada da meia-noite.
    /// </param>
    private WeatherViewModel BuildViewModel(int forecastDays, int startOffsetDays = 0)
    {
        var daily = new List<DailyForecast>();
        for (var i = 0; i < forecastDays; i++)
        {
            daily.Add(new DailyForecast(
                DateOnly.FromDateTime(DateTime.Today.AddDays(startOffsetDays + i)),
                WeatherCode: 3,
                MinC: 5 + i,
                MaxC: 15 + i,
                PrecipitationProbability: i * 10,
                Sunrise: new TimeOnly(6, 45),
                Sunset: new TimeOnly(18, 22)));
        }

        var snapshot = new WeatherSnapshot("Teste, Estado", 10, 8, 60, 9, 0, true, daily, DateTimeOffset.Now);
        var cache = new WeatherCache(_cachePath);
        cache.Save(snapshot);

        return new WeatherViewModel(
            new WeatherOptions { Latitude = -23.5, Longitude = -46.6 }, cache);
    }

    [Fact]
    public void Medio_mostra_4_dias_e_grande_mostra_6()
    {
        using var vm = BuildViewModel(forecastDays: 7);

        Assert.Equal(4, vm.Daily.Count);
        Assert.Equal(6, vm.DailyExtended.Count);
    }

    [Fact]
    public void Nenhuma_das_duas_listas_repete_hoje()
    {
        // Hoje já é a barra de faixa. Repetir seria ruído nos dois layouts.
        using var vm = BuildViewModel(forecastDays: 7);

        Assert.DoesNotContain(vm.Daily, d => d.Weekday == "Hoje");
        Assert.DoesNotContain(vm.DailyExtended, d => d.Weekday == "Hoje");
    }

    [Fact]
    public void Medio_e_um_prefixo_do_grande()
    {
        // Os mesmos dias, na mesma ordem: mudar de tamanho não pode reordenar
        // nem trocar o que já estava na tela.
        using var vm = BuildViewModel(forecastDays: 7);

        Assert.Equal(
            vm.DailyExtended.Take(4).Select(d => d.Weekday),
            vm.Daily.Select(d => d.Weekday));
    }

    [Fact]
    public void Previsao_curta_nao_estoura_nenhum_dos_layouts()
    {
        // A API pode devolver menos dias que o pedido.
        using var vm = BuildViewModel(forecastDays: 3);

        Assert.Equal(2, vm.Daily.Count);
        Assert.Equal(2, vm.DailyExtended.Count);
    }

    [Fact]
    public void Chuva_ausente_esconde_o_campo_em_vez_de_mostrar_zero()
    {
        var daily = new List<DailyForecast>
        {
            new(DateOnly.FromDateTime(DateTime.Today), 3, 5, 15),
            new(DateOnly.FromDateTime(DateTime.Today.AddDays(1)), 3, 5, 15),
        };
        var cache = new WeatherCache(_cachePath);
        cache.Save(new WeatherSnapshot("T", 10, 8, 60, 9, 0, true, daily, DateTimeOffset.Now));

        using var vm = new WeatherViewModel(
            new WeatherOptions { Latitude = -23.5, Longitude = -46.6 }, cache);

        var day = vm.DailyExtended[0];
        Assert.Null(day.Precipitation);
        Assert.False(day.HasPrecipitation);
    }

    [Fact]
    public void Nascer_e_por_do_sol_vem_de_hoje()
    {
        using var vm = BuildViewModel(forecastDays: 7);

        Assert.True(vm.HasSunTimes);
        Assert.Equal("06:45", vm.Sunrise);
        Assert.Equal("18:22", vm.Sunset);
    }

    [Fact]
    public void Sol_ausente_desliga_a_linha_inteira()
    {
        var daily = new List<DailyForecast> { new(DateOnly.FromDateTime(DateTime.Today), 3, 5, 15) };
        var cache = new WeatherCache(_cachePath);
        cache.Save(new WeatherSnapshot("T", 10, 8, 60, 9, 0, true, daily, DateTimeOffset.Now));

        using var vm = new WeatherViewModel(
            new WeatherOptions { Latitude = -23.5, Longitude = -46.6 }, cache);

        Assert.False(vm.HasSunTimes);
        Assert.Equal("", vm.Sunrise);
    }

    [Fact]
    public void Snapshot_buscado_ontem_nao_rotula_ontem_como_hoje()
    {
        // O widget fica aberto a noite inteira. Um snapshot das 23:57 tem
        // Daily[0] = ontem depois da virada; usar o índice em vez da data
        // mostraria a faixa de ontem rotulada "hoje" e deixaria o dia de hoje
        // sobrando na tira.
        using var vm = BuildViewModel(forecastDays: 7, startOffsetDays: -1);

        Assert.DoesNotContain(vm.Daily, d => d.Weekday == "Hoje");
        Assert.DoesNotContain(vm.DailyExtended, d => d.Weekday == "Hoje");

        // A barra usa a entrada de hoje (índice 1), cuja faixa é 6..16.
        Assert.True(vm.HasTodayRange);
        Assert.Equal("6°", vm.TodayMin);
        Assert.Equal("16°", vm.TodayMax);

        // Restam 5 dias à frente, não 6.
        Assert.Equal(5, vm.DailyExtended.Count);
    }

    [Fact]
    public void Previsao_toda_no_passado_esconde_a_barra_em_vez_de_mentir()
    {
        // Cache de vários dias atrás: melhor não mostrar faixa nenhuma do que
        // apresentar a de outro dia como se fosse a de hoje.
        using var vm = BuildViewModel(forecastDays: 3, startOffsetDays: -10);

        Assert.False(vm.HasTodayRange);
        Assert.Empty(vm.DailyExtended);
    }

    [Fact]
    public void Rotulo_curto_tira_o_estado_para_caber_no_pequeno()
    {
        using var vm = BuildViewModel(forecastDays: 7);

        Assert.Equal("Teste, Estado", vm.LocationName);
        Assert.Equal("Teste", vm.LocationShort);
    }

    [Fact]
    public void Widget_declara_as_tres_variantes_com_tamanhos_distintos()
    {
        using var widget = new WeatherWidget(null, _ => { });

        Assert.Equal(
            new[] { WidgetSize.Small, WidgetSize.Medium, WidgetSize.Large },
            widget.SupportedSizes);

        var sizes = widget.SupportedSizes.Select(widget.DesignSizeFor).ToList();

        // Cada variante é maior que a anterior nas duas dimensões.
        for (var i = 1; i < sizes.Count; i++)
        {
            Assert.True(sizes[i].Width > sizes[i - 1].Width);
            Assert.True(sizes[i].Height > sizes[i - 1].Height);
        }
    }

    public void Dispose()
    {
        if (File.Exists(_cachePath)) File.Delete(_cachePath);
    }
}

