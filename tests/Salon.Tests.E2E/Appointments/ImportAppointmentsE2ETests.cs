using System.Globalization;
using System.Text;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using NodaTime;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Appointments;

/// <summary>
/// The demo owner on a phone brings the coming weeks of the old system's diary over from a file (ADR 0067): upload,
/// a preview that says what would be added, what would not and why, and until when the old system must keep
/// reminding; a confirm; and the visit is on the day list. 390 px, axe clean, no horizontal scroll.
/// </summary>
public class ImportAppointmentsE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_demo_owner_reads_the_old_diary_in_on_a_phone_through_upload_preview_and_result()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await app.UseDefaultSettingsAsync(tenantId);

        // A client of the salon (with a visit long ago) and a day far enough ahead that the demo diary is empty then.
        var suffix = Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture);
        var lastName = "Diary" + suffix;
        await app.AddVisitAsync(tenantId, "Wendy", lastName, -Duration.FromDays(30), deposit: 0m);
        var staff = await app.FirstStaffNameAsync(tenantId);
        var day = SystemClock.Instance.GetCurrentInstant().InZone(DateTimeZoneProviders.Tzdb["Europe/London"]).Date.PlusDays(45);
        var date = day.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var csv =
            "Date,Start time,End time,Client,Service,Staff,Price,Status,Notes\n" +
            $"{date},07:00,07:45,Wendy {lastName},Cut & blow-dry,{staff},50.00,Confirmed,colour reaction last time\n" +
            $"{date},08:00,,Wendy {lastName},Highlights,{staff},,,\n" +
            $"{date},09:00,,Wendy {lastName},Cut & blow-dry,{staff},,Cancelled,\n";

        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(app.TenantUrl(Slug) + "/admin/appointments/import");
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync("**/admin/appointments/import");

        // 1. The page says the order of things, what a file looks like and what will and will not happen.
        (await page.GetByTestId("import-columns").TextContentAsync()).ShouldNotBeNull().ShouldContain("Date,Start time,End time,Client");
        (await page.GetByTestId("import-rules").TextContentAsync()).ShouldNotBeNull().ShouldContain("Notes are not brought over");
        await CheckAndShootAsync(page, "diary-import-upload-mobile-390.png");

        // 2. The file is checked: one to add, one with a service the salon does not have, one cancelled. Nothing yet.
        await page.GetByLabel("CSV file").SetInputFilesAsync(new FilePayload
        {
            Name = "diary.csv",
            MimeType = "text/csv",
            Buffer = Encoding.UTF8.GetBytes(csv),
        });
        await page.GetByTestId("import-upload").ClickAsync();
        await page.ReachAsync(url => url.Contains("/admin/appointments/import/", StringComparison.Ordinal));
        (await page.GetByTestId("count-new").TextContentAsync()).ShouldNotBeNull().Trim().ShouldBe("1");
        (await page.GetByTestId("count-cancelled").TextContentAsync()).ShouldNotBeNull().Trim().ShouldBe("1");
        (await page.GetByTestId("count-not-brought").TextContentAsync()).ShouldNotBeNull().Trim().ShouldBe("1");
        (await page.GetByTestId("unknown-service").TextContentAsync()).ShouldNotBeNull().ShouldContain("Highlights: 1 row");
        var added = page.Locator("[data-testid='import-row'][data-status='New']");
        (await added.GetByTestId("import-row-details").TextContentAsync()).ShouldBe($"Cut & blow-dry · {staff} · 45 min · £50.00");
        (await added.GetByTestId("import-row-when").TextContentAsync()).ShouldNotBeNull().ShouldContain(day.ToString("ddd d MMM yyyy", CultureInfo.GetCultureInfo("en-GB")));
        (await page.GetByTestId("import-reminders").TextContentAsync()).ShouldNotBeNull().ShouldContain("No visit to add starts before ");
        (await page.GetByTestId("import-confirm").TextContentAsync()).ShouldBe("Add 1 visit");
        await CheckAndShootAsync(page, "diary-import-preview-mobile-390.png");

        // 3. Confirmed.
        await page.GetByTestId("import-confirm").ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='import-result']");
        (await page.GetByTestId("result-added").TextContentAsync()).ShouldNotBeNull().Trim().ShouldBe("1");
        (await page.GetByTestId("result-invalid").TextContentAsync()).ShouldNotBeNull().ShouldContain("1 future visit could not be read in.");
        await CheckAndShootAsync(page, "diary-import-result-mobile-390.png");

        // 4. The visit is on that day's list.
        await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/appointments?date={day:yyyy-MM-dd}");
        await page.ReachAsync("**/admin/appointments?date=*");
        (await page.GetByText($"Wendy {lastName}").CountAsync()).ShouldBeGreaterThanOrEqualTo(1);

        // 5. Read in by mistake: the file is taken out again (ADR 0074) and the visit is gone from the day.
        await page.GotoAsync(app.TenantUrl(Slug) + "/admin/appointments/import");
        await page.ReachAsync("**/admin/appointments/import");
        await page.GetByTestId("undo-import-link").ClickAsync();
        await page.ReachAsync("**/admin/appointments/import/undo");
        var batch = page.GetByTestId("undo-batch").First;
        (await batch.GetAttributeAsync("data-untouched")).ShouldBe("1", "this run's file is the newest read-in");
        await CheckAndShootAsync(page, "diary-import-undo-mobile-390.png");
        await batch.GetByTestId("undo-import").ClickAsync();
        await page.ReachAsync("**/admin/appointments/import/undo");
        (await page.GetByTestId("flash").TextContentAsync()).ShouldBe("The file is out of the diary: 1 visit removed, 0 kept.");
        await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/appointments?date={day:yyyy-MM-dd}");
        await page.ReachAsync("**/admin/appointments?date=*");
        (await page.GetByText($"Wendy {lastName}").CountAsync()).ShouldBe(0, "the visit went with the file");
    }

    private static async Task CheckAndShootAsync(IPage page, string fileName)
    {
        (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, fileName),
            FullPage = true,
        });
        var axe = await page.RunAxe();
        axe.Violations.ShouldBeEmpty(string.Join("; ", axe.Violations.Select(v => $"{v.Id}: {v.Help}")));
    }
}