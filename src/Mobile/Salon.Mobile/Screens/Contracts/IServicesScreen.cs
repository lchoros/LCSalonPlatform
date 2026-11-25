using System.ComponentModel;
using System.Windows.Input;

namespace Salon.Mobile.Screens.Contracts;

/// <summary>Step 1: the salon's services, searched and filtered on the phone (BOOKING-API.md, "Services").</summary>
public interface IServicesScreen : IScreen
{
    /// <summary>"Add another service": the list is over the time screen and offers only what is not in the visit yet.</summary>
    bool IsAdding { get; }

    /// <summary>The salon has no service to book online at all.</summary>
    bool HasNoServices { get; }

    string SearchText { get; set; }

    /// <summary>All, Popular (when any service is), then the categories.</summary>
    IReadOnlyList<IServiceFilter> Filters { get; }

    /// <summary>What the search and the filter leave, in the salon's order.</summary>
    IReadOnlyList<IServiceRow> Rows { get; }

    /// <summary>The row whose description is open in the full-screen sheet; null while it is closed.</summary>
    IServiceRow? Reading { get; }

    /// <summary>Parameter: the <see cref="IServiceFilter"/> tapped.</summary>
    ICommand ChooseFilterCommand { get; }

    /// <summary>Clears the search and the filter ("Show all services").</summary>
    ICommand ShowAllCommand { get; }

    /// <summary>Parameter: an <see cref="IServiceRow"/> with options; opens or closes them under it.</summary>
    ICommand ToggleOptionsCommand { get; }

    /// <summary>Parameter: the <see cref="IServiceRow"/> whose "More" was tapped; sets <see cref="Reading"/>.</summary>
    ICommand ReadMoreCommand { get; }

    ICommand CloseReadingCommand { get; }

    /// <summary>Parameter: an <see cref="IServiceRow"/> without options, or an <see cref="IServiceOption"/>.</summary>
    ICommand BookCommand { get; }

    /// <summary>"Back to your booking", in <see cref="IsAdding"/> mode.</summary>
    ICommand BackCommand { get; }
}

public enum ServiceFilterKind
{
    All,
    Popular,
    Category,
}

public interface IServiceFilter : INotifyPropertyChanged
{
    ServiceFilterKind Kind { get; }

    /// <summary>The category's name for <see cref="ServiceFilterKind.Category"/>; null otherwise.</summary>
    string? CategoryName { get; }

    bool IsSelected { get; }
}

/// <summary>One row of the list: a service, or a service with options (variants) that open under it.</summary>
public interface IServiceRow : INotifyPropertyChanged
{
    string Name { get; }

    /// <summary>What the salon wrote; two lines under the row, all of it in the sheet.</summary>
    string? Description { get; }

    /// <summary>For a row with options: the shortest.</summary>
    int DurationMinutes { get; }

    /// <summary>For a row with options: the lowest, as "from".</summary>
    IPriced Price { get; }

    /// <summary>Empty for a plain service.</summary>
    IReadOnlyList<IServiceOption> Options { get; }

    bool HasOptions { get; }

    /// <summary>The options are showing.</summary>
    bool IsOpen { get; }
}

/// <summary>One option of a service ("Short hair", "Long hair").</summary>
public interface IServiceOption
{
    string Label { get; }

    int DurationMinutes { get; }

    IPriced Price { get; }
}