using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Windows;
using Widgets.Abstractions;
using Widgets.Weather.Views;

namespace Widgets.Weather;

/// <summary>
/// Widget de previsão do tempo. Guarda o ViewModel (que sobrevive à recriação
/// da janela pelo host) e produz Views novas sob demanda.
/// </summary>
public sealed class WeatherWidget : IWidget
{
    private readonly WeatherViewModel _viewModel;
    private readonly WeatherOptions _options;
    private readonly Action<JsonObject> _persistOptions;
    private bool _started;

    /// <param name="options">Bloco de opções vindo do settings.json do host.</param>
    /// <param name="persistOptions">Chamado quando as opções mudam (ex.: localização detectada por IP).</param>
    public WeatherWidget(JsonObject? options, Action<JsonObject> persistOptions)
    {
        _persistOptions = persistOptions;

        _options = WeatherOptions.FromJson(options);
        _options.Changed += (_, _) => _persistOptions(_options.ToJson());

        _viewModel = new WeatherViewModel(_options);
    }

    public string Id => "weather";
    public string DisplayName => "Previsão do Tempo";

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } =
        new[] { WidgetSize.Small, WidgetSize.Medium, WidgetSize.Large };

    // Alturas MEDIDAS contra o conteúdo real de cada layout, não estimadas.
    // São a referência de escala 1.0: apertar qualquer uma espreme o conteúdo
    // em todas as escalas, não só na inicial.
    public Size DesignSizeFor(WidgetSize size) => size switch
    {
        WidgetSize.Small => new Size(212, 89),
        WidgetSize.Large => new Size(424, 355),
        _ => new Size(300, 291),
    };

    public FrameworkElement CreateView(WidgetSize size)
    {
        FrameworkElement view = size switch
        {
            WidgetSize.Small => new WeatherViewSmall(_viewModel),
            WidgetSize.Large => new WeatherViewLarge(_viewModel),
            _ => new WeatherViewMedium(_viewModel),
        };

        // A primeira carga só dispara depois que existe uma View na tela, para
        // o estado de "Carregando..." ter onde aparecer. Chamado uma vez só:
        // recriações de janela reaproveitam os dados já carregados.
        if (!_started)
        {
            _started = true;
            view.Loaded += (_, _) => _ = _viewModel.StartAsync();
        }

        return view;
    }

    public void Refresh() => _ = _viewModel.RefreshAsync();

    public void ShowSettings()
    {
        var dialog = new WeatherSettingsWindow(_options);
        if (dialog.ShowDialog() == true) _viewModel.ApplyOptions();
    }

    public void Dispose() => _viewModel.Dispose();
}
