using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Salon.Core.Payments;
using Salon.Services.Payments;

namespace Salon.Web.Framework.TagHelpers;

/// <summary>
/// <c>&lt;payment-method-options /&gt;</c> inside a <c>&lt;select&gt;</c> renders one <c>&lt;option&gt;</c> per way
/// the salon takes money (ADR 0060): the built-in methods it has not switched off, then its own. Every "how was it
/// paid" list is this one, so a method the owner adds is offered everywhere at once.
/// </summary>
[HtmlTargetElement("payment-method-options", TagStructure = TagStructure.WithoutEndTag)]
public sealed class PaymentMethodOptionsTagHelper(IPaymentMethodService methods, HtmlEncoder encoder) : TagHelper
{
    /// <summary>
    /// The method to mark as chosen when the salon still offers it: how the money came, for a form that gives it
    /// back, or what was picked on a form shown again. One the salon no longer offers is not listed.
    /// </summary>
    public PaymentChoice? Selected { get; set; }

    /// <summary>Leave out "voucher": a gift voucher is not bought or given back with another one.</summary>
    public bool ForVoucher { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.TagName = null;
        foreach (var option in await methods.OptionsAsync(ForVoucher))
        {
            output.Content.AppendHtml("<option value=\"");
            output.Content.AppendHtml(encoder.Encode(option.Value));
            output.Content.AppendHtml(Selected == option.Choice ? "\" selected=\"selected\">" : "\">");
            output.Content.AppendHtml(encoder.Encode(option.Name));
            output.Content.AppendHtml("</option>");
        }
    }
}