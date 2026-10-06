namespace Quadra.UnitTests.Modules.InGame;

/// <summary>
/// Shared helpers for the F1.4 scoreboard handler unit tests.
/// </summary>
internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;
    public FixedTimeProvider(DateTimeOffset now) => _now = now;
    public override DateTimeOffset GetUtcNow() => _now;
}
