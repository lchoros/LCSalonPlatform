namespace Salon.Services.Messaging;

/// <summary>The unrendered text of one template for one channel and culture, with its placeholders still in place.</summary>
/// <param name="Subject">Email subject; always null for SMS.</param>
/// <param name="Body">Plain text with <c>{{Token}}</c> placeholders.</param>
/// <param name="IsTenantOverride">True when the text came from the tenant's own <c>message_templates</c> row, false for an embedded default.</param>
public sealed record MessageTemplateText(string? Subject, string Body, bool IsTenantOverride);