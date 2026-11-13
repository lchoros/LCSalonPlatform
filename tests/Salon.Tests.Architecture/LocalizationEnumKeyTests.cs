using Salon.Core.Localization;
using Salon.Core.Payments;
using Salon.Core.Reports;
using Salon.Services.Payments;

namespace Salon.Tests.Architecture;

/// <summary>
/// Keys a view or controller builds from an enum member (<c>T[$"Payments.Method.{method}"]</c>) are invisible to the
/// literal-key scan in <see cref="LocalizationResourceTests"/>, so a new member would ship without its text. Each
/// member is listed here from the enum itself; the parity test then guarantees the Polish value.
/// </summary>
public partial class LocalizationResourceTests
{
    [Fact]
    public void Every_enum_member_the_fee_pages_turn_into_a_key_has_its_text()
    {
        var expected = new List<string>();
        expected.AddRange(Enum.GetNames<PaymentMethod>().Select(name => $"Payments.Method.{name}"));
        expected.AddRange(Enum.GetNames<FeeStatus>().Select(name => $"Payments.Fee.Status.{name}"));
        expected.AddRange(Enum.GetValues<ClientAccountEntryKind>()
            .Where(ClientAccountRules.IsFee)
            .SelectMany(kind => new[] { $"Payments.Fee.Kind.{kind}", $"Payments.Fee.Proposal.{kind}" }));
        // NotFound is a 404, not a message.
        expected.AddRange(Enum.GetValues<ClientAccountRefusal>()
            .Where(refusal => refusal != ClientAccountRefusal.NotFound)
            .Select(refusal => $"Payments.Fee.Refused.{refusal}"));

        // The till names each kind of line that moved money and each reason a day was not closed.
        expected.AddRange(Enum.GetValues<ClientAccountEntryKind>()
            .Where(kind => TillRules.Direction(kind) != 0)
            .Select(kind => $"Payments.Kind.{kind}"));
        expected.AddRange(Enum.GetValues<CashUpOutcome>()
            .Where(outcome => outcome != CashUpOutcome.Closed)
            .Select(outcome => $"Payments.CashUp.Refused.{outcome}"));

        var known = Resources.Value[SupportedCultures.EnGb];

        expected.Where(key => !known.ContainsKey(key)).ShouldBeEmpty();
    }

    [Fact]
    public void Every_one_tap_report_period_has_its_name()
    {
        var known = Resources.Value[SupportedCultures.EnGb];

        Enum.GetNames<ReportPreset>().Select(name => $"Reports.Preset.{name}").Where(key => !known.ContainsKey(key)).ShouldBeEmpty();
    }
}