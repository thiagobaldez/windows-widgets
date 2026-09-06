using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Widgets.Weather.Domain;

namespace Widgets.Weather.Views;

/// <summary>
/// Ícone de condição do tempo, desenhado em vetor sobre uma caixa 24x24.
///
/// <para>Em vez de 16 paths desenhados um a um, cada condição é uma RECEITA
/// combinando poucas primitivas (astro, nuvem, precipitação, raio, névoa).
/// Isso mantém a linguagem visual coerente entre os ícones e faz um ícone novo
/// custar uma linha na tabela, não um arquivo de SVG.</para>
///
/// <para>Vetor e não fonte de ícone: o widget precisa escalar bem em DPI alto
/// sem depender de uma fonte estar instalada.</para>
/// </summary>
public sealed class WeatherIcon : UserControl
{
    private enum Precip { None, Drizzle, Rain, Snow, Pellets }

    private readonly record struct Recipe(
        bool Luminary, bool Cloud, Precip Precip, bool Bolt, bool Fog, bool Question = false);

    private static readonly IReadOnlyDictionary<WeatherIconKind, Recipe> Recipes =
        new Dictionary<WeatherIconKind, Recipe>
        {
            [WeatherIconKind.Clear] = new(true, false, Precip.None, false, false),
            [WeatherIconKind.MainlyClear] = new(true, true, Precip.None, false, false),
            [WeatherIconKind.PartlyCloudy] = new(true, true, Precip.None, false, false),
            [WeatherIconKind.Overcast] = new(false, true, Precip.None, false, false),
            [WeatherIconKind.Fog] = new(false, true, Precip.None, false, true),
            [WeatherIconKind.Drizzle] = new(false, true, Precip.Drizzle, false, false),
            [WeatherIconKind.FreezingRain] = new(false, true, Precip.Pellets, false, false),
            [WeatherIconKind.Rain] = new(false, true, Precip.Rain, false, false),
            [WeatherIconKind.Showers] = new(true, true, Precip.Rain, false, false),
            [WeatherIconKind.Snow] = new(false, true, Precip.Snow, false, false),
            [WeatherIconKind.SnowGrains] = new(false, true, Precip.Pellets, false, false),
            [WeatherIconKind.SnowShowers] = new(true, true, Precip.Snow, false, false),
            [WeatherIconKind.Thunderstorm] = new(false, true, Precip.None, true, false),
            [WeatherIconKind.ThunderstormHail] = new(false, true, Precip.Pellets, true, false),
            [WeatherIconKind.Unknown] = new(false, false, Precip.None, false, false, Question: true),
        };

    // Nuvem em caixa 24x24, ocupando a faixa y≈9..18.
    private static readonly Geometry CloudGeometry = Geometry.Parse(
        "M6.5,18.5 A4.2,4.2 0 0 1 6.1,10.1 A6,6 0 0 1 17.4,8.8 A3.9,3.9 0 0 1 17.2,18.5 Z");

    private static readonly Geometry BoltGeometry = Geometry.Parse(
        "M12.6,13.2 L9.2,19.2 L11.9,19.2 L10.6,23.6 L14.6,17.4 L11.9,17.4 Z");

    private static readonly Geometry MoonGeometry = Geometry.Parse(
        "M16.8,4.2 A8.6,8.6 0 1 0 16.8,20.6 A6.9,6.9 0 1 1 16.8,4.2 Z");

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(WeatherIconKind), typeof(WeatherIcon),
        new PropertyMetadata(WeatherIconKind.Unknown, OnVisualChanged));

    public static readonly DependencyProperty IsDayProperty = DependencyProperty.Register(
        nameof(IsDay), typeof(bool), typeof(WeatherIcon),
        new PropertyMetadata(true, OnVisualChanged));

    public static readonly DependencyProperty PrimaryBrushProperty = DependencyProperty.Register(
        nameof(PrimaryBrush), typeof(Brush), typeof(WeatherIcon),
        new PropertyMetadata(Brushes.White, OnVisualChanged));

    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
        nameof(AccentBrush), typeof(Brush), typeof(WeatherIcon),
        new PropertyMetadata(Brushes.Gold, OnVisualChanged));

    public WeatherIcon()
    {
        Focusable = false;
        IsHitTestVisible = false;
        Rebuild();
    }

    public WeatherIconKind Kind
    {
        get => (WeatherIconKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public bool IsDay
    {
        get => (bool)GetValue(IsDayProperty);
        set => SetValue(IsDayProperty, value);
    }

    /// <summary>Cor das nuvens e da precipitação.</summary>
    public Brush PrimaryBrush
    {
        get => (Brush)GetValue(PrimaryBrushProperty);
        set => SetValue(PrimaryBrushProperty, value);
    }

    /// <summary>Cor do sol/lua e do raio.</summary>
    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((WeatherIcon)d).Rebuild();

    private void Rebuild()
    {
        var recipe = Recipes.TryGetValue(Kind, out var r) ? r : Recipes[WeatherIconKind.Unknown];
        var canvas = new Canvas { Width = 24, Height = 24 };

        if (recipe.Question)
        {
            canvas.Children.Add(new TextBlock
            {
                Text = "?",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = PrimaryBrush,
                Margin = new Thickness(8, 1, 0, 0),
            });
        }

        if (recipe.Luminary)
        {
            // Sozinho fica grande e centralizado; com nuvem, encolhe e sobe
            // para o canto, espiando por trás dela.
            if (recipe.Cloud) AddLuminary(canvas, cx: 15.5, cy: 8.0, radius: 3.4, rays: IsDay);
            else AddLuminary(canvas, cx: 12.0, cy: 12.0, radius: 5.0, rays: IsDay);
        }

        if (recipe.Cloud) canvas.Children.Add(Filled(CloudGeometry, PrimaryBrush));
        if (recipe.Fog) AddFogLines(canvas);
        if (recipe.Bolt) canvas.Children.Add(Filled(BoltGeometry, AccentBrush));
        if (recipe.Precip != Precip.None) AddPrecipitation(canvas, recipe.Precip);

        Content = new Viewbox { Child = canvas, Stretch = Stretch.Uniform };
    }

    private void AddLuminary(Canvas canvas, double cx, double cy, double radius, bool rays)
    {
        if (!IsDay)
        {
            // A lua usa a cor primária (branca), não o acento: o acento é
            // quente e serve ao sol. Uma lua alaranjada seria falsa.
            var moon = Filled(MoonGeometry, PrimaryBrush);
            var scale = radius / 5.0;
            moon.RenderTransform = new TransformGroup
            {
                Children =
                {
                    new ScaleTransform(scale, scale, 12, 12),
                    new TranslateTransform(cx - 12, cy - 12),
                },
            };
            canvas.Children.Add(moon);
            return;
        }

        canvas.Children.Add(Filled(new EllipseGeometry(new Point(cx, cy), radius, radius), AccentBrush));

        if (!rays) return;

        var group = new GeometryGroup();
        for (var i = 0; i < 8; i++)
        {
            var angle = i * Math.PI / 4;
            var inner = radius + 1.6;
            var outer = radius + 3.4;
            group.Children.Add(new LineGeometry(
                new Point(cx + Math.Cos(angle) * inner, cy + Math.Sin(angle) * inner),
                new Point(cx + Math.Cos(angle) * outer, cy + Math.Sin(angle) * outer)));
        }
        canvas.Children.Add(Stroked(group, AccentBrush, 1.5));
    }

    private void AddFogLines(Canvas canvas)
    {
        var group = new GeometryGroup();
        group.Children.Add(new LineGeometry(new Point(5, 21), new Point(19, 21)));
        group.Children.Add(new LineGeometry(new Point(7.5, 23.4), new Point(16.5, 23.4)));
        canvas.Children.Add(Stroked(group, PrimaryBrush, 1.6));
    }

    private void AddPrecipitation(Canvas canvas, Precip precip)
    {
        double[] xs = { 8.5, 12.0, 15.5 };

        switch (precip)
        {
            case Precip.Drizzle:
            case Precip.Rain:
            {
                var length = precip == Precip.Rain ? 3.6 : 2.2;
                var group = new GeometryGroup();
                foreach (var x in xs)
                    group.Children.Add(new LineGeometry(
                        new Point(x, 20.0), new Point(x - 1.1, 20.0 + length)));
                canvas.Children.Add(Stroked(group, PrimaryBrush, 1.6));
                break;
            }

            case Precip.Snow:
            {
                var group = new GeometryGroup();
                foreach (var x in xs)
                {
                    // Asterisco de 3 traços.
                    group.Children.Add(new LineGeometry(new Point(x, 20.0), new Point(x, 23.0)));
                    group.Children.Add(new LineGeometry(new Point(x - 1.3, 20.75), new Point(x + 1.3, 22.25)));
                    group.Children.Add(new LineGeometry(new Point(x - 1.3, 22.25), new Point(x + 1.3, 20.75)));
                }
                canvas.Children.Add(Stroked(group, PrimaryBrush, 1.1));
                break;
            }

            case Precip.Pellets:
            {
                var group = new GeometryGroup();
                foreach (var x in xs)
                    group.Children.Add(new EllipseGeometry(new Point(x, 21.5), 1.05, 1.05));
                canvas.Children.Add(Filled(group, PrimaryBrush));
                break;
            }
        }
    }

    private static System.Windows.Shapes.Path Filled(Geometry geometry, Brush brush)
        => new() { Data = geometry, Fill = brush };

    private static System.Windows.Shapes.Path Stroked(Geometry geometry, Brush brush, double thickness)
        => new()
        {
            Data = geometry,
            Stroke = brush,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
}
