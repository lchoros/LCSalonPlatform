namespace Salon.Tests.Architecture;

/// <summary>
/// E2E tests wait for navigation through <c>ReachAsync</c> (tests/Salon.Tests.E2E/Infrastructure/PageNavigationExtensions.cs).
/// Playwright's own <c>WaitForURLAsync</c> / <c>WaitForLoadStateAsync</c> / <c>WaitForNavigationAsync</c> can miss the
/// load event on a cold process and then time out after 30 s although the page is there; that cost two days of red CI.
/// </summary>
public class E2ENavigationWaitTests
{
    private static readonly string[] RacyCalls = [".WaitForURLAsync(", ".WaitForLoadStateAsync(", ".WaitForNavigationAsync("];

    [Fact]
    public void No_e2e_test_waits_with_the_racy_playwright_calls()
    {
        var root = Path.Combine(RepositoryRoot(), "tests", "Salon.Tests.E2E");

        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => (line, number: index + 1))
                .Where(l => RacyCalls.Any(call => l.line.Contains(call, StringComparison.Ordinal)))
                .Select(l => $"{Path.GetRelativePath(root, file)}:{l.number}"))
            .ToList();

        offenders.ShouldBeEmpty("use page.ReachAsync(...) instead: " + string.Join(", ", offenders));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Salon.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Salon.slnx not found above " + AppContext.BaseDirectory);
    }
}