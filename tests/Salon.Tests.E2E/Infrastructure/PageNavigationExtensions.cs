using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Salon.Tests.E2E.Infrastructure;

/// <summary>
/// Waiting for a navigation without the race in <c>page.WaitForURLAsync</c>: when the URL already matches, Playwright
/// then waits for a <c>load</c> event, and on a cold process (the first run after a build, so every CI run) that event
/// can fire between its "already loaded?" check and its subscription. The wait then sees only <c>networkidle</c> and
/// times out after 30 s although the page is there. These helpers poll the URL and ask the document itself whether it
/// has finished loading, so there is no event to miss.
/// </summary>
public static class PageNavigationExtensions
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(20);

    /// <summary>Waits until the page is at a URL matching <paramref name="glob"/> (<c>*</c> stays inside a path segment, <c>**</c> crosses them, everything else is literal) and has finished loading.</summary>
    public static Task ReachAsync(this IPage page, string glob)
    {
        ArgumentNullException.ThrowIfNull(glob);
        var pattern = GlobToRegex(glob);
        return page.ReachAsync(pattern.IsMatch, glob);
    }

    /// <summary>Waits until the page is at a URL accepted by <paramref name="matches"/> and has finished loading.</summary>
    public static Task ReachAsync(this IPage page, Func<string, bool> matches) => page.ReachAsync(matches, "the expected URL");
    /// <summary>
    /// Waits until the booking's time page has finished changing in place (ADR 0094, stage 6): what was fetched has
    /// arrived and has come up from pale. For a tap that keeps the address pattern a walk is not waiting for.
    /// </summary>
    public static Task SettleBookingAsync(this IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return page.WaitForFunctionAsync("() => !document.querySelector('[data-booking][aria-busy]') && !document.getAnimations().some(a => a.playState === 'running' && a.effect?.target?.closest?.('[data-booking]'))");
    }


    /// <summary>
    /// On the public time page, opens the staff sheet and answers "who with" with "anyone available" where it is owed (more than one
    /// person offers the service; ADR 0094, stages 3 and 6), and waits for the answered page. Where one person
    /// offers the service there is no question and this does nothing.
    /// </summary>
    public static async Task ChooseAnyoneAsync(this IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (await page.GetByTestId("staff-required").CountAsync() == 0)
        {
            return;
        }

        await page.GetByTestId("change-staff").ClickAsync();
        await page.GetByTestId("staff-any").ClickAsync();
        await page.ReachAsync(url => url.Contains("staff=any", StringComparison.Ordinal));
    }

    /// <summary>The regular expression equivalent of a Playwright-style URL glob, anchored at both ends.</summary>
    public static Regex GlobToRegex(string glob)
    {
        ArgumentNullException.ThrowIfNull(glob);
        var pattern = new StringBuilder("^");
        for (var i = 0; i < glob.Length; i++)
        {
            if (glob[i] != '*')
            {
                pattern.Append(Regex.Escape(glob[i].ToString()));
                continue;
            }

            var crossesSegments = i + 1 < glob.Length && glob[i + 1] == '*';
            pattern.Append(crossesSegments ? ".*" : "[^/]*");
            if (crossesSegments)
            {
                i++;
            }
        }

        return new Regex(pattern.Append('$').ToString(), RegexOptions.CultureInvariant);
    }

    private static async Task ReachAsync(this IPage page, Func<string, bool> matches, string description)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(matches);

        var waited = Stopwatch.StartNew();
        while (!matches(page.Url))
        {
            if (waited.Elapsed > Timeout)
            {
                throw new TimeoutException($"The page did not reach {description} within {Timeout.TotalSeconds:F0} s; it is at {page.Url}.");
            }

            await Task.Delay(PollInterval);
        }

        // Evaluated inside the page, and re-evaluated by Playwright if a navigation replaces the document meanwhile.
        // The page also has to have finished arriving: where the browser cross-fades between pages (ADR 0092) a
        // screenshot or an axe pass taken during the fade sees two pages at once.
        // The booking's time page also changes in place (ADR 0094, stage 6): wait until what was fetched has arrived
        // and has finished coming up from pale.
        await page.WaitForFunctionAsync("() => document.readyState === 'complete' && !document.querySelector('[data-booking][aria-busy]') && !document.getAnimations().some(a => a.effect?.pseudoElement?.startsWith('::view-transition') || (a.playState === 'running' && a.effect?.target?.closest?.('[data-booking]')))");
    }

    /// <summary>
    /// Opens an admin menu destination the way a person does (ADR 0080): on a phone the menu lives in the drawer
    /// behind the hamburger, on a desk the sidebar is simply there.
    /// </summary>
    public static async Task OpenNavAsync(this IPage page, string navTestId)
    {
        var hamburger = page.GetByTestId("open-menu");
        if (await hamburger.IsVisibleAsync())
        {
            await hamburger.ClickAsync();
        }

        await page.GetByTestId(navTestId).ClickAsync();
    }
}