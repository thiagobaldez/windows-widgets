using System;
using System.Collections.Generic;
using System.Windows;

namespace Widgets.Abstractions;

/// <summary>
/// Variante de layout. Não é zoom: cada tamanho mostra um conjunto diferente de
/// informação. O zoom contínuo (grip) é ortogonal e se aplica por cima.
/// </summary>
public enum WidgetSize
{
    Small,
    Medium,
    Large,
}

/// <summary>
/// Contrato de um widget. O host não sabe nada sobre o conteúdo; o widget não
/// sabe nada sobre a área de trabalho.
///
/// <para><see cref="CreateView"/> pode ser chamado MAIS DE UMA VEZ na vida do
/// widget: quando o explorer.exe reinicia, o SHELLDLL_DefView é destruído e o
/// Windows destrói junto as janelas filhas, então o host recria a janela do
/// widget. Por isso o estado (ViewModel, dados carregados, timers) tem que
/// viver na implementação de <see cref="IWidget"/>, nunca na View.</para>
/// </summary>
public interface IWidget : IDisposable
{
    /// <summary>Chave estável usada na persistência. Ex.: "weather".</summary>
    string Id { get; }

    /// <summary>Nome exibido no menu da bandeja.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Variantes que este widget oferece, da menor para a maior. Um widget
    /// simples pode oferecer só <see cref="WidgetSize.Medium"/> — o host
    /// esconde as opções que não existem em vez de mostrar item morto.
    /// </summary>
    IReadOnlyList<WidgetSize> SupportedSizes { get; }

    /// <summary>
    /// Tamanho de PROJETO da variante, em DIPs — a referência de escala 1.0.
    /// O host envolve a View num Viewbox e escala uniformemente a partir daqui.
    ///
    /// <para>Meça este valor contra o conteúdo real em vez de estimar: o
    /// Viewbox nunca corta, mas um valor apertado deixa o conteúdo espremido
    /// em todas as escalas.</para>
    /// </summary>
    Size DesignSizeFor(WidgetSize size);

    /// <summary>Cria uma View nova da variante pedida, ligada ao estado que já existe.</summary>
    FrameworkElement CreateView(WidgetSize size);

    /// <summary>Atualização disparada pelo usuário no menu de contexto.</summary>
    void Refresh();

    /// <summary>
    /// Abre a configuração do widget. Cada widget é dono da própria UI de
    /// ajustes — o host não conhece nenhum campo específico. Widgets sem nada
    /// para configurar não fazem nada aqui.
    /// </summary>
    void ShowSettings();
}
