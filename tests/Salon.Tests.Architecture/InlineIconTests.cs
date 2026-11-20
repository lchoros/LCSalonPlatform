using System.Text.RegularExpressions;

namespace Salon.Tests.Architecture;

/// <summary>
/// An icon drawn in a view carries its own size. Without its stylesheet (a failed download, the CI pages) an unsized
/// <c>&lt;svg&gt;</c> grows to the width of the screen: the menu button became a screen-high drawing that pushed every
/// form's button below the fold, and two E2E walks stopped pressing "Save" on CI only.
/// </summary>
public partial class InlineIconTests
{
    [Fact]
    public void An_icon_sized_by_a_class_also_states_its_size_in_the_markup()
    {
        var root = Path.Combine(SolutionRoot(), "src", "Presentation", "Salon.Web");

        var unsized = Directory.EnumerateFiles(root, "*.cshtml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(path => IconPattern().Matches(File.ReadAllText(path))
                .Select(match => match.Value)
                .Where(tag => SizeClassPattern().IsMatch(tag) && !(tag.Contains(" width=\"", StringComparison.Ordinal) && tag.Contains(" height=\"", StringComparison.Ordinal)))
                .Select(tag => $"{Path.GetRelativePath(root, path)}: {tag}"))
            .ToList();

        unsized.ShouldBeEmpty("give each icon width and height attributes beside its size class: " + string.Join("; ", unsized));
    }

    private static string SolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Salon.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Salon.slnx not found above " + AppContext.BaseDirectory);
    }

    [GeneratedRegex("<svg\\b[^>]*>")]
    private static partial Regex IconPattern();

    [GeneratedRegex("class=\"[^\"]*\\bsize-\\d")]
    private static partial Regex SizeClassPattern();
}