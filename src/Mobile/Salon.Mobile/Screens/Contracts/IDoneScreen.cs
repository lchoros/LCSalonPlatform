using System.Windows.Input;

namespace Salon.Mobile.Screens.Contracts;

/// <summary>Step 5: the confirmation (GET visits/{id}).</summary>
public interface IDoneScreen : IScreen
{
    IVisitSummary Visit { get; }

    /// <summary>Opens <c>BookedVisit.ManageUrl</c> in the browser: moving and cancelling happen on the web.</summary>
    ICommand ManageCommand { get; }

    /// <summary>Starts over at the service list.</summary>
    ICommand BookAnotherCommand { get; }
}