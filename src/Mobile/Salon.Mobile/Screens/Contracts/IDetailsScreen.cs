using System.Windows.Input;

namespace Salon.Mobile.Screens.Contracts;

/// <summary>Step 3: who the client is (BOOKING-API.md, "Details"; the terms come from GET terms).</summary>
public interface IDetailsScreen : IScreen
{
    IVisitSummary Visit { get; }

    string FirstName { get; set; }

    string LastName { get; set; }

    string Phone { get; set; }

    /// <summary>Optional.</summary>
    string Email { get; set; }

    bool MarketingConsent { get; set; }

    /// <summary>The exact text beside the marketing tick (<c>BookingTerms.ConsentWording</c>), recorded with a yes.</summary>
    string ConsentWording { get; }

    /// <summary>A code is texted before the booking is made: the button says "Send code" rather than "Book".</summary>
    bool RequiresCode { get; }

    /// <summary>Per field, the server's message (<c>ApiProblem.Fields</c>) or a check made on the phone; null when fine.</summary>
    string? FirstNameError { get; }

    string? LastNameError { get; }

    string? PhoneError { get; }

    string? EmailError { get; }

    /// <summary>Opens the salon's privacy notice in the browser.</summary>
    ICommand OpenPrivacyCommand { get; }

    ICommand SubmitCommand { get; }

    ICommand BackCommand { get; }
}