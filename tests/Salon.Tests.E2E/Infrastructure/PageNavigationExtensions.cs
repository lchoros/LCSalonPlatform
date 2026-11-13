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
        await page.WaitForFunctionAsync("() => document.readyState === 'complete'");
    }
}