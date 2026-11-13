namespace Salon.Plugin.Import.Parsers;

/// <summary>
/// Booksy Biz customer export / import template (ADR 0019 lists the assumed columns): English or Polish headings,
/// comma-separated, UTF-8 with a BOM. Birthday, gender, address and loyalty columns identify the product and are ignored.
/// </summary>
public sealed class BooksyCsvParser : ClientCsvParserBase
{
    public const string FormatName = "Booksy";

    private static readonly HashSet<string> FirstName = HeaderKey.Set("First Name", "Firstname", "Given name", "Imię");
    private static readonly HashSet<string> LastName = HeaderKey.Set("Last Name", "Lastname", "Surname", "Family name", "Nazwisko");
    private static readonly HashSet<string> FullName = HeaderKey.Set("Name", "Full name", "Customer", "Customer name", "Client", "Client name", "Imię i nazwisko", "Klient", "Nazwa");
    private static readonly HashSet<string> Phone = HeaderKey.Set("Phone", "Phone Number", "Mobile", "Mobile Number", "Cell Phone", "Cell", "Telephone", "Numer telefonu", "Telefon", "Nr telefonu");
    private static readonly HashSet<string> Email = HeaderKey.Set("Email", "E-mail", "Email Address", "E-mail Address", "Adres e-mail", "Adres email");
    private static readonly HashSet<string> Notes = HeaderKey.Set("Notes", "Note", "Customer notes", "Client notes", "Comments", "Notatki", "Notatka", "Uwagi");
    private static readonly HashSet<string> Consent = HeaderKey.Set("Marketing Consent", "Marketing", "Accepts Marketing", "Email Marketing", "SMS Marketing", "Marketing opt-in", "Zgoda marketingowa", "Zgody marketingowe");
    private static readonly HashSet<string> Signature = HeaderKey.Set("Birthday", "Date of birth", "Gender", "Address", "City", "Zip Code", "Zip", "Postcode", "State", "Loyalty points", "Total visits", "Last visit", "Customer since");

    public override string Format => FormatName;

    protected override HashSet<string> FirstNameAliases => FirstName;

    protected override HashSet<string> LastNameAliases => LastName;

    protected override HashSet<string> FullNameAliases => FullName;

    protected override HashSet<string> PhoneAliases => Phone;

    protected override HashSet<string> EmailAliases => Email;

    protected override HashSet<string> NotesAliases => Notes;

    protected override HashSet<string> ConsentAliases => Consent;

    protected override HashSet<string> SignatureAliases => Signature;
}