using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using Npgsql;
using Salon.Core.Appointments;
using Salon.Core.Identity;
using Salon.Core.Messaging;
using Salon.Core.Notifications;
using Salon.Core.Products;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Formatting;
using Salon.Services.Localization;
using Salon.Services.Reports;
using Salon.Services.Settings;
using Salon.Services.Tenancy;

namespace Salon.Services.Notifications;

/// <param name="Day">The day the numbers are of, on the salon's calendar.</param>
/// <param name="Text">Plain text, in the salon's language. Numbers only: no client is named.</param>
public sealed record DailySummaryEmail(LocalDate Day, string Subject, string Text);

/// <param name="Recipients">How many owner logins the summary would go to.</param>
public sealed record DailySummaryPreview(DailySummaryEmail Email, int Recipients);

/// <summary>The day's numbers by email to the owners of the salon in scope (ADR 0050).</summary>
public interface IDailySummaryService
{
    /// <summary>What today's summary would say if it went out now, and to how many owners.</summary>
    Task<DailySummaryPreview> PreviewAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends today's summary when it is switched on, its time has come and the day was not sent yet. Returns how many
    /// owners it was handed over for, or null when nothing was due. Safe to call as often as wanted and from several
    /// workers: the day is claimed by a unique row before anything is sent.
    /// </summary>
    Task<int?> SendIfDueAsync(Instant now, CancellationToken cancellationToken = default);
}

internal sealed class DailySummaryService(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    ISettingService settings,
    IReportService reports,
    ILocalizationService localizer,
    IPublicUrlBuilder urls,
    IEmailSender email,
    IClock clock,
    ILogger<DailySummaryService> logger) : IDailySummaryService
{
    private const string Keys = "Settings.DailySummary.Email.";

    public async Task<DailySummaryPreview> PreviewAsync(CancellationToken cancellationToken = default)
    {
        var (tenant, zone) = await SalonAsync(cancellationToken).ConfigureAwait(false);
        var day = clock.GetCurrentInstant().InZone(zone).Date;
        var summary = await ComposeAsync(tenant, zone, day, cancellationToken).ConfigureAwait(false);
        return new DailySummaryPreview(summary, (await OwnersAsync(cancellationToken).ConfigureAwait(false)).Count);
    }

    public async Task<int?> SendIfDueAsync(Instant now, CancellationToken cancellationToken = default)
    {
        var chosen = await settings.LoadAsync<DailySummarySettings>(cancellationToken).ConfigureAwait(false);
        if (!chosen.Enabled)
        {
            return null;
        }

        var (tenant, zone) = await SalonAsync(cancellationToken).ConfigureAwait(false);
        var salonNow = now.InZone(zone).LocalDateTime;
        var day = salonNow.Date;
        if (!DailySummaryRules.IsDue(chosen, salonNow)
            || await db.DailySummaryDeliveries.AsNoTracking().AnyAsync(d => d.Day == day, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        // The claim comes first (as for reminders, ADR 0020): at worst a day is missed, never sent twice.
        var claim = new DailySummaryDelivery { TenantId = tenant.Id, Day = day, SentAt = now };
        db.DailySummaryDeliveries.Add(claim);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(claim).State = EntityState.Detached;
            return null;
        }

        var summary = await ComposeAsync(tenant, zone, day, cancellationToken).ConfigureAwait(false);
        var html = EmailBodies.ToHtml(summary.Text);
        var handed = 0;
        foreach (var owner in await OwnersAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                await email.SendAsync(new EmailMessage(owner, summary.Subject, html, summary.Text), cancellationToken).ConfigureAwait(false);
                handed++;
            }
#pragma warning disable CA1031 // One owner's mail server must not cost the other owners their summary; the failure is logged.
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                logger.LogWarning(ex, "The daily summary of {Day} for tenant {Slug} could not be handed over for one owner", day, tenant.Slug);
            }
        }

        claim.Recipients = handed;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return handed;
    }

    private async Task<DailySummaryEmail> ComposeAsync(TenantInfo tenant, DateTimeZone zone, LocalDate day, CancellationToken cancellationToken)
    {
        var culture = CultureInfo.GetCultureInfo(tenant.Culture);
        string Text(string key, params object?[] args) => string.Format(culture, localizer.Get(Keys + key, tenant.Culture), args);

        var report = await reports.GetAsync(null, day, day, cancellationToken).ConfigureAwait(false);
        var tomorrowStart = zone.AtStartOfDay(day.PlusDays(1)).ToInstant();
        var tomorrowEnd = zone.AtStartOfDay(day.PlusDays(2)).ToInstant();
        var active = AppointmentStateMachine.Active.ToArray();
        var tomorrow = await db.Appointments.AsNoTracking()
            .Where(a => active.Contains(a.Status) && a.Start >= tomorrowStart && a.Start < tomorrowEnd)
            .OrderBy(a => a.Start)
            .Select(a => a.Start)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var shelves = await db.Products.AsNoTracking()
            .Where(p => p.IsActive && p.LowStockAt != null)
            .Select(p => new { p.StockOnHand, p.LowStockAt })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var low = shelves.Count(p => StockRules.IsLow(p.StockOnHand, p.LowStockAt));

        var dayName = day.ToDayHeading(culture);
        var total = report.Takings.Total.ToDisplayString(culture);
        var text = new StringBuilder();
        text.Append(tenant.Name).Append(", ").Append(dayName).Append("\n\n");
        if (report.Takings.ByMethod.Count == 0)
        {
            text.Append(Text("NothingTaken")).Append('\n');
        }
        else
        {
            text.Append(Text("Taken", total)).Append('\n');
            foreach (var method in report.Takings.ByMethod)
            {
                text.Append("- ").Append(localizer.Get($"Payments.Method.{method.Method}", tenant.Culture)).Append(": ").Append(method.Amount.ToDisplayString(culture)).Append('\n');
            }
        }

        text.Append('\n');
        text.Append(Text("Completed", report.Visits.Completed)).Append('\n');
        text.Append(Text("NoShows", report.Visits.NoShows)).Append('\n');
        text.Append(Text("Cancelled", report.Visits.CancelledByClient + report.Visits.CancelledBySalon)).Append('\n');
        if (report.UnpaidCount > 0)
        {
            text.Append(Text("Unpaid", report.UnpaidCount, report.UnpaidTotal.ToDisplayString(culture))).Append('\n');
        }

        text.Append('\n');
        text.Append(tomorrow.Count == 0
            ? Text("TomorrowNone")
            : Text("Tomorrow", tomorrow.Count, tomorrow[0].InZone(zone).TimeOfDay.ToString("HH:mm", CultureInfo.InvariantCulture))).Append('\n');
        if (low > 0)
        {
            text.Append(Text("LowStock", low)).Append('\n');
        }

        var iso = day.ToIso();
        text.Append('\n').Append(Text("Report", urls.SalonUrl(tenant.Slug, $"/admin/reports?from={iso}&to={iso}"))).Append('\n');
        text.Append('\n').Append(Text("Footer")).Append('\n');

        return new DailySummaryEmail(day, Text("Subject", tenant.Name, total, dayName), text.ToString());
    }

    /// <summary>The addresses of the salon's active owner logins, in a stable order.</summary>
    private async Task<List<string>> OwnersAsync(CancellationToken cancellationToken)
        => await db.Users.AsNoTracking()
            .Where(u => u.Role == Role.Owner && u.IsActive)
            .OrderBy(u => u.Email)
            .Select(u => u.Email)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    private async Task<(TenantInfo Tenant, DateTimeZone Zone)> SalonAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("The daily summary is sent per salon; a tenant must be in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Tenant {tenantId} does not exist.");
        return (tenant, DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc);
    }
}