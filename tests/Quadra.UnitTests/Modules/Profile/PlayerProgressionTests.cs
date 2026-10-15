using FluentAssertions;
using Quadra.Modules.Profile.Application;

namespace Quadra.UnitTests.Modules.Profile;

/// <summary>Points and level numbers: 10 × n × (n − 1) points to reach level n.</summary>
public sealed class PlayerProgressionTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(19, 1)]
    [InlineData(20, 2)]
    [InlineData(59, 2)]
    [InlineData(60, 3)]
    [InlineData(199, 4)]
    [InlineData(200, 5)]
    [InlineData(2399, 15)]
    [InlineData(2400, 16)]
    public void LevelFor_follows_the_curve(int points, int level) =>
        PlayerProgression.LevelFor(points).Should().Be(level);

    [Fact]
    public void PointsOf_adds_attendance_wins_and_mvps() =>
        PlayerProgression.PointsOf(matchesPlayed: 4, wins: 2, mvpsReceived: 1).Should().Be(40 + 30 + 25);
}
