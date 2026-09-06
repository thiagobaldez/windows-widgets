using System;

namespace Widgets.Weather.Domain;

/// <summary>
/// Decide quando tentar de novo. Classe pura, sem timer e sem relógio — é
/// testável sem esperar minutos.
///
/// <para>Sucesso: volta ao intervalo base, com jitter para várias instâncias
/// não baterem na API no mesmo segundo. Falha: backoff exponencial começando
/// em 1 min e limitado a 30 min.</para>
/// </summary>
public sealed class RefreshPolicy
{
    public static readonly TimeSpan FirstBackoff = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(30);

    /// <summary>Fração do intervalo base sorteada como jitter (10%).</summary>
    private const double JitterFraction = 0.10;

    private readonly TimeSpan _baseInterval;
    private readonly Func<double> _random;
    private TimeSpan? _backoff;

    /// <param name="random">Fonte de aleatoriedade em [0,1). Injetável para testes.</param>
    public RefreshPolicy(TimeSpan baseInterval, Func<double>? random = null)
    {
        if (baseInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(baseInterval), "O intervalo base tem que ser positivo.");

        _baseInterval = baseInterval;
        _random = random ?? Random.Shared.NextDouble;
    }

    /// <summary>Backoff acumulado no momento. Null quando a última tentativa deu certo.</summary>
    public TimeSpan? CurrentBackoff => _backoff;

    public TimeSpan Next(bool succeeded)
    {
        if (succeeded)
        {
            _backoff = null;
            return WithJitter(_baseInterval);
        }

        _backoff = _backoff is null
            ? FirstBackoff
            : Min(_backoff.Value * 2, MaxBackoff);

        return _backoff.Value;
    }

    private TimeSpan WithJitter(TimeSpan value)
    {
        // Jitter simétrico: [-10%, +10%].
        var offset = (_random() * 2 - 1) * JitterFraction;
        return value * (1 + offset);
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
}
