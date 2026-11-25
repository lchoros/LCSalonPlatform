using Salon.Mobile.Core.Mvvm;

namespace Salon.Mobile.Core.Screens.Services;

/// <summary>One bookable service as a line of the list, or as one option under a group's row.</summary>
/// <param name="Label">The option's own label under a group; the service's name otherwise.</param>
/// <param name="PriceText">"£45.00" or "from £16.00".</param>
/// <param name="DurationText">"45 min".</param>
public sealed record ServiceOption(Guid ServiceId, string Name, string Label, string? Description, string PriceText, string DurationText, bool IsPopular);

/// <summary>
/// A row of the service list: a single service (<see cref="OnlyOption"/> set), or the options of one service under one row
/// that opens (<see cref="IsGroup"/>, <see cref="Options"/>). A tap on the row is <c>ServicesScreen.TapRowCommand</c>.
/// </summary>
public sealed class ServiceRow : ObservableObject
{
    private bool _isExpanded;

    internal ServiceRow(string key, string name, string? description, string priceText, string durationText, IReadOnlyList<ServiceOption> options, string? optionsSummary, bool isExpanded)
    {
        Key = key;
        Name = name;
        Description = description;
        PriceText = priceText;
        DurationText = durationText;
        Options = options;
        OptionsSummary = optionsSummary;
        _isExpanded = isExpanded;
    }

    /// <summary>Stable across filtering: the service id, or "group:" and the group's name.</summary>
    public string Key { get; }

    public string Name { get; }

    public string? Description { get; }

    /// <summary>The service's price; for a group the lowest, with "from" unless every option costs the same.</summary>
    public string PriceText { get; }

    /// <summary>"45 min"; for a group the range, "30–60 min".</summary>
    public string DurationText { get; }

    /// <summary>The options under a group's row; the one service for a single row.</summary>
    public IReadOnlyList<ServiceOption> Options { get; }

    public bool IsGroup => Options.Count > 1;

    /// <summary>The service a single row books; null for a group.</summary>
    public ServiceOption? OnlyOption => IsGroup ? null : Options[0];

    /// <summary>"Options: Short, Long" under a closed group's row; null for a single row.</summary>
    public string? OptionsSummary { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        internal set => SetProperty(ref _isExpanded, value);
    }
}

/// <summary>A filter chip over the list: All, Popular, or a category.</summary>
public sealed class FilterChip : ObservableObject
{
    public const string AllKey = "all";
    public const string PopularKey = "popular";
    public const string CategoryPrefix = "category:";

    private bool _isSelected;

    internal FilterChip(string key, string label, string? category, bool isSelected)
    {
        Key = key;
        Label = label;
        Category = category;
        _isSelected = isSelected;
    }

    public string Key { get; }

    public string Label { get; }

    /// <summary>The category's name for a category chip; null for All and Popular.</summary>
    public string? Category { get; }

    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetProperty(ref _isSelected, value);
    }
}