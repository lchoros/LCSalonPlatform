namespace Salon.Core.Import;

/// <summary>Why a row cannot be imported as it is, or why it would only repeat a client the salon already has (ADR 0019).</summary>
public enum ImportProblem
{
    /// <summary>The record has a different number of fields than the header.</summary>
    WrongColumnCount = 1,
    MissingFirstName = 2,
    MissingLastName = 3,
    NameTooLong = 4,
    InvalidPhone = 5,
    InvalidEmail = 6,
    /// <summary>A client of the salon already has this normalised phone.</summary>
    DuplicatePhone = 7,
    /// <summary>A client of the salon already has this email.</summary>
    DuplicateEmail = 8,
    /// <summary>No phone or email to match on, and a client with this first and last name already exists.</summary>
    DuplicateName = 9,
    /// <summary>An earlier row of the same file has the same phone, email or (contact-less) name.</summary>
    DuplicateInFile = 10,
}
