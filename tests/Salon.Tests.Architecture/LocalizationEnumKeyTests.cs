using Salon.Core.Localization;
using Salon.Core.Messaging;
using Salon.Core.Payments;
using Salon.Core.Products;
using Salon.Core.Reports;
using Salon.Services.Appointments;
using Salon.Services.Localization;
using Salon.Services.Payments;
using Salon.Services.Products;

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
    public void Every_message_channel_and_placeholder_the_messages_page_names_has_its_text()
    {
        var expected = new List<string>();
        expected.AddRange(MessageTemplateCatalog.All.SelectMany(d => new[] { $"Settings.Messages.Name.{d.Key}", $"Settings.Messages.When.{d.Key}" }));
        expected.AddRange(MessageTemplateCatalog.All.SelectMany(d => d.TokenNames).Distinct().Select(token => $"Settings.Messages.Token.{token}"));
        expected.AddRange(Enum.GetNames<MessageChannel>().Select(channel => $"Settings.Messages.Channel.{channel}"));

        var known = Resources.Value[SupportedCultures.EnGb];

        expected.Where(key => !known.ContainsKey(key)).ShouldBeEmpty();
    }

    [Fact]
    public void Every_kind_of_stock_change_and_every_refusal_has_its_text()
    {
        // The product page names each kind, confirms each with a counted text (ADR 0035) and explains each refusal (ADR 0036).
        var expected = new List<string>();
        expected.AddRange(Enum.GetNames<StockMovementKind>().Select(kind => $"Products.Kind.{kind}"));
        // A sale and a return are recorded on a bill, not from the product page (ADR 0037, ADR 0040), so they have a name and no confirmation here.
        expected.AddRange(Enum.GetValues<StockMovementKind>()
            .Where(kind => kind is not (StockMovementKind.Sold or StockMovementKind.Returned))
            .SelectMany(kind => new[]
            {
                $"Products.Stock.Recorded.{kind}{LocalizationPlurals.OneSuffix}",
                $"Products.Stock.Recorded.{kind}{LocalizationPlurals.ManySuffix}",
            }));
        expected.Add("Products.Kind.CountedAtSale");
        // A recorded change and a matching count are confirmations; an unknown product is a 404, not a message.
        expected.AddRange(Enum.GetValues<StockOutcome>()
            .Where(outcome => outcome is not (StockOutcome.Recorded or StockOutcome.CountMatches or StockOutcome.ProductNotFound))
            .Select(outcome => $"Products.Stock.Refused.{outcome}"));

        var known = Resources.Value[SupportedCultures.EnGb];

        expected.Where(key => !known.ContainsKey(key)).ShouldBeEmpty();
    }

    [Fact]
    public void Every_answer_of_the_waitlist_form_has_its_text()
    {
        // ADR 0041: a saved entry is confirmed by its outcome, a refused one explained; an unknown client is a 404.
        var expected = Enum.GetValues<WaitlistAddOutcome>()
            .Where(outcome => outcome != WaitlistAddOutcome.ClientUnknown)
            .Select(outcome => outcome is WaitlistAddOutcome.Added or WaitlistAddOutcome.Updated
                ? $"Appointments.Waitlist.{outcome}"
                : $"Appointments.Waitlist.Refused.{outcome}");

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