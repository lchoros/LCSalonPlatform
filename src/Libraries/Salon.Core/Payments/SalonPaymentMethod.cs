using System.ComponentModel;
using System.Globalization;
using NodaTime;
using Salon.Core.Domain;
using Salon.Core.Settings;
using Salon.Core.Tenancy;

namespace Salon.Core.Payments;

/// <summary>
/// A way of taking money the salon named itself (ADR 0060): PayPal, a second card machine, a cheque. Tenant-owned.
/// It only names how money the salon took itself changed hands; nothing is charged through it. A method that money
/// was ever recorded with is never deleted, only taken out of use, so the lines that carry it keep pointing at it.
/// </summary>
public class SalonPaymentMethod : BaseEntity, ITenantEntity
{
    public const int NameMaxLength = 40;

    public Guid TenantId { get; set; }

    public required string Name { get; set; }

    /// <summary>False once the salon no longer takes money this way: it leaves the lists, its history stays.</summary>
    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    public Instant CreatedAt { get; set; }
}

/// <summary>
/// Which of the built-in methods the salon takes (ADR 0060). Cash is not here: a salon always has a drawer, and the
/// cash-up counts it. Switching one off takes it out of the lists; lines already recorded with it are untouched.
/// </summary>
public sealed class PaymentMethodSettings : ISettings
{
    public bool CardAtSalonTerminal { get; set; } = true;

    public bool BankTransfer { get; set; } = true;

    /// <summary>A paper voucher the system does not track; gift vouchers sold here are used by their code instead (ADR 0057).</summary>
    public bool Voucher { get; set; } = true;
}

/// <summary>The rules of the salon's own methods (ADR 0060). Pure.</summary>
public static class SalonPaymentMethodRules
{
    /// <summary>More than any desk would scroll through.</summary>
    public const int MaxMethods = 20;

    /// <summary>The built-in methods a salon may switch off; cash always stays, and "other" is not one to offer.</summary>
    public static IReadOnlyList<PaymentMethod> Switchable { get; } = [PaymentMethod.CardAtSalonTerminal, PaymentMethod.BankTransfer, PaymentMethod.Voucher];

    /// <summary>The built-in methods, in the order they are offered.</summary>
    public static IReadOnlyList<PaymentMethod> BuiltIn { get; } = [PaymentMethod.Cash, PaymentMethod.CardAtSalonTerminal, PaymentMethod.BankTransfer, PaymentMethod.Voucher];

    /// <summary>The name with the space around it gone and runs of space inside made one; null for nothing.</summary>
    public static string? Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>A name is one to <see cref="SalonPaymentMethod.NameMaxLength"/> characters once normalized.</summary>
    public static bool IsName(string? name) => Normalize(name) is { Length: > 0 and <= SalonPaymentMethod.NameMaxLength };

    /// <summary>Two names are the same method to the desk whatever their capitals: "Paypal" and "PayPal".</summary>
    public static bool SameName(string? one, string? other)
        => string.Equals(Normalize(one), Normalize(other), StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the salon takes a built-in method. Cash always; "other" is never offered by itself.</summary>
    public static bool IsOffered(PaymentMethod method, PaymentMethodSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return method switch
        {
            PaymentMethod.Cash => true,
            PaymentMethod.CardAtSalonTerminal => settings.CardAtSalonTerminal,
            PaymentMethod.BankTransfer => settings.BankTransfer,
            PaymentMethod.Voucher => settings.Voucher,
            _ => false,
        };
    }
}

/// <summary>
/// What a form says about how money changed hands (ADR 0060): one of the built-in methods, or one of the salon's own
/// by its id. A built-in method converts to it by itself, so code that names one reads as before.
/// </summary>
/// <param name="Method">The built-in method, or <see cref="PaymentMethod.Other"/> for one of the salon's own.</param>
/// <param name="CustomMethodId">Which of the salon's own; null for a built-in method.</param>
[TypeConverter(typeof(PaymentChoiceConverter))]
public readonly record struct PaymentChoice(PaymentMethod Method, Guid? CustomMethodId = null)
{
    /// <summary>True for a shape that can be recorded: a built-in method without an id, or "other" with one.</summary>
    public bool IsWellFormed => Method == PaymentMethod.Other
        ? CustomMethodId is not null
        : Enum.IsDefined(Method) && CustomMethodId is null;

    /// <summary>What the form posts: the built-in method's name, or the id of the salon's own.</summary>
    public string Value => CustomMethodId is { } id ? id.ToString() : Method.ToString();

    public static implicit operator PaymentChoice(PaymentMethod method) => FromPaymentMethod(method);

    public static PaymentChoice FromPaymentMethod(PaymentMethod method) => new(method);

    /// <summary>One of the salon's own methods.</summary>
    public static PaymentChoice Own(Guid customMethodId) => new(PaymentMethod.Other, customMethodId);

    /// <summary>
    /// Reads what a form posted. A built-in method by its name (not by its number, and never "Other" by itself), one
    /// of the salon's own by its id. Whether the salon has such a method is not decided here.
    /// </summary>
    public static bool TryParse(string? text, out PaymentChoice choice)
    {
        choice = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        if (Guid.TryParse(value, out var id))
        {
            choice = Own(id);
            return true;
        }

        // The exact name of one built-in method. Enum parsing would also take a number or "Cash,Voucher".
        foreach (var method in SalonPaymentMethodRules.BuiltIn)
        {
            if (string.Equals(method.ToString(), value, StringComparison.Ordinal))
            {
                choice = new PaymentChoice(method);
                return true;
            }
        }

        return false;
    }

    public override string ToString() => Value;
}

/// <summary>Lets a form field bind straight to a <see cref="PaymentChoice"/>.</summary>
public sealed class PaymentChoiceConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string text)
        {
            return PaymentChoice.TryParse(text, out var choice)
                ? choice
                : throw new FormatException("Not a payment method.");
        }

        return base.ConvertFrom(context, culture, value);
    }

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
        => destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        => destinationType == typeof(string) && value is PaymentChoice choice
            ? choice.Value
            : base.ConvertTo(context, culture, value, destinationType);
}