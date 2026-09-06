using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using Widgets.Weather;
using Widgets.Weather.Domain;
using Widgets.Weather.Views;

namespace Widgets.Weather.Tests;

/// <summary>
/// Defeitos de layout que só aparecem quando a largura NÃO é limitada.
///
/// <para>O host coloca a View dentro de um Viewbox, que mede o filho com
/// largura infinita. Um Grid com um filho alinhado à esquerda e outro à direita
/// no MESMO espaço pede, nessa medição, só a largura do maior — e no arrange os
/// dois se sobrepõem. Foi exatamente assim que "UMIDADE 74%" acabou impresso
/// por cima de "Atualizado 00:02" na tela do usuário.</para>
///
/// <para>Medir com largura fixa (como o preview fazia) esconde o problema. Por
/// isso estes testes medem com <see cref="double.PositiveInfinity"/>.</para>
/// </summary>
public class LayoutOverlapTests : IDisposable
{
    private readonly string _cachePath =
        Path.Combine(Path.GetTempPath(), $"weather-layout-{Guid.NewGuid():N}.json");

    /// <summary>WPF exige STA para construir elementos visuais.</summary>
    private static T RunSta<T>(Func<T> body)
    {
        T result = default!;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try { result = body(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null) throw failure;
        return result;
    }

    private WeatherViewModel BuildViewModel()
    {
        var daily = new List<DailyForecast>();
        for (var i = 0; i < 7; i++)
        {
            daily.Add(new DailyForecast(
                DateOnly.FromDateTime(DateTime.Today.AddDays(i)),
                3, 5 + i, 15 + i, i * 10, new TimeOnly(6, 45), new TimeOnly(18, 22)));
        }

        var cache = new WeatherCache(_cachePath);
        cache.Save(new WeatherSnapshot(
            "São Paulo, São Paulo", 9, 6, 74, 9, 0, false, daily, DateTimeOffset.Now));

        return new WeatherViewModel(
            new WeatherOptions { Latitude = -23.5, Longitude = -46.6 }, cache);
    }

    /// <summary>Faz o layout SEM limitar a largura, como o Viewbox do host faz.</summary>
    private static FrameworkElement LayoutUnconstrained(FrameworkElement view)
    {
        view.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        view.Arrange(new Rect(new Point(0, 0), view.DesiredSize));
        view.UpdateLayout();
        return view;
    }

    private static Rect BoundsOf(FrameworkElement root, string name)
    {
        var element = (FrameworkElement)root.FindName(name)!;
        var transform = element.TransformToAncestor(root);
        return transform.TransformBounds(new Rect(element.RenderSize));
    }

    [Fact]
    public void Medio_painel_de_medicoes_nao_invade_a_leitura_principal()
    {
        var overlap = RunSta(() =>
        {
            var view = LayoutUnconstrained(new WeatherViewMedium(BuildViewModel()));
            return Rect.Intersect(BoundsOf(view, "MainReading"), BoundsOf(view, "Metrics"));
        });

        Assert.True(overlap.IsEmpty || overlap.Width <= 0.5,
            $"medições por cima da temperatura em {overlap.Width:0.##}px");
    }

    [Fact]
    public void Grande_painel_de_medicoes_nao_invade_a_leitura_principal()
    {
        var overlap = RunSta(() =>
        {
            var view = LayoutUnconstrained(new WeatherViewLarge(BuildViewModel()));
            return Rect.Intersect(BoundsOf(view, "MainReading"), BoundsOf(view, "Metrics"));
        });

        Assert.True(overlap.IsEmpty || overlap.Width <= 0.5,
            $"medições por cima da temperatura em {overlap.Width:0.##}px");
    }

    [Fact]
    public void Painel_de_medicoes_fica_acima_da_metade_do_cartao()
    {
        // "Canto superior direito" não é figura de linguagem: se o bloco
        // escorregar para o meio, deixa de ser um painel de instrumento e vira
        // texto solto no meio do widget.
        var (metricsTop, cardHeight) = RunSta(() =>
        {
            var view = LayoutUnconstrained(new WeatherViewLarge(BuildViewModel()));
            return (BoundsOf(view, "Metrics").Top, view.DesiredSize.Height);
        });

        Assert.True(metricsTop < cardHeight / 2,
            $"painel começa em {metricsTop:0.##}px, abaixo da metade de {cardHeight:0.##}px");
    }

    [Fact]
    public void Medio_selo_de_ressalva_nao_cobre_o_nome_da_cidade()
    {
        var overlap = RunSta(() =>
        {
            var vm = BuildViewModel();
            var view = LayoutUnconstrained(new WeatherViewMedium(vm));
            var location = BoundsOf(view, "HeaderLocation");
            var badge = BoundsOf(view, "HeaderBadge");
            return Rect.Intersect(location, badge);
        });

        Assert.True(overlap.IsEmpty || overlap.Width <= 0.5,
            $"selo cobrindo a cidade em {overlap.Width:0.##}px");
    }

    [Theory]
    [InlineData(WidgetSizeName.Small)]
    [InlineData(WidgetSizeName.Medium)]
    [InlineData(WidgetSizeName.Large)]
    public void Nenhum_layout_encolhe_abaixo_do_que_precisa(WidgetSizeName which)
    {
        // Largura desejada sem restrição tem que ser positiva e finita: zero ou
        // infinito indicam um painel que não sabe o próprio tamanho, e é desse
        // tipo de layout que nascem as sobreposições.
        var desired = RunSta(() =>
        {
            FrameworkElement view = which switch
            {
                WidgetSizeName.Small => new WeatherViewSmall(BuildViewModel()),
                WidgetSizeName.Large => new WeatherViewLarge(BuildViewModel()),
                _ => new WeatherViewMedium(BuildViewModel()),
            };
            view.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return view.DesiredSize;
        });

        Assert.True(desired.Width is > 0 and < 5000, $"largura desejada absurda: {desired.Width}");
        Assert.True(desired.Height is > 0 and < 5000, $"altura desejada absurda: {desired.Height}");
    }

    public enum WidgetSizeName { Small, Medium, Large }

    public void Dispose()
    {
        if (File.Exists(_cachePath)) File.Delete(_cachePath);
    }
}


