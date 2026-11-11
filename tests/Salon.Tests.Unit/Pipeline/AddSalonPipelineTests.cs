using FluentValidation;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Salon.Core.Cqrs;
using Salon.Core.Tenancy;
using Salon.Web.Framework.Pipeline;

namespace Salon.Tests.Unit.Pipeline;

/// <summary>Exercises the real registration: dispatcher, the four behaviours in order, scanned handlers and validators.</summary>
public class AddSalonPipelineTests
{
    private static ServiceProvider Build(Guid? tenantId)
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);

        var services = new ServiceCollection();
        services.AddSingleton(tenant);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSalonPipeline(typeof(Ping).Assembly);
        services.AddScoped(_ => Substitute.For<IUnitOfWork>());
        return services.BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public void Registers_the_behaviours_in_the_documented_order()
    {
        var services = new ServiceCollection();

        services.AddSalonPipeline(typeof(Ping).Assembly);

        services.Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(d => d.ImplementationType)
            .ShouldBe(
            [
                typeof(LoggingBehavior<,>),
                typeof(ValidationBehavior<,>),
                typeof(TenantGuardBehavior<,>),
                typeof(TransactionBehavior<,>),
            ]);
        services.ShouldContain(d => d.ServiceType == typeof(IDispatcher) && d.Lifetime == ServiceLifetime.Scoped);
        services.ShouldContain(d => d.ServiceType == typeof(IUnitOfWork) && d.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void Scans_handlers_and_validators_from_the_given_assemblies()
    {
        var services = new ServiceCollection();

        services.AddSalonPipeline(typeof(Ping).Assembly);

        services.ShouldContain(d =>
            d.ServiceType == typeof(IRequestHandler<Ping, string>)
            && d.ImplementationType == typeof(PingHandler)
            && d.Lifetime == ServiceLifetime.Scoped);
        services.ShouldContain(d =>
            d.ServiceType == typeof(IRequestHandler<CountThings, int>) && d.ImplementationType == typeof(CountThingsHandler));
        services.ShouldContain(d => d.ServiceType == typeof(IValidator<Ping>) && d.ImplementationType == typeof(PingValidator));
    }

    [Fact]
    public void Calling_it_twice_does_not_duplicate_behaviours()
    {
        var services = new ServiceCollection();

        services.AddSalonPipeline(typeof(Ping).Assembly);
        services.AddSalonPipeline(typeof(Ping).Assembly);

        services.Count(d => d.ServiceType == typeof(IPipelineBehavior<,>)).ShouldBe(4);
        services.Count(d => d.ServiceType == typeof(IDispatcher)).ShouldBe(1);
    }

    [Fact]
    public async Task End_to_end_a_valid_command_with_a_tenant_is_handled_in_a_transaction()
    {
        await using var provider = Build(Guid.CreateVersion7());
        using var scope = provider.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(new Ping("ok"), CancellationToken.None);

        result.ShouldBe("pong: ok");
        await unitOfWork.Received(1).BeginTransactionAsync(Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task End_to_end_an_invalid_command_fails_validation_before_the_tenant_guard()
    {
        await using var provider = Build(null);
        using var scope = provider.CreateScope();

        await Should.ThrowAsync<ValidationException>(
            () => scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(new Ping(""), CancellationToken.None));
    }

    [Fact]
    public async Task End_to_end_a_valid_command_without_a_tenant_is_refused_and_never_starts_a_transaction()
    {
        await using var provider = Build(null);
        using var scope = provider.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await Should.ThrowAsync<TenantViolationException>(
            () => scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(new Ping("ok"), CancellationToken.None));

        await unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task End_to_end_a_platform_request_runs_without_a_tenant()
    {
        await using var provider = Build(null);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(new PlatformPing(), CancellationToken.None);

        result.ShouldBe("platform");
    }
}