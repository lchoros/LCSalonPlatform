using NSubstitute;
using Salon.Core.Tenancy;
using Salon.Web.Framework.Pipeline;

namespace Salon.Tests.Unit.Pipeline;

public class TenantGuardBehaviorTests
{
    private static ITenantContext Tenant(Guid? id)
    {
        var context = Substitute.For<ITenantContext>();
        context.TenantId.Returns(id);
        return context;
    }

    [Fact]
    public async Task Request_without_tenant_is_refused()
    {
        var behavior = new TenantGuardBehavior<Ping, string>(Tenant(null));
        var handled = false;

        var ex = await Should.ThrowAsync<TenantViolationException>(
            () => behavior.HandleAsync(new Ping("x"), () => { handled = true; return Task.FromResult("ok"); }, CancellationToken.None));

        handled.ShouldBeFalse();
        ex.Message.ShouldContain(nameof(Ping));
    }

    [Fact]
    public async Task Request_with_tenant_passes()
    {
        var behavior = new TenantGuardBehavior<Ping, string>(Tenant(Guid.CreateVersion7()));

        (await behavior.HandleAsync(new Ping("x"), () => Task.FromResult("ok"), CancellationToken.None)).ShouldBe("ok");
    }

    [Fact]
    public async Task Platform_request_passes_without_tenant()
    {
        var behavior = new TenantGuardBehavior<PlatformPing, string>(Tenant(null));

        (await behavior.HandleAsync(new PlatformPing(), () => Task.FromResult("ok"), CancellationToken.None)).ShouldBe("ok");
    }
}