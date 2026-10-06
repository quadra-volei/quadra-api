using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quadra.Modules.Profile.Application;
using Quadra.Modules.Profile.Entities;
using Quadra.Modules.Profile.Persistence;

namespace Quadra.UnitTests.Modules.Profile;

/// <summary>
/// Unit tests for <see cref="ProvisionProfileHandler"/> (implements <c>IPlayerProfileProvisioner</c>).
///
/// Covers: F2.1 "provisioning idempotency" — EnsureProfileAsync bootstraps an empty profile + stats
/// pair on first call and is a no-op when a profile already exists (UserRegistered redelivery).
/// </summary>
public sealed class ProvisionProfileHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = new("11111111-1111-1111-1111-111111111111");

    private readonly IPlayerProfileRepository _profiles = Substitute.For<IPlayerProfileRepository>();
    private readonly TimeProvider _time = new FixedTimeProvider(Now);

    private ProvisionProfileHandler CreateSut() =>
        new(_profiles, _time, NullLogger<ProvisionProfileHandler>.Instance);

    /// <summary>Covers: first-time provisioning inserts a placeholder profile and a zeroed stats row.</summary>
    [Fact]
    public async Task EnsureProfileAsync_when_absent_creates_profile_and_stats()
    {
        _profiles.ExistsAsync(UserId, Arg.Any<CancellationToken>()).Returns(false);

        await CreateSut().EnsureProfileAsync(UserId, CancellationToken.None);

        await _profiles.Received(1).AddAsync(
            Arg.Is<PlayerProfile>(p =>
                p.UserId == UserId &&
                p.DisplayName == PlayerProfile.PlaceholderDisplayName &&
                p.Level == PlayerLevel.Beginner),
            Arg.Is<PlayerStats>(s =>
                s.UserId == UserId &&
                s.MatchesPlayed == 0 &&
                s.Wins == 0 &&
                s.Losses == 0 &&
                s.Draws == 0 &&
                s.MvpsReceived == 0),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Covers: idempotency — when a profile already exists the handler is a no-op.</summary>
    [Fact]
    public async Task EnsureProfileAsync_when_already_exists_is_a_noop()
    {
        _profiles.ExistsAsync(UserId, Arg.Any<CancellationToken>()).Returns(true);

        await CreateSut().EnsureProfileAsync(UserId, CancellationToken.None);

        await _profiles.DidNotReceive().AddAsync(
            Arg.Any<PlayerProfile>(), Arg.Any<PlayerStats>(), Arg.Any<CancellationToken>());
    }
}
