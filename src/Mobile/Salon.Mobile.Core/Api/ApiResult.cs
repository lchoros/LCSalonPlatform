using System.Diagnostics.CodeAnalysis;
using Salon.Api.Contracts.Booking;

namespace Salon.Mobile.Core.Api;

public enum ApiOutcome
{
    /// <summary>The server answered with what was asked for.</summary>
    Ok,

    /// <summary>The server answered with a refusal (<see cref="ApiProblem"/>), or with something the app could not read.</summary>
    Refused,

    /// <summary>No answer: no network, the host could not be reached, or the call took too long.</summary>
    Offline,
}

/// <summary>
/// What one call to the booking API came to. Never an exception for a refusal or a lost network: the screens decide
/// what to say. Only the caller's own cancellation is thrown, as <see cref="OperationCanceledException"/>.
/// </summary>
public sealed class ApiResult<T>
    where T : class
{
    internal ApiResult(ApiOutcome outcome, T? value, int statusCode, ApiProblem? problem)
    {
        Outcome = outcome;
        Value = value;
        StatusCode = statusCode;
        Problem = problem;
    }

    public ApiOutcome Outcome { get; }

    public T? Value { get; }

    /// <summary>The HTTP status; 0 when <see cref="IsOffline"/>.</summary>
    public int StatusCode { get; }

    public ApiProblem? Problem { get; }

    [MemberNotNullWhen(true, nameof(Value))]
    public bool IsOk => Outcome == ApiOutcome.Ok;

    [MemberNotNullWhen(true, nameof(Problem))]
    public bool IsRefused => Outcome == ApiOutcome.Refused;

    public bool IsOffline => Outcome == ApiOutcome.Offline;

    /// <summary>The refusal's code (<see cref="ApiProblemCodes"/>), or null when the call did not end in one.</summary>
    public string? ProblemCode => Problem?.Code;
}

/// <summary>Makes <see cref="ApiResult{T}"/> values; also what a fake <see cref="IBookingApi"/> in a test answers with.</summary>
public static class ApiResult
{
    public static ApiResult<T> Ok<T>(T value, int statusCode = 200)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        return new ApiResult<T>(ApiOutcome.Ok, value, statusCode, null);
    }

    public static ApiResult<T> Refused<T>(int statusCode, ApiProblem problem)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(problem);
        return new ApiResult<T>(ApiOutcome.Refused, null, statusCode, problem);
    }

    public static ApiResult<T> Offline<T>()
        where T : class
    {
        return new ApiResult<T>(ApiOutcome.Offline, null, 0, null);
    }
}