namespace Quadra.IntegrationTests.Modules.Gamification;

/// <summary>
/// Groups the F2.3 group-ranking integration tests into one collection sharing a single
/// <see cref="GamificationWebApplicationFactory"/> (one Testcontainers Postgres for the whole suite).
/// </summary>
[CollectionDefinition("Gamification")]
public sealed class GamificationCollection : ICollectionFixture<GamificationWebApplicationFactory>;
