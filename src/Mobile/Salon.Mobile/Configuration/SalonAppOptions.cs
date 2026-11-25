using System.Reflection;
using System.Text.Json;

namespace Salon.Mobile.Configuration;

/// <summary>What the app is told when it is built (ADR 0097): which salon's host it books with. No secrets live here.</summary>
public sealed class SalonAppOptions
{
    /// <summary>The salon's host; the booking API is at <c>{BaseAddress}/api/v1/booking</c>.</summary>
    public required Uri BaseAddress { get; init; }

    private const string Settings = "Salon.Mobile.appsettings.json";
    private const string LocalSettings = "Salon.Mobile.appsettings.Local.json";

    /// <summary>
    /// Reads the embedded appsettings.json, then, in Debug builds where one was present at build time, the git-ignored
    /// appsettings.Local.json over it. Plain System.Text.Json: no configuration package for one value.
    /// </summary>
    public static SalonAppOptions Load(Assembly assembly)
    {
        var baseAddress = Read(assembly, Settings) ?? throw new InvalidOperationException($"{Settings} has no BaseAddress.");
#if DEBUG
        baseAddress = Read(assembly, LocalSettings) ?? baseAddress;
#endif
        if (!Uri.TryCreate(baseAddress, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException($"BaseAddress \"{baseAddress}\" is not an absolute http(s) address.");
        }

        return new SalonAppOptions { BaseAddress = ForThisDevice(uri) };
    }

    private static string? Read(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name);
        if (stream is null)
        {
            return null;
        }

        using var json = JsonDocument.Parse(stream, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return json.RootElement.TryGetProperty(nameof(BaseAddress), out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    /// <summary>The Android emulator reaches the desk it runs on as 10.0.2.2, not localhost.</summary>
    private static Uri ForThisDevice(Uri uri)
    {
#if DEBUG && ANDROID
        if (uri.IsLoopback)
        {
            return new UriBuilder(uri) { Host = "10.0.2.2" }.Uri;
        }
#endif
        return uri;
    }
}