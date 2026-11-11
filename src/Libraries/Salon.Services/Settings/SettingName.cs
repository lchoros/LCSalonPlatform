using System.Text.RegularExpressions;

namespace Salon.Services.Settings;

/// <summary>
/// Guards the raw setting API. A name is "SettingsClass.Property" (letters, digits, dots), at most 200 characters, and a
/// value at most 4000 characters, so an admin form can never turn <c>SetRawAsync</c> into an unbounded, arbitrary-key write.
/// </summary>
public static partial class SettingName
{
    public const int MaxNameLength = 200;

    public const int MaxValueLength = 4000;

    public static void Validate(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > MaxNameLength || !NamePattern().IsMatch(name))
        {
            throw new ArgumentException(
                $"'{name}' is not a valid setting name. Use 'SettingsClass.Property' with letters and digits only, at most {MaxNameLength} characters.",
                nameof(name));
        }
    }

    public static void ValidateValue(string? value, string name)
    {
        if (value is { Length: > MaxValueLength })
        {
            throw new ArgumentException($"Setting '{name}' exceeds {MaxValueLength} characters.", nameof(value));
        }
    }

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9]*(\.[A-Za-z][A-Za-z0-9]*)+$")]
    private static partial Regex NamePattern();
}