using System;
using Widgets.Weather.Domain;

namespace Widgets.Weather.Tests;

public class RefreshPolicyTests
{
    /// <summary>Jitter fixo no meio da faixa: elimina o sorteio dos testes.</summary>
    private static RefreshPolicy Policy(int baseMinutes = 15)
        => new(TimeSpan.FromMinutes(baseMinutes), random: () => 0.5);

    [Fact]
    public void Sucesso_devolve_o_intervalo_base()
    {
        var next = Policy(15).Next(succeeded: true);

        Assert.Equal(TimeSpan.FromMinutes(15), next);
    }

    [Fact]
    public void Jitter_fica_dentro_de_dez_porcento()
    {
        var baseInterval = TimeSpan.FromMinutes(15);

        foreach (var roll in new[] { 0.0, 0.25, 0.5, 0.75, 0.999 })
        {
            var next = new RefreshPolicy(baseInterval, () => roll).Next(succeeded: true);
            Assert.InRange(next, baseInterval * 0.9, baseInterval * 1.1);
        }
    }

    [Fact]
    public void Falha_dobra_o_backoff_a_partir_de_um_minuto()
    {
        var policy = Policy();

        Assert.Equal(TimeSpan.FromMinutes(1), policy.Next(succeeded: false));
        Assert.Equal(TimeSpan.FromMinutes(2), policy.Next(succeeded: false));
        Assert.Equal(TimeSpan.FromMinutes(4), policy.Next(succeeded: false));
        Assert.Equal(TimeSpan.FromMinutes(8), policy.Next(succeeded: false));
        Assert.Equal(TimeSpan.FromMinutes(16), policy.Next(succeeded: false));
    }

    [Fact]
    public void Backoff_para_de_crescer_no_teto_de_trinta_minutos()
    {
        var policy = Policy();

        for (var i = 0; i < 20; i++) policy.Next(succeeded: false);

        Assert.Equal(RefreshPolicy.MaxBackoff, policy.CurrentBackoff);
        Assert.Equal(RefreshPolicy.MaxBackoff, policy.Next(succeeded: false));
    }

    [Fact]
    public void Primeiro_sucesso_zera_o_backoff_acumulado()
    {
        var policy = Policy(15);
        policy.Next(succeeded: false);
        policy.Next(succeeded: false);
        policy.Next(succeeded: false);

        var recovered = policy.Next(succeeded: true);

        Assert.Null(policy.CurrentBackoff);
        Assert.Equal(TimeSpan.FromMinutes(15), recovered);

        // E a próxima falha recomeça do 1 min, não de onde parou.
        Assert.Equal(RefreshPolicy.FirstBackoff, policy.Next(succeeded: false));
    }

    [Fact]
    public void Intervalo_base_invalido_e_rejeitado_na_construcao()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RefreshPolicy(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RefreshPolicy(TimeSpan.FromMinutes(-5)));
    }
}
