using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Widgets.Abstractions;
using Widgets.Weather;

namespace WidgetHost.Widgets;

/// <summary>
/// Um tipo de widget que pode ser colocado na área de trabalho. A fábrica
/// permite N instâncias independentes do mesmo tipo (duas cidades, dois relógios).
/// </summary>
/// <param name="Create">
/// Recebe as opções salvas da instância e o callback para persistir mudanças.
/// </param>
public sealed record WidgetType(
    string Id,
    string DisplayName,
    Func<JsonObject?, Action<JsonObject>, IWidget> Create);

/// <summary>
/// Registro dos widgets disponíveis. Único ponto do host que conhece
/// implementações concretas — adicionar um widget novo é uma linha aqui.
/// </summary>
public static class WidgetCatalog
{
    public static IReadOnlyList<WidgetType> Types { get; } = new WidgetType[]
    {
        new("weather", "Previsão do Tempo",
            (options, persist) => new WeatherWidget(options, persist)),
    };

    public static WidgetType? Find(string id)
        => Types.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
}
