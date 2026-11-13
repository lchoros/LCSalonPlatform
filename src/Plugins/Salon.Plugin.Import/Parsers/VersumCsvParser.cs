namespace Salon.Plugin.Import.Parsers;

/// <summary>
/// Versum client export (ADR 0019 lists the assumed columns): Polish headings, usually semicolon-separated and saved as
/// Windows-1250 by Excel. Birth date, gender, source, visit and balance columns identify the product and are ignored.
/// </summary>
public sealed class VersumCsvParser : ClientCsvParserBase
{
    public const string FormatName = "Versum";

    private static readonly HashSet<string> FirstName = HeaderKey.Set("Imię", "First name", "Firstname");
    private static readonly HashSet<string> LastName = HeaderKey.Set("Nazwisko", "Last name", "Surname");
    private static readonly HashSet<string> FullName = HeaderKey.Set("Imię i nazwisko", "Klient", "Nazwa", "Name", "Full name");
    private static readonly HashSet<string> Phone = HeaderKey.Set("Telefon", "Telefon komórkowy", "Nr telefonu", "Numer telefonu", "Telefon 1", "Tel.", "Komórka", "Phone", "Mobile", "Phone number");
    private static readonly HashSet<string> Email = HeaderKey.Set("E-mail", "Email", "Adres e-mail", "Adres email", "E-mail address");
    private static readonly HashSet<string> Notes = HeaderKey.Set("Opis", "Uwagi", "Komentarz", "Notatki", "Notatka", "Description", "Notes", "Comments");
    private static readonly HashSet<string> Consent = HeaderKey.Set("Zgoda marketingowa", "Zgoda na marketing", "Zgoda SMS", "Zgoda e-mail", "Zgoda email", "Zgody marketingowe", "Marketing consent", "Marketing", "Marketing SMS", "Marketing e-mail");
    private static readonly HashSet<string> Signature = HeaderKey.Set("Data urodzenia", "Płeć", "Źródło", "Ostatnia wizyta", "Liczba wizyt", "Tagi", "Saldo", "Data dodania", "Miasto", "Kod pocztowy", "Adres");

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