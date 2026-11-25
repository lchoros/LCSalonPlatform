namespace Salon.Mobile.Core.Localization;

/// <summary>
/// Every word the app shows, by key. Each has a value in Localization/en-GB.json and Localization/pl-PL.json (a test
/// reads every constant here). The MAUI screens bind labels as <c>Text[key]</c> on a screen model's <c>Text</c>.
/// </summary>
public static class TextKeys
{
    public const string Offline = "Common.Offline";
    public const string Unexpected = "Common.Unexpected";
    public const string Retry = "Common.Retry";
    public const string Close = "Common.Close";
    public const string Minutes = "Common.Minutes";
    public const string MinutesRange = "Common.MinutesRange";
    public const string PriceFrom = "Common.PriceFrom";
    public const string TimeRange = "Common.TimeRange";
    public const string DateAndTime = "Common.DateAndTime";
    public const string SlotGone = "Common.SlotGone";

    public const string ServicesTitle = "Services.Title";
    public const string ServicesAddTitle = "Services.AddTitle";
    public const string ServicesAddLead = "Services.AddLead";
    public const string ServicesSearch = "Services.Search";
    public const string ServicesAll = "Services.All";
    public const string ServicesPopular = "Services.Popular";
    public const string ServicesNone = "Services.None";
    public const string ServicesNoMatch = "Services.NoMatch";
    public const string ServicesShowAll = "Services.ShowAll";
    public const string ServicesBook = "Services.Book";
    public const string ServicesAdd = "Services.Add";
    public const string ServicesBackToBooking = "Services.BackToBooking";
    public const string ServicesOptions = "Services.Options";
    public const string ServicesFull = "Services.Full";

    public const string TimeTitle = "Time.Title";
    public const string TimeEarlier = "Time.Earlier";
    public const string TimeLater = "Time.Later";
    public const string TimeMorning = "Time.Morning";
    public const string TimeAfternoon = "Time.Afternoon";
    public const string TimeEvening = "Time.Evening";
    public const string TimeNoTimesOnDay = "Time.NoTimesOnDay";
    public const string TimeNoTimesInWindow = "Time.NoTimesInWindow";
    public const string TimeStaff = "Time.Staff";
    public const string TimeNoPreference = "Time.NoPreference";
    public const string TimeNotChosen = "Time.NotChosen";
    public const string TimeRequired = "Time.Required";
    public const string TimeChoose = "Time.Choose";
    public const string TimeChange = "Time.Change";
    public const string TimeRemove = "Time.Remove";
    public const string TimeAddService = "Time.AddService";
    public const string TimeTotal = "Time.Total";
    public const string TimeContinue = "Time.Continue";
    public const string TimeMissingTime = "Time.MissingTime";
    public const string TimeMissingStaff = "Time.MissingStaff";
    public const string TimeMissingTimeAndStaff = "Time.MissingTimeAndStaff";
    public const string TimeCannotContinue = "Time.CannotContinue";
    public const string TimeCannotAddMore = "Time.CannotAddMore";
    public const string TimeStaffFor = "Time.StaffFor";
    public const string TimeStaffAtTime = "Time.StaffAtTime";
    public const string TimeAnyone = "Time.Anyone";
    public const string TimeLeave = "Time.Leave";
    public const string TimeDiscardTitle = "Time.DiscardTitle";
    public const string TimeDiscardText = "Time.DiscardText";
    public const string TimeDiscardStay = "Time.DiscardStay";
    public const string TimeDiscardYes = "Time.DiscardYes";

    public const string DetailsTitle = "Details.Title";
    public const string DetailsSummary = "Details.Summary";
    public const string DetailsFirstName = "Details.FirstName";
    public const string DetailsLastName = "Details.LastName";
    public const string DetailsPhone = "Details.Phone";
    public const string DetailsPhoneHint = "Details.PhoneHint";
    public const string DetailsEmail = "Details.Email";
    public const string DetailsPrivacy = "Details.Privacy";
    public const string DetailsSendCode = "Details.SendCode";
    public const string DetailsBook = "Details.Book";
    public const string DetailsFirstNameRequired = "Details.FirstNameRequired";
    public const string DetailsLastNameRequired = "Details.LastNameRequired";
    public const string DetailsPhoneRequired = "Details.PhoneRequired";
    public const string DetailsTooManyCodes = "Details.TooManyCodes";
    public const string DetailsTermsTitle = "Details.TermsTitle";
    public const string DetailsFreeUntilOne = "Details.FreeUntilOne";
    public const string DetailsFreeUntilMany = "Details.FreeUntilMany";
    public const string DetailsLateFee = "Details.LateFee";
    public const string DetailsNoShowFee = "Details.NoShowFee";

    public const string CodeTitle = "Code.Title";
    public const string CodeSent = "Code.Sent";
    public const string CodeLabel = "Code.Label";
    public const string CodeSubmit = "Code.Submit";
    public const string CodeInvalid = "Code.Invalid";
    public const string CodeWrongOne = "Code.WrongOne";
    public const string CodeWrongMany = "Code.WrongMany";
    public const string CodeExpired = "Code.Expired";
    public const string CodeTooManyAttempts = "Code.TooManyAttempts";
    public const string CodeNotFound = "Code.NotFound";

    public const string DoneTitle = "Done.Title";
    public const string DoneLead = "Done.Lead";
    public const string DoneService = "Done.Service";
    public const string DoneStaff = "Done.Staff";
    public const string DoneWhen = "Done.When";
    public const string DonePrice = "Done.Price";
    public const string DoneManage = "Done.Manage";
    public const string DoneBookAnother = "Done.BookAnother";
}