using Salon.Core.Localization;
using Salon.Core.Payments;
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

        var known = Resources.Value[SupportedCultures.EnGb];

        expected.Where(key => !known.ContainsKey(key)).ShouldBeEmpty();
    }
}