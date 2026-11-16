using System.Security.Cryptography;
using NodaTime;
using Salon.Core.Domain;
using Salon.Core.Tenancy;

namespace Salon.Core.Payments;

/// <summary>
/// A gift voucher the salon sold (ADR 0057): money taken today for a visit somebody will have later. Tenant-owned.
/// The money of the sale is one line of the ledger (<see cref="SaleEntryId"/>); every use of the voucher is a line
/// that points at that sale, on the account of the client who used it. <see cref="Balance"/> is what is left: it only
/// goes down, by a use, or to zero when the salon gives the money back for a voucher nobody used.
/// A voucher is found by its <see cref="Code"/>; whoever holds the code holds the voucher, like a paper one.
/// </summary>
public class GiftVoucher : BaseEntity, ITenantEntity
{
    public const int CodeLength = 10;
    public const int NoteMaxLength = 200;
    public const int CurrencyLength = 3;

    public Guid TenantId { get; set; }

    /// <summary>Ten characters without the dash, upper case, unique in the salon.</summary>
    public required string Code { get; set; }

    /// <summary>What it was sold for.</summary>
    public decimal InitialAmount { get; set; }

    /// <summary>What is left on it; never below zero, never above <see cref="InitialAmount"/>.</summary>
    public decimal Balance { get; set; }

    /// <summary>ISO 4217 code of both amounts; the salon's currency.</summary>
    public required string Currency { get; set; }

    public Instant SoldAt { get; set; }

    /// <summary>The login that sold it.</summary>
    public Guid? SoldByUserId { get; set; }

    /// <summary>The line of money that is the sale.</summary>
    public Guid SaleEntryId { get; set; }

    /// <summary>The last day it can be used; null never expires.</summary>
    public LocalDate? ExpiresOn { get; set; }

    /// <summary>Who it is for, or anything else the desk wants to remember; optional.</summary>
    public string? Note { get; set; }

    /// <summary>When the salon gave the money back for it; null for a voucher that stands.</summary>
    public Instant? RefundedAt { get; set; }

    public Money Initial => new(InitialAmount, Currency);

    public Money Left => new(Balance, Currency);
}

public enum GiftVoucherStatus
{
    /// <summary>Has money on it and can be used.</summary>
    Active = 1,

    /// <summary>Nothing is left on it.</summary>
    Used = 2,

    /// <summary>Its last day has passed with money still on it.</summary>
    Expired = 3,

    /// <summary>The salon gave the money back.</summary>
    Refunded = 4,
}

/// <summary>What a gift voucher may be and do (ADR 0057). Pure, except for drawing a random code.</summary>
public static class GiftVoucherRules
{
    /// <summary>More than any salon sells a voucher for; keeps a slip of the finger out of the books.</summary>
    public const decimal MaxAmount = 5_000m;

    /// <summary>How long a voucher is good for unless the desk says otherwise.</summary>
    public const int DefaultValidMonths = 12;

    /// <summary>Letters and digits nobody mixes up when reading a code aloud: no 0 or O, no 1, I or L.</summary>
    public const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    /// <summary>The ways money changes hands for a voucher: every method but "voucher", which is not how one is bought or given back.</summary>
    public static IReadOnlyList<PaymentMethod> PaymentMethods { get; } = [.. Enum.GetValues<PaymentMethod>().Where(m => m != PaymentMethod.Voucher)];

    /// <summary>An amount above zero, with at most two decimals, up to <see cref="MaxAmount"/>.</summary>
    public static bool IsAmount(decimal amount) => amount > 0m && amount <= MaxAmount && decimal.Round(amount, 2) == amount;

    public static bool NoteFits(string? note) => note is null || note.Trim().Length <= GiftVoucher.NoteMaxLength;

    /// <summary>A fresh code. Random from the system's secure source: a code is as good as the money on it.</summary>
    public static string NewCode()
        => string.Create(GiftVoucher.CodeLength, 0, static (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
            }
        });

    /// <summary>
    /// A code as somebody typed it, made comparable: spaces and dashes dropped, upper case. Null when it cannot be a
    /// code: the wrong length, or a character the alphabet does not have.
    /// </summary>
    public static string? Normalize(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed))
        {
            return null;
        }

        var code = new string([.. typed.Where(c => c is not (' ' or '-' or '‑' or '–')).Select(char.ToUpperInvariant)]);
        return code.Length == GiftVoucher.CodeLength && code.All(c => Alphabet.Contains(c, StringComparison.Ordinal)) ? code : null;
    }

    /// <summary>The code as it is printed and read aloud: two groups of five.</summary>
    public static string Format(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return code.Length == GiftVoucher.CodeLength ? string.Concat(code.AsSpan(0, 5), "-", code.AsSpan(5)) : code;
    }

    /// <summary>The last day of a voucher sold on <paramref name="soldOn"/> that is good for so many months; null for zero months.</summary>
    public static LocalDate? ExpiresOn(LocalDate soldOn, int validMonths) => validMonths > 0 ? soldOn.PlusMonths(validMonths) : null;

    public static bool IsExpired(LocalDate? expiresOn, LocalDate today) => expiresOn is { } last && today > last;

    public static GiftVoucherStatus Status(decimal balance, LocalDate? expiresOn, Instant? refundedAt, LocalDate today)
        => refundedAt is not null ? GiftVoucherStatus.Refunded
            : balance <= 0m ? GiftVoucherStatus.Used
            : IsExpired(expiresOn, today) ? GiftVoucherStatus.Expired
            : GiftVoucherStatus.Active;

    /// <summary>How much of a voucher goes onto a bill: what is left to pay, or all the voucher has if that is less.</summary>
    public static decimal Applies(decimal balance, decimal outstanding) => Math.Max(0m, Math.Min(balance, outstanding));

    /// <summary>The money goes back only for a voucher nobody has used and that was not given back already.</summary>
    public static bool CanRefund(decimal initialAmount, decimal balance, Instant? refundedAt) => refundedAt is null && balance == initialAmount && balance > 0m;
}