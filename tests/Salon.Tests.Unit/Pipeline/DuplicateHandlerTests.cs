using Microsoft.Extensions.DependencyInjection;
using Salon.Core.Cqrs;
using Salon.Web.Framework.Pipeline;

namespace Salon.Tests.Unit.Pipeline;

/// <summary>ADR 0003: a request has exactly one handler. Two must fail at registration, and at dispatch if they slip through DI.</summary>
public class DuplicateHandlerTests
{
    [Fact]
    public void Scanning_an_assembly_whose_request_already_has_another_handler_throws()
    {
        var services = new ServiceCollection();
        services.AddScoped<IRequestHandler<Ping, string>, OtherPingHandler<int>>();

        var ex = Should.Throw<InvalidOperationException>(() => services.AddSalonPipeline(typeof(Ping).Assembly));

        ex.Message.ShouldContain("Ping");
        ex.Message.ShouldContain("two handlers");
    }

    [Fact]
    public async Task Dispatching_a_request_with_two_registered_handlers_throws()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSalonPipeline();
        services.AddScoped<IRequestHandler<Ping, string>, PingHandler>();
        services.AddScoped<IRequestHandler<Ping, string>, OtherPingHandler<int>>();
        services.AddScoped<Salon.Core.Tenancy.ITenantContext, FixedTenant>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => dispatcher.SendAsync(new Ping("x"), CancellationToken.None));

        ex.Message.ShouldContain("2 handlers");
    }

    /// <summary>Generic on purpose: the assembly scan skips open generic definitions, so only explicit registration adds it.</summary>
    private sealed class OtherPingHandler<TUnused> : IRequestHandler<Ping, string>
    {
        public Task<string> HandleAsync(Ping request, CancellationToken cancellationToken) => Task.FromResult("other");
    }

    private sealed class FixedTenant : Salon.Core.Tenancy.ITenantContext
    {
        public Guid? TenantId => Guid.CreateVersion7();
    }
}