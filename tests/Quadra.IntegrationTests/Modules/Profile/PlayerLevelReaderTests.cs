using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Quadra.Modules.Profile.Abstractions;
using Quadra.Shared.Contracts;

namespace Quadra.IntegrationTests.Modules.Profile;

/// <summary>
/// Integration tests for <see cref="IPlayerLevelReader"/> (the F1.3 contract now implemented by the
/// real <c>PlayerLevelReader</c>), backed by a real Postgres.
///
/// Covers: F2.1 "IPlayerLevelReader upgrade" — team drafting reads persisted levels, and the reader is
/// resilient to players with no profile yet (returns "Beginner" for unknown ids).
/// </summary>
[Collection("Profile")]
public sealed class PlayerLevelReaderTests
{
    private readonly ProfileWebApplicationFactory _fx;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public PlayerLevelReaderTests(ProfileWebApplicationFactory fx)
    {
        _fx = fx;
    }

    /// <summary>Covers: an unknown/no-profile player id resolves to "Beginner".</summary>
    [Fact]
    public async Task Returns_Beginner_for_unknown_player()
    {
        await _fx.ResetAsync();
        var unknown = Guid.NewGuid();

        IReadOnlyDictionary<Guid, string> levels = null!;
        await _fx.WithScopeAsync(async sp =>
        {
            var reader = sp.GetRequiredService<IPlayerLevelReader>();
            levels = await reader.GetPlayerLevelsAsync(new[] { unknown }, Ct);
        });

        levels.Should().ContainKey(unknown);
        levels[unknown].Should().Be("Beginner");
    }

    /// <summary>
    /// Covers: a provisioned player returns their persisted level, and it updates to "Intermediate"
    /// once the finished-match writes promote them — so drafting balances on real levels.
    /// </summary>
    [Fact]
    public async Task Returns_persisted_level_for_known_player()
    {
        await _fx.ResetAsync();
        var beginner = Guid.NewGuid();
        var promoted = Guid.NewGuid();
        var unknown = Guid.NewGuid();

        await _fx.WithScopeAsync(async sp =>
        {
            var provisioner = sp.GetRequiredService<IPlayerProfileProvisioner>();
            await provisioner.EnsureProfileAsync(beginner, Ct);
            await provisioner.EnsureProfileAsync(promoted, Ct);
        });

        // Promote `promoted` to Intermediate: 10 matches with an MVP.
        for (var i = 0; i < 10; i++)
        {
            await _fx.WithScopeAsync(async sp =>
            {
                var writer = sp.GetRequiredService<IPlayerStatsWriter>();
                await writer.ApplyFinishedMatchAsync(
                    new FinishedMatchParticipation(
                        PlayerId: promoted,
                        MatchId: Guid.NewGuid(),
                        MatchName: "M",
                        MatchDateTime: DateTimeOffset.UtcNow.AddDays(-i),
                        TeamId: Guid.NewGuid(),
                        Outcome: "Win",
                        WasMvp: i == 0,
                        DurationSeconds: 3600,
                        FinishedAt: DateTimeOffset.UtcNow.AddDays(-i)),
                    Ct);
            });
        }

        IReadOnlyDictionary<Guid, string> levels = null!;
        await _fx.WithScopeAsync(async sp =>
        {
            var reader = sp.GetRequiredService<IPlayerLevelReader>();
            levels = await reader.GetPlayerLevelsAsync(new[] { beginner, promoted, unknown }, Ct);
        });

        levels[beginner].Should().Be("Beginner");
        levels[promoted].Should().Be("Intermediate");
        levels[unknown].Should().Be("Beginner", "unknown ids default to Beginner");
    }
}
