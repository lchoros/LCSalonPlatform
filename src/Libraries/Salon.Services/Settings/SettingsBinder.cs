using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Settings;

namespace Salon.Services.Settings;

/// <summary>
/// Converts between a settings object and its "{Type}.{Property}" → text rows. Text is always culture-invariant:
/// decimals use '.', booleans are "True"/"False", dates and times are ISO-8601, enums are stored by name.
/// </summary>
internal static class SettingsBinder
{
    private static readonly LocalTimePattern LocalTimeFormat = LocalTimePattern.ExtendedIso;
    private static readonly DurationPattern DurationFormat = DurationPattern.Roundtrip;

    public static string Prefix(Type settingsType)
    {
        ArgumentNullException.ThrowIfNull(settingsType);
        return settingsType.Name + ".";
    }

    public static TSettings Bind<TSettings>(IReadOnlyDictionary<string, string?> rows)
        where TSettings : ISettings, new()
    {
        ArgumentNullException.ThrowIfNull(rows);

        var settings = new TSettings();
        var prefix = Prefix(typeof(TSettings));
        foreach (var property in BindableProperties(typeof(TSettings)))
        {
            if (!rows.TryGetValue(prefix + property.Name, out var raw))
            {
                continue;
            }

            if (raw is null && !IsNullable(property.PropertyType))
            {
                continue;
            }

            property.SetValue(settings, Deserialize(raw, property.PropertyType));
        }

        return settings;
    }

    public static IEnumerable<KeyValuePair<string, string?>> Flatten<TSettings>(TSettings settings)
        where TSettings : ISettings
    {
        ArgumentNullException.ThrowIfNull(settings);

        var prefix = Prefix(typeof(TSettings));
        return BindableProperties(typeof(TSettings))
            .Select(p => KeyValuePair.Create(prefix + p.Name, Serialize(p.GetValue(settings))));
    }

    public static string? Serialize(object? value) => value switch
    {
        null => null,
        string text => text,
        bool flag => flag.ToString(CultureInfo.InvariantCulture),
        Enum member => Enum.Format(member.GetType(), member, "G"),
        DateOnly date => date.ToString("O", CultureInfo.InvariantCulture),
        TimeOnly time => time.ToString("O", CultureInfo.InvariantCulture),
        LocalTime localTime => LocalTimeFormat.Format(localTime),
        Duration duration => DurationFormat.Format(duration),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };

    public static object? Deserialize(string? value, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (value is null)
        {
            return IsNullable(type) ? null : throw new FormatException($"A null setting cannot be bound to {type.Name}.");
        }

        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (target == typeof(string))
        {
            return value;
        }

        if (target == typeof(LocalTime))
        {
            return LocalTimeFormat.Parse(value).GetValueOrThrow();
        }

        if (target == typeof(Duration))
        {
            return DurationFormat.Parse(value).GetValueOrThrow();
        }

        if (target.IsEnum)
        {
            return Enum.Parse(target, value, ignoreCase: true);
        }

        // Handles the primitives, decimal, Guid, DateOnly and TimeOnly with the invariant culture.
        return TypeDescriptor.GetConverter(target).ConvertFromInvariantString(value);
    }

    private static bool IsNullable(Type type) => !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;

    private static IEnumerable<PropertyInfo> BindableProperties(Type type)
        => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.SetMethod!.IsPublic && p.GetIndexParameters().Length == 0);
}