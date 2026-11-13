namespace Salon.Services.Localization;

/// <summary>
/// One embedded JSON resource as <see cref="EmbeddedJsonResourceSource.ReadFiles"/> saw it: the manifest name, the culture
/// it is for, the module (the file name under the culture folder, e.g. <c>Catalog</c>; <see langword="null"/> for a legacy
/// flat <c>{culture}.json</c>) and its parsed entries. Used by the architecture tests to check file-level parity.
/// </summary>
/// <param name="ResourceName">Manifest resource name, e.g. <c>Salon.Web.Localization.en-GB.Catalog.json</c>.</param>
/// <param name="Culture">Culture as spelled in the resource name, e.g. <c>en-GB</c>.</param>
/// <param name="Module">Module file name, or <see langword="null"/> for a flat per-culture file.</param>
/// <param name="Entries">Resource key → text, in file order.</param>
public sealed record LocalizationResourceFile(
    string ResourceName,
    string Culture,
    string? Module,
    IReadOnlyDictionary<string, string> Entries);