using Salon.Mobile.Core.Navigation;

namespace Salon.Tests.Mobile.Fakes;

/// <summary>Records where the screens sent the app.</summary>
internal sealed class FakeNavigator : INavigator
{
    public const string Services = "services";
    public const string Time = "time";
    public const string Details = "details";
    public const string Code = "code";
    public const string Done = "done";

    public List<string> Shown { get; } = [];

    public List<Uri> Opened { get; } = [];

    public string? Last => Shown.Count == 0 ? null : Shown[^1];

    public Task ShowServicesAsync()
    {
        Shown.Add(Services);
        return Task.CompletedTask;
    }

    public Task ShowTimeAsync()
    {
        Shown.Add(Time);
        return Task.CompletedTask;
    }

    public Task ShowDetailsAsync()
    {
        Shown.Add(Details);
        return Task.CompletedTask;
    }

    public Task ShowCodeAsync()
    {
        Shown.Add(Code);
        return Task.CompletedTask;
    }

    public Task ShowDoneAsync()
    {
        Shown.Add(Done);
        return Task.CompletedTask;
    }

    public Task OpenInBrowserAsync(Uri address)
    {
        Opened.Add(address);
        return Task.CompletedTask;
    }
}