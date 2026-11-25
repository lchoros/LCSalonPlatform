using System.ComponentModel;

namespace Salon.Mobile.Screens.Contracts;

// What the pages bind to (ADR 0097). The pages know only these interfaces; the samples in Design/ fill them today and
// the screen models of Salon.Mobile.Core replace them by a change of registration in MauiProgram. Conventions:
// - Values are the API's own (BookingContracts.cs): dates "yyyy-MM-dd", clock times "HH:mm", money a decimal with its
//   currency code. The app turns them into words (Formatting/), so every word on screen comes from AppResources.
// - Lists are replaced, not changed in place: a new answer from the server raises PropertyChanged for the list.
// - Commands that act on an item take that item as their parameter.

/// <summary>What every screen has: work in progress, a refusal to show, and a moment to load.</summary>
public interface IScreen : INotifyPropertyChanged
{
    /// <summary>A call to the salon is on its way; the page fades what will change and blocks a second tap.</summary>
    bool IsBusy { get; }

    /// <summary>A sentence fit to show at the top of the page (the server's <c>ApiProblem.Message</c>), or null.</summary>
    string? Problem { get; }

    /// <summary>The page has come into sight (first time or on the way back).</summary>
    Task AppearingAsync();
}

/// <summary>A price as the server states it.</summary>
public interface IPriced
{
    decimal Price { get; }

    /// <summary>The people who offer it charge differently: <see cref="Price"/> is the lowest ("from £16.00").</summary>
    bool PriceIsFrom { get; }

    /// <summary>ISO 4217 code, as in the API ("GBP").</summary>
    string Currency { get; }
}

/// <summary>A visit in one glance: the details summary and the confirmation.</summary>
public interface IVisitSummary
{
    /// <summary>Every service of the visit, joined ("Cut + Colour").</summary>
    string ServiceName { get; }

    /// <summary>Who with, joined when several people; null for "anyone available".</summary>
    string? StaffName { get; }

    /// <summary>"yyyy-MM-dd" in the salon's zone.</summary>
    string LocalDate { get; }

    /// <summary>"HH:mm" in the salon's zone.</summary>
    string LocalTime { get; }

    /// <summary>The total.</summary>
    IPriced Price { get; }
}