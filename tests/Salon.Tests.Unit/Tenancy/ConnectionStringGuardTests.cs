using Salon.Web.Framework.Infrastructure;

namespace Salon.Tests.Unit.Tenancy;

/// <summary>ADR 0002: the Npgsql pool reset must stay on. Since ADR 0090 the tenant setting is no session state, so this is a second guard.</summary>
public class ConnectionStringGuardTests
{
    private const string Base = "Host=localhost;Database=salon_test;Username=postgres;Password=postgres";

    [Fact]
    public void Default_connection_string_is_accepted()
    {
        Should.NotThrow(() => SalonFrameworkServiceCollectionExtensions.EnsureTenantSafeConnectionString(Base));
    }

    [Theory]
    [InlineData("No Reset On Close=true")]
    [InlineData("Multiplexing=true")]
    public void Options_that_skip_the_pool_reset_are_refused(string option)
    {
        Should.Throw<InvalidOperationException>(
            () => SalonFrameworkServiceCollectionExtensions.EnsureTenantSafeConnectionString(Base + ";" + option))
            .Message.ShouldContain("ADR 0002");
    }
}