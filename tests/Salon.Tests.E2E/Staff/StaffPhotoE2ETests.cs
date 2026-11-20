using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Staff;

/// <summary>
/// Staff profile photos on a phone (ADR 0088): the owner opens a staff member from the menu drawer, uploads a small
/// picture and sees the face on the page; a client on the public booking page then finds that face in the "who with"
/// picker beside the letters of the people without one; and removing the photo brings the letter back. Each page is
/// captured for the PR, passes axe and never scrolls sideways.
/// </summary>
public class StaffPhotoE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";
    private const int Side = 96;

    [Fact]
    public async Task The_owner_uploads_a_face_and_a_client_sees_it_in_the_picker_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.UseDefaultSettingsAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        // The owner signs in and finds the team in the menu drawer.
        (await page.GotoAsync(app.TenantUrl(Slug) + "/admin/appointments")).ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync("**/admin/appointments");
        await page.OpenNavAsync("nav-staff");
        await page.ReachAsync("**/admin/staff");
        var styled = await StylesheetLoadedAsync(page);
        var anna = page.GetByTestId("staff-card").Filter(new LocatorFilterOptions { HasText = "Anna Kowalska" });
        (await anna.Locator("[data-avatar=letter]").TextContentAsync()).ShouldBe("A", "no picture yet: the first letter of the name");

        // Anna's page: the letter, a file picker and no "remove" while there is nothing to remove.
        await anna.GetByTestId("edit-staff").ClickAsync();
        await page.ReachAsync("**/admin/staff/*/edit");
        var photo = page.GetByTestId("staff-photo");
        (await photo.Locator("[data-avatar=letter]").CountAsync()).ShouldBe(1);
        (await page.GetByTestId("staff-photo-remove").CountAsync()).ShouldBe(0);
        await page.GetByTestId("staff-photo-file").SetInputFilesAsync(new FilePayload { Name = "anna.png", MimeType = "image/png", Buffer = FacePng() });
        await page.GetByTestId("staff-photo-upload").ClickAsync();

        // The post comes back to the same address, so wait for what it left there.
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("Photo saved.");
        await ExpectFaceAsync(page, "[data-testid=staff-photo] img[data-avatar=photo]");
        (await photo.Locator("img[data-avatar=photo]").GetAttributeAsync("alt")).ShouldBe(string.Empty, "decorative: the name is beside it");
        await CheckPageAsync(page, styled, "staff-photo-upload", "staff-photo-edit-mobile-390.png");

        // A client, not signed in, choosing who to book with.
        await using (var visitorContext = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
            IsMobile = true,
            HasTouch = true,
        }))
        {
            var visitor = await visitorContext.NewPageAsync();
            (await visitor.GotoAsync(app.TenantUrl(Slug) + "/book")).ShouldNotBeNull().Ok.ShouldBeTrue();
            await visitor.GetByTestId("book-service").First.TapAsync();
            await visitor.ReachAsync(url => url.Contains("/book/", StringComparison.Ordinal) && !url.EndsWith("/book", StringComparison.Ordinal));

            var picker = visitor.GetByTestId("staff-picker");
            var options = picker.GetByTestId("staff-option");
            (await options.CountAsync()).ShouldBe(3);
            await ExpectFaceAsync(visitor, "[data-testid=staff-picker] img[data-avatar=photo]");
            (await picker.Locator("img[data-avatar=photo]").CountAsync()).ShouldBe(1, "only Anna has a picture");
            var annaOption = options.Filter(new LocatorFilterOptions { HasText = "Anna Kowalska" });
            (await annaOption.Locator("img[data-avatar=photo]").GetAttributeAsync("alt")).ShouldBe(string.Empty);
            (await picker.Locator("[data-avatar=letter]").AllTextContentsAsync()).ShouldBe(["T", "P"], "Tom and Priya show their letter");
            (await picker.GetByTestId("staff-any").Locator("[data-avatar]").CountAsync()).ShouldBe(0, "anyone has no face");
            await CheckPageAsync(visitor, await StylesheetLoadedAsync(visitor), null, "book-slots-faces-mobile-390.png");

            // The face is still a link to that person's times.
            await annaOption.TapAsync();
            await visitor.ReachAsync(url => url.Contains("staff=", StringComparison.Ordinal));
            (await visitor.GetByTestId("staff-option").Filter(new LocatorFilterOptions { HasText = "Anna Kowalska" }).GetAttributeAsync("aria-current")).ShouldBe("true");
        }

        // Removing it brings the letter back, here and for clients.
        await page.GetByTestId("staff-photo-remove").ClickAsync();
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("Photo removed.");
        (await photo.Locator("img[data-avatar=photo]").CountAsync()).ShouldBe(0);
        (await photo.Locator("[data-avatar=letter]").TextContentAsync()).ShouldBe("A");
    }

    /// <summary>Waits until the picture has arrived and the browser has made an image of it, at the size it was drawn.</summary>
    private static async Task ExpectFaceAsync(IPage page, string selector)
        => await page.WaitForFunctionAsync(
            "([selector, side]) => { const face = document.querySelector(selector); return !!face && face.complete && face.naturalWidth === side; }",
            new object[] { selector, Side });

    /// <summary>Touch target (when styled), no sideways scroll, a screenshot for the PR and an axe pass.</summary>
    private static async Task CheckPageAsync(IPage page, bool styled, string? buttonTestId, string screenshot)
    {
        if (styled)
        {
            if (buttonTestId is not null)
            {
                (await page.GetByTestId(buttonTestId).BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
            }

            (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);
        }

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, screenshot),
            FullPage = true,
        });
        var axe = await page.RunAxe();
        axe.Violations.ShouldBeEmpty(screenshot + ": " + string.Join("; ", axe.Violations.Select(v => $"{v.Id}: {v.Help}")));
    }

    /// <summary>True when a stylesheet resolved with rules in it (a 404 or an empty body parses to zero rules).</summary>
    private static Task<bool> StylesheetLoadedAsync(IPage page)
        => page.EvaluateAsync<bool>("() => Array.from(document.styleSheets).some(s => s.href && s.cssRules.length > 0)");

    /// <summary>
    /// A small picture made here, so the test needs no file on disk: a square of 96 pixels with a round head and
    /// shoulders drawn in two colours, written out as a real PNG (signature, IHDR, one IDAT, IEND).
    /// </summary>
    private static byte[] FacePng()
    {
        var raw = new byte[Side * (1 + (Side * 3))];
        for (var y = 0; y < Side; y++)
        {
            var row = y * (1 + (Side * 3));
            raw[row] = 0; // No filter on this line.
            for (var x = 0; x < Side; x++)
            {
                var head = ((x - 48) * (x - 48)) + ((y - 38) * (y - 38)) <= 20 * 20;
                var shoulders = ((x - 48) * (x - 48)) + ((y - 104) * (y - 104)) <= 40 * 40;
                var (r, g, b) = head || shoulders ? ((byte)0x6D, (byte)0x28, (byte)0xD9) : ((byte)0xFD, (byte)0xE6, (byte)0x8A);
                var at = row + 1 + (x * 3);
                (raw[at], raw[at + 1], raw[at + 2]) = (r, g, b);
            }
        }

        using var packed = new MemoryStream();
        using (var zlib = new ZLibStream(packed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, Side);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), Side);
        header[8] = 8; // Bits per channel.
        header[9] = 2; // Colour type: RGB.
        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", packed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(MemoryStream png, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        byte[] typed = [.. Encoding.ASCII.GetBytes(type), .. data];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        png.Write(number);
        png.Write(typed);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc32(typed));
        png.Write(number);
    }

    /// <summary>The checksum every PNG chunk ends with (the same polynomial as zip), written out because the test project has no package for it.</summary>
    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}