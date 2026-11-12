using Salon.Core.Clients;

namespace Salon.Services.Clients;

/// <summary>A merge the rules refuse (ADR 0017): the same card on both sides, or a card already merged away. The caller shows the reason.</summary>
public sealed class ClientMergeRefusedException(MergeRefusal reason)
    : InvalidOperationException($"The merge was refused: {reason}.")
{
    public MergeRefusal Reason { get; } = reason;
}