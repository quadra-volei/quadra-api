namespace Quadra.IntegrationTests.Modules.Profile;

/// <summary>
/// Groups the F2.1 Profile integration tests into one collection sharing a single
/// <see cref="ProfileWebApplicationFactory"/> (one Testcontainers Postgres for the whole suite).
/// </summary>
[CollectionDefinition("Profile")]
public sealed class ProfileCollection : ICollectionFixture<ProfileWebApplicationFactory>;
