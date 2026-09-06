using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Widgets.Abstractions;

namespace WidgetHost.Config;

public sealed class HostSettings
{
    /// <summary>Versão do schema. Ver <see cref="SettingsMigration"/>.</summary>
    public int Version { get; set; } = SettingsMigration.CurrentVersion;

    public bool StartWithWindows { get; set; }

    public List<WidgetInstanceSettings> Widgets { get; set; } = new();
}

/// <summary>
/// Uma instância colocada na área de trabalho.
///
/// <para><see cref="Type"/> e <see cref="InstanceId"/> são coisas diferentes de
/// propósito: dois relógios ou duas cidades são o mesmo tipo com instâncias
/// distintas, cada uma com posição, escala e opções próprias.</para>
/// </summary>
public sealed class WidgetInstanceSettings
{
    /// <summary>Identidade única desta instância. Estável entre execuções.</summary>
    public string InstanceId { get; set; } = "";

    /// <summary>Chave do tipo no <c>WidgetCatalog</c>. Ex.: "weather".</summary>
    public string Type { get; set; } = "";

    /// <summary>Posição em coordenadas da TELA VIRTUAL, em pixels físicos. Null = ainda não posicionado.</summary>
    public int? X { get; set; }
    public int? Y { get; set; }

    /// <summary>
    /// Variante de LAYOUT — qual conjunto de informação aparece. Ortogonal à
    /// <see cref="Scale"/>, que é zoom sobre o layout escolhido.
    /// </summary>
    public WidgetSize Size { get; set; } = WidgetSize.Medium;

    /// <summary>Fator sobre o <c>DesignSizeFor(Size)</c> do widget. 1.0 = tamanho de projeto.</summary>
    public double Scale { get; set; } = 1.0;

    public bool Locked { get; set; }

    /// <summary>Configuração específica do widget, opaca para o host.</summary>
    public JsonObject Options { get; set; } = new();

    [JsonIgnore]
    public bool HasPosition => X.HasValue && Y.HasValue;
}
