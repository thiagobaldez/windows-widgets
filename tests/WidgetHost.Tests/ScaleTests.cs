using WidgetHost.Shell;

namespace WidgetHost.Tests;

public class ScaleTests
{
    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(0.6, 0.6)]
    [InlineData(2.5, 2.5)]
    [InlineData(0.1, WidgetWindow.MinScale)]     // arrastou o grip até quase nada
    [InlineData(-3.0, WidgetWindow.MinScale)]    // largura negativa: grip cruzou a origem
    [InlineData(99.0, WidgetWindow.MaxScale)]
    public void Escala_fica_dentro_dos_limites(double input, double expected)
        => Assert.Equal(expected, WidgetWindow.ClampScale(input));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Valor_nao_finito_volta_para_o_tamanho_original(double input)
    {
        // Divisão por zero no cálculo do grip não pode virar uma janela de
        // tamanho inválido — o Windows rejeita e o widget some.
        Assert.Equal(1.0, WidgetWindow.ClampScale(input));
    }

    [Fact]
    public void Limites_fazem_sentido_entre_si()
    {
        Assert.True(WidgetWindow.MinScale > 0);
        Assert.True(WidgetWindow.MinScale < 1.0);
        Assert.True(WidgetWindow.MaxScale > 1.0);
    }
}
