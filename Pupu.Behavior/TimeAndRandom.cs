namespace Pupu.Behavior;

public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}

public interface IRandomSource
{
    double NextDouble();
    int Next(int minInclusive, int maxExclusive);
}

public sealed class SeededRandomSource : IRandomSource
{
    private readonly Random _random;

    public SeededRandomSource(int seed) => _random = new Random(seed);
    public double NextDouble() => _random.NextDouble();
    public int Next(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);
}

public sealed class SystemRandomSource : IRandomSource
{
    private readonly Random _random = new();
    public double NextDouble() => _random.NextDouble();
    public int Next(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);
}
