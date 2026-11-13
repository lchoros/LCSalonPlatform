using Microsoft.EntityFrameworkCore;

using Salon.Data;
using Salon.Plugin.Import;
using Salon.Plugin.Payments.Manual;
using Salon.Plugin.Sms.Log;
using Salon.Services.Seeding;
using Salon.Web.Framework.Identity;
using Salon.Web.Framework.Infrastructure;
using Salon.Web.Framework.Jobs;
using Salon.Web.Framework.Localization;
using Salon.Web.Framework.Plugins;
using Salon.Web.Framework.Tenancy;

var builder = WebApplication.CreateBuilder(args);

// Slices live in this assembly; their handlers and validators are discovered from it.
builder.Services.AddSalonFramework(builder.Configuration, typeof(Program).Assembly);
builder.Services.AddSalonAuthentication(builder.Configuration);

// Phase 1 plugins: record-only payments, log + Mailpit SMS, CSV client import. Real gateways arrive at M3 behind the same interfaces.
builder.Services.AddManualPaymentProvider();
builder.Services.AddLogSmsProvider();
builder.Services.AddCsvClientImport();
builder.Services.AddSalonPlugins();
builder.Services.AddSalonJobs(builder.Configuration);

var app = builder.Build();

// Local development convenience; CI and tests migrate explicitly, production will use a migration bundle at M3.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Database.MigrateAsync();
}

// Development only: the studio-20 demo salon, created once and never touched again (see DemoSeeder).
if (app.Configuration.GetValue<bool>("Database:SeedDemo"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<DemoSeeder>().SeedAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseRateLimiter();
app.UseSalonPlugins();
// Authentication first, so the tenant middleware can compare the cookie's tenant claim with the host's tenant (ADR 0009).
app.UseSalonAuthentication();
app.UseSalonTenancy();
app.UseSalonLocalization();
app.UseSalonScheduleTasks();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

/// <summary>Exposed so WebApplicationFactory in the integration and E2E tests can host the app.</summary>
public partial class Program;