using Microsoft.EntityFrameworkCore;

using Salon.Data;
using Salon.Plugin.Payments.Manual;
using Salon.Plugin.Sms.Log;
using Salon.Web.Framework.Infrastructure;
using Salon.Web.Framework.Jobs;
using Salon.Web.Framework.Plugins;
using Salon.Web.Framework.Tenancy;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSalonFramework(builder.Configuration);

// Phase 1 plugins: record-only payments, log + Mailpit SMS. Real gateways arrive at M3 behind the same interfaces.
builder.Services.AddManualPaymentProvider();
builder.Services.AddLogSmsProvider();
builder.Services.AddSalonPlugins();
builder.Services.AddSalonJobs(builder.Configuration);

var app = builder.Build();

// Local development convenience; CI and tests migrate explicitly, production will use a migration bundle at M3.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Database.MigrateAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseSalonTenancy();
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