using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Salon.Tests.Integration.Infrastructure;

/// <summary>Hosts Salon.Web in-process against the local test database (salon_test). Respawn resets it between tests.</summary>
public sealed class SalonWebApplicationFactory : WebApplicationFactory<Program>
{
    public static string TestConnectionString
        => Environment.GetEnvironmentVariable("ConnectionStrings__Test")
           ?? "Host=localhost;Port=5432;Database=salon_test;Username=postgres;Password=postgres";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(config =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = TestConnectionString,
            }));
    }
}