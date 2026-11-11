namespace Salon.Tests.Integration.Infrastructure;

/// <summary>All tests that touch salon_test share one host and run serially, so Respawn resets never race.</summary>
[CollectionDefinition(Name)]
public sealed class SharedDatabase : ICollectionFixture<SalonWebApplicationFactory>
{
    public const string Name = "Database";
}