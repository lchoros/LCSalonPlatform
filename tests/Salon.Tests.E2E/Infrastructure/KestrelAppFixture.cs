using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Salon.Tests.E2E.Infrastructure;

/// <summary>Starts Salon.Web on a real Kestrel port and a headless Chromium so Playwright can drive the pages.</summary>
public sealed class KestrelAppFixture : IAsyncLifetime, IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _factory = new HostFactory();
    private IPlaywright? _playwright;

    public IBrowser Browser { get; private set; } = null!;

    public string BaseUrl { get; private set; } = string.Empty;

    /// <summary>Screenshots land in tests/Salon.Tests.E2E/screenshots so CI can upload them and PRs can attach them.</summary>
    public static string ScreenshotDirectory { get; } =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "screenshots"));

    public async Task InitializeAsync()
    {
        _factory.UseKestrel();
        _factory.StartServer();
        var addresses = _factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
        BaseUrl = (addresses?.FirstOrDefault() ?? throw new InvalidOperationException("Kestrel did not report an address.")).TrimEnd('/');

        Directory.CreateDirectory(ScreenshotDirectory);
        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync();
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        _playwright?.Dispose();
        await _factory.DisposeAsync();
    }

    async ValueTask IAsyncDisposable.DisposeAsync() => await DisposeAsync();

    private sealed class HostFactory : WebApplicationFactory<Program>
    {
        public HostFactory()
        {
            Environment.SetEnvironmentVariable(
                "ConnectionStrings__Default",
                Environment.GetEnvironmentVariable("ConnectionStrings__Test")
                ?? "Host=localhost;Port=5432;Database=salon_test;Username=postgres;Password=postgres");
            Environment.SetEnvironmentVariable("Database__MigrateOnStartup", "false");
            Environment.SetEnvironmentVariable("Jobs__Enabled", "false");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing");
    }
}