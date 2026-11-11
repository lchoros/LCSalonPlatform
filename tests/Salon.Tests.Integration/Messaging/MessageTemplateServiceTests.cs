using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Salon.Core.Messaging;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Messaging;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Messaging;

/// <summary>
/// Message templates (ADR 0008): embedded defaults per culture with en-GB as the last fallback, per-tenant overrides in
/// message_templates that never leak to another tenant, validation against the catalogue, and a cache that is dropped on save.
/// </summary>
[Collection(SharedDatabase.Name)]
public class MessageTemplateServiceTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string Confirmation = "Booking.Confirmation";

    private Tenant _tenantA = null!;
    private Tenant _tenantB = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _tenantA = await factory.CreateTenantAsync("templates-a");
        _tenantB = await factory.CreateTenantAsync("templates-b", "pl-PL", "PLN");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Without_an_override_the_embedded_default_of_the_culture_is_returned()
    {
        await using var scope = factory.CreateTenantScope(_tenantB.Id);

        var sms = await Service(scope).GetAsync(Confirmation, MessageChannel.Sms, "pl-PL");
        var email = await Service(scope).GetAsync(Confirmation, MessageChannel.Email, "pl-PL");

        sms.IsTenantOverride.ShouldBeFalse();
        sms.Subject.ShouldBeNull();
        sms.Body.ShouldStartWith("Cześć {{ClientFirstName}}");
        email.IsTenantOverride.ShouldBeFalse();
        email.Subject.ShouldNotBeNullOrWhiteSpace();
        email.Body.ShouldStartWith("Cześć {{ClientFirstName}}");
    }

    [Fact]
    public async Task An_unknown_culture_falls_back_to_the_default_culture_text()
    {
        await using var scope = factory.CreateTenantScope(_tenantA.Id);

        var text = await Service(scope).GetAsync(Confirmation, MessageChannel.Sms, "de-DE");
        var english = await Service(scope).GetAsync(Confirmation, MessageChannel.Sms, "en-GB");

        text.ShouldBe(english);
        text.Body.ShouldStartWith("Hi {{ClientFirstName}}");
    }

    [Fact]
    public async Task An_unknown_key_throws_before_any_lookup()
    {
        await using var scope = factory.CreateTenantScope(_tenantA.Id);

        var ex = await Should.ThrowAsync<MessageTemplateNotFoundException>(() => Service(scope).GetAsync("Booking.Nope", MessageChannel.Sms, "en-GB"));

        ex.Key.ShouldBe("Booking.Nope");
    }

    [Fact]
    public async Task Save_override_then_get_returns_the_override_and_the_row_belongs_to_the_tenant()
    {
        await using var scope = factory.CreateTenantScope(_tenantA.Id);
        await Service(scope).SaveOverrideAsync(Confirmation, MessageChannel.Email, "en-gb", "Booked: {{ServiceName}}", "Hello {{ClientFirstName}}, see you {{AppointmentStart}}.");

        var text = await Service(scope).GetAsync(Confirmation, MessageChannel.Email, "en-GB");
        var row = await scope.ServiceProvider.GetRequiredService<SalonDbContext>().MessageTemplates.SingleAsync();

        text.IsTenantOverride.ShouldBeTrue();
        text.Subject.ShouldBe("Booked: {{ServiceName}}");
        text.Body.ShouldBe("Hello {{ClientFirstName}}, see you {{AppointmentStart}}.");
        row.TenantId.ShouldBe(_tenantA.Id);
        row.Key.ShouldBe(Confirmation);
        row.Channel.ShouldBe(MessageChannel.Email);
        row.Culture.ShouldBe("en-GB");
        row.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task An_override_saved_by_tenant_A_is_not_visible_to_tenant_B()
    {
        await using (var scopeA = factory.CreateTenantScope(_tenantA.Id))
        {
            await Service(scopeA).SaveOverrideAsync(Confirmation, MessageChannel.Sms, "en-GB", null, "Tenant A text {{ClientFirstName}}");
        }

        await using var scopeB = factory.CreateTenantScope(_tenantB.Id);
        var textB = await Service(scopeB).GetAsync(Confirmation, MessageChannel.Sms, "en-GB");
        var rowsB = await scopeB.ServiceProvider.GetRequiredService<SalonDbContext>().MessageTemplates.CountAsync();

        textB.IsTenantOverride.ShouldBeFalse();
        textB.Body.ShouldStartWith("Hi {{ClientFirstName}}");
        rowsB.ShouldBe(0);
    }

    [Fact]
    public async Task Save_rejects_a_token_the_catalogue_does_not_allow_and_names_it()
    {
        await using var scope = factory.CreateTenantScope(_tenantA.Id);

        var ex = await Should.ThrowAsync<ArgumentException>(() =>
            Service(scope).SaveOverrideAsync(Confirmation, MessageChannel.Sms, "en-GB", null, "Hi {{ClientFirstName}}, pay {{DepositAmount}}"));

        ex.Message.ShouldContain("DepositAmount");
        (await scope.ServiceProvider.GetRequiredService<SalonDbContext>().MessageTemplates.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Save_rejects_a_subject_for_sms_and_requires_one_for_email()
    {
        await using var scope = factory.CreateTenantScope(_tenantA.Id);

        await Should.ThrowAsync<ArgumentException>(() => Service(scope).SaveOverrideAsync(Confirmation, MessageChannel.Sms, "en-GB", "No subjects in SMS", "Body"));
        await Should.ThrowAsync<ArgumentException>(() => Service(scope).SaveOverrideAsync(Confirmation, MessageChannel.Email, "en-GB", null, "Body"));
        await Should.ThrowAsync<ArgumentException>(() => Service(scope).SaveOverrideAsync(Confirmation, MessageChannel.Sms, "en-GB", null, "   "));
        await Should.ThrowAsync<ArgumentException>(() => Service(scope).SaveOverrideAsync(Confirmation, MessageChannel.Sms, "fr-FR", null, "Bonjour"));
        await Should.ThrowAsync<MessageTemplateNotFoundException>(() => Service(scope).SaveOverrideAsync("Booking.Nope", MessageChannel.Sms, "en-GB", null, "Body"));
    }

    [Fact]
    public async Task Render_with_every_token_is_complete_and_substitutes_subject_and_body()
    {
        await using var scope = factory.CreateTenantScope(_tenantA.Id);
        var tokens = MessageTokens.Empty
            .Add("SalonName", "Studio 20")
            .Add("ClientFirstName", "Anna")
            .Add("ServiceName", "Cut")
            .Add("StaffName", "Kasia")
            .Add("AppointmentStart", "Fri 14 Nov, 10:00")
            .Add("CancelUrl", "https://studio-20.example/c/1");

        var rendered = await Service(scope).RenderAsync(Confirmation, MessageChannel.Email, "en-GB", tokens);

        rendered.IsComplete.ShouldBeTrue(string.Join(", ", rendered.MissingTokens));
        rendered.Subject.ShouldBe("Your Cut at Studio 20 is booked");
        rendered.Body.ShouldContain("Hi Anna,");
        rendered.Body.ShouldContain("with Kasia at Studio 20 is booked for Fri 14 Nov, 10:00");
        rendered.Body.ShouldContain("https://studio-20.example/c/1");
        rendered.Body.ShouldNotContain("{{");
    }

    [Fact]
    public async Task Render_with_a_missing_token_lists_it_once_across_subject_and_body()
    {
        await using var scope = factory.CreateTenantScope(_tenantA.Id);
        var tokens = MessageTokens.Empty.Add("SalonName", "Studio 20").Add("ClientFirstName", "Anna").Add("StaffName", "Kasia");

        var rendered = await Service(scope).RenderAsync(Confirmation, MessageChannel.Email, "en-GB", tokens);

        rendered.IsComplete.ShouldBeFalse();
        rendered.MissingTokens.ShouldBe(["ServiceName", "AppointmentStart", "CancelUrl"]);
        rendered.Subject.ShouldBe("Your  at Studio 20 is booked");
    }

    [Fact]
    public async Task Remove_override_restores_the_default()
    {
        await using var scope = factory.CreateTenantScope(_tenantA.Id);
        var service = Service(scope);
        await service.SaveOverrideAsync(Confirmation, MessageChannel.Sms, "en-GB", null, "Custom {{ClientFirstName}}");
        (await service.GetAsync(Confirmation, MessageChannel.Sms, "en-GB")).IsTenantOverride.ShouldBeTrue();

        await service.RemoveOverrideAsync(Confirmation, MessageChannel.Sms, "en-GB");
        await service.RemoveOverrideAsync(Confirmation, MessageChannel.Sms, "en-GB");

        var text = await service.GetAsync(Confirmation, MessageChannel.Sms, "en-GB");
        text.IsTenantOverride.ShouldBeFalse();
        text.Body.ShouldStartWith("Hi {{ClientFirstName}}");
        (await scope.ServiceProvider.GetRequiredService<SalonDbContext>().MessageTemplates.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_second_save_is_visible_at_once_and_updates_the_row_in_place()
    {
        await using (var scope = factory.CreateTenantScope(_tenantA.Id))
        {
            var service = Service(scope);
            // Warm the cache first so the test proves every save invalidates it.
            await service.GetAsync(Confirmation, MessageChannel.Sms, "en-GB");
            await service.SaveOverrideAsync(Confirmation, MessageChannel.Sms, "en-GB", null, "Version one {{ClientFirstName}}");
            (await service.GetAsync(Confirmation, MessageChannel.Sms, "en-GB")).Body.ShouldBe("Version one {{ClientFirstName}}");
            await service.SaveOverrideAsync(Confirmation, MessageChannel.Sms, "en-GB", null, "Version two {{ClientFirstName}}");
            (await service.GetAsync(Confirmation, MessageChannel.Sms, "en-GB")).Body.ShouldBe("Version two {{ClientFirstName}}");
        }

        await using var fresh = factory.CreateTenantScope(_tenantA.Id);
        (await Service(fresh).GetAsync(Confirmation, MessageChannel.Sms, "en-GB")).Body.ShouldBe("Version two {{ClientFirstName}}");
        (await fresh.ServiceProvider.GetRequiredService<SalonDbContext>().MessageTemplates.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Without_a_tenant_every_operation_throws()
    {
        await using var scope = factory.CreateTenantScope(null);
        var service = Service(scope);

        await Should.ThrowAsync<TenantViolationException>(() => service.GetAsync(Confirmation, MessageChannel.Sms, "en-GB"));
        await Should.ThrowAsync<TenantViolationException>(() => service.RenderAsync(Confirmation, MessageChannel.Sms, "en-GB", MessageTokens.Empty));
        await Should.ThrowAsync<TenantViolationException>(() => service.SaveOverrideAsync(Confirmation, MessageChannel.Sms, "en-GB", null, "Body"));
        await Should.ThrowAsync<TenantViolationException>(() => service.RemoveOverrideAsync(Confirmation, MessageChannel.Sms, "en-GB"));
    }

    private static IMessageTemplateService Service(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IMessageTemplateService>();
}