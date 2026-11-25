using Salon.Mobile.Core.Api;
using Salon.Mobile.Core.Localization;

namespace Salon.Mobile.Core.Screens;

/// <summary>What a screen says when a call did not come back with its answer.</summary>
internal static class ScreenText
{
    /// <summary>Offline in the app's words; a refusal in the server's sentence when it sent one, the app's otherwise.</summary>
    public static string Failure<T>(IAppText text, ApiResult<T> result)
        where T : class
    {
        if (result.IsOffline)
        {
            return text[TextKeys.Offline];
        }

        return string.IsNullOrWhiteSpace(result.Problem?.Message) ? text[TextKeys.Unexpected] : result.Problem.Message;
    }
}