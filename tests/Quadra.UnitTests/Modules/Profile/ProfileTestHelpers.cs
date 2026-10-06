namespace Quadra.UnitTests.Modules.Profile;

/// <summary>A <see cref="TimeProvider"/> that always returns a fixed instant, for deterministic tests.</summary>
internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;
    public FixedTimeProvider(DateTimeOffset now) => _now = now;
    public override DateTimeOffset GetUtcNow() => _now;
}
