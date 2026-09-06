using System;
using System.Collections.Generic;
using System.IO;
using Widgets.Weather;
using Widgets.Weather.Domain;

namespace Widgets.Weather.Tests;

/// <summary>
/// A barra de faixa é o elemento que carrega a leitura "onde estamos dentro do
/// dia". Se a posição do marcador estiver errada, ela mente com confiança.
/// </summary>
public class TodayRangeTests : IDisposable
{
    private readonly string _cachePath =
        Path.Combine(Path.GetTempPath(), $"weather-range-{Guid.NewGuid():N}.json");

    private WeatherViewModel BuildViewModel(double currentC, double minC, double maxC, int extraDays = 4)
    {
        var daily = new List<DailyForecast>
        {
            new(DateOnly.FromDateTime(DateTime.Today), 3, minC, maxC),
        };
        for (var i = 1; i <= extraDays; i++)
            daily.Add(new DailyForecast(DateOnly.FromDateTime(DateTime.Today.AddDays(i)), 3, 5, 15));

        var snapshot = new WeatherSnapshot(
            "Teste", currentC, currentC - 2, 60, 10, 0, true, daily, DateTimeOffset.Now);

        var cache = new WeatherCache(_cachePath);
        cache.Save(snapshot);

        return new WeatherViewModel(
            new WeatherOptions { Latitude = -23.5, Longitude = -46.6 }, cache);
    }

    [Fact]
    public void Marcador_fica_na_fracao_correta_entre_minima_e_maxima()
    {
        // 10° numa faixa de 8..18 = 20% do caminho.
        using var vm = BuildViewModel(currentC: 10, minC: 8, maxC: 18);

        Assert.True(vm.HasTodayRange);
        Assert.Equal(0.2, vm.RangeLeft.Value, precision: 6);
        Assert.Equal(0.8, vm.RangeRight.Value, precision: 6);
    }

    [Theory]
    [InlineData(8, 0.0)]     // exatamente na mínima
    [InlineData(18, 1.0)]    // exatamente na máxima
    [InlineData(13, 0.5)]    // no meio
    public void Extremos_e_meio_da_faixa(double current, double expected)
    {
        using var vm = BuildViewModel(current, minC: 8, maxC: 18);

        Assert.Equal(expected, vm.RangeLeft.Value, precision: 6);
    }

    [Theory]
    [InlineData(3)]    // mais frio que a mínima prevista
    [InlineData(25)]   // mais quente que a máxima prevista
    public void Temperatura_fora_da_faixa_prevista_grampeia_na_borda(double current)
    {
        // A temperatura atual pode estourar a previsão. Sem o clamp, o marcador
        // sairia da barra.
        using var vm = BuildViewModel(current, minC: 8, maxC: 18);

        Assert.InRange(vm.RangeLeft.Value, 0d, 1d);
        Assert.InRange(vm.RangeRight.Value, 0d, 1d);
    }

    [Fact]
    public void Faixa_degenerada_desliga_a_barra_em_vez_de_dividir_por_zero()
    {
        using var vm = BuildViewModel(currentC: 12, minC: 12, maxC: 12);

        Assert.False(vm.HasTodayRange);
    }

    [Fact]
    public void Tira_de_dias_comeca_amanha_para_nao_repetir_a_barra()
    {
        // Hoje já é a barra; repetir a mesma mín/máx logo abaixo seria ruído.
        using var vm = BuildViewModel(currentC: 10, minC: 8, maxC: 18, extraDays: 4);

        Assert.Equal(4, vm.Daily.Count);
        Assert.DoesNotContain(vm.Daily, d => d.Weekday == "Hoje");
    }

    [Fact]
    public void Rotulos_da_faixa_saem_sem_a_letra_da_unidade()
    {
        // A unidade já está estabelecida pela leitura principal.
        using var vm = BuildViewModel(currentC: 10, minC: 8, maxC: 18);

        Assert.Equal("8°", vm.TodayMin);
        Assert.Equal("18°", vm.TodayMax);
        Assert.Equal("10", vm.TemperatureValue);
        Assert.Equal("°C", vm.TemperatureUnit);
    }

    [Fact]
    public void Fahrenheit_converte_valor_e_unidade()
    {
        var daily = new List<DailyForecast>
        {
            new(DateOnly.FromDateTime(DateTime.Today), 3, 0, 100),
        };
        var snapshot = new WeatherSnapshot("Teste", 0, 0, 60, 10, 0, true, daily, DateTimeOffset.Now);
        var cache = new WeatherCache(_cachePath);
        cache.Save(snapshot);

        using var vm = new WeatherViewModel(
            new WeatherOptions { Latitude = -23.5, Longitude = -46.6, UseFahrenheit = true }, cache);

        Assert.Equal("32", vm.TemperatureValue);
        Assert.Equal("°F", vm.TemperatureUnit);
        Assert.Equal("212°", vm.TodayMax);
    }

    public void Dispose()
    {
        if (File.Exists(_cachePath)) File.Delete(_cachePath);
    }
}

