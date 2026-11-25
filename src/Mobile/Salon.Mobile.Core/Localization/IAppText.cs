using System.ComponentModel;
using System.Globalization;

namespace Salon.Mobile.Core.Localization;

/// <summary>
/// The app's words in the salon's language (<c>SalonInfo.Culture</c>), so they match the sentences the server sends.
/// Raises <see cref="INotifyPropertyChanged.PropertyChanged"/> for the indexer when the language changes, so
/// <c>Text[key]</c> bindings refresh.
/// </summary>
public interface IAppText : INotifyPropertyChanged
{
    /// <summary>The culture numbers, prices and dates are shown in.</summary>
    CultureInfo Culture { get; }

    /// <summary>The dictionary in use: "en-GB" or "pl-PL".</summary>
    string Language { get; }

    /// <summary>The words for a key (<see cref="TextKeys"/>); English where the language lacks it; the key itself where both do.</summary>
    string this[string key] { get; }

    /// <summary>The words for a key with its {0} filled in, in <see cref="Culture"/>.</summary>
    string Format(string key, params object?[] args);

    /// <summary>Speaks the salon's culture from now on; an unknown culture falls back to en-GB.</summary>
    void UseCulture(string? cultureName);
}