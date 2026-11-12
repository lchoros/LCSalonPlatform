namespace Salon.Tests.Unit.Import;

/// <summary>The sample exports copied next to the test assembly (see the csproj).</summary>
internal static class ImportFixtures
{
    public const string Booksy = "booksy-customers.csv";
    public const string VersumCp1250 = "versum-klienci-cp1250.csv";
    public const string Broken = "broken-clients.csv";

    public static byte[] Read(string name)
        => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Fixtures", name));
}
