namespace Salon.Core.Import;

/// <summary>Hard caps on one import file (ADR 0019). Both the upload endpoint and the parser enforce them.</summary>
public static class ClientImportLimits
{
    public const int MaxRows = 5_000;

    public const long MaxBytes = 2 * 1024 * 1024;
}
