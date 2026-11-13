using FluentValidation;
using Salon.Core.Cqrs;

namespace Salon.Tests.Unit.Pipeline;

/// <summary>Sample slice used to exercise real DI registration through AddSalonPipeline(typeof(Ping).Assembly).</summary>
public sealed record Ping(string Message) : ICommand<string>;

public sealed class PingHandler : IRequestHandler<Ping, string>
{
    public Task<string> HandleAsync(Ping request, CancellationToken cancellationToken)
        => Task.FromResult("pong: " + request.Message);
}

public sealed class PingValidator : AbstractValidator<Ping>
{
    public PingValidator()
    {
        RuleFor(p => p.Message).NotEmpty();
        RuleFor(p => p.Message).MaximumLength(10);
    }
}

public sealed record CountThings : IQuery<int>;

public sealed class CountThingsHandler : IRequestHandler<CountThings, int>
{
    public Task<int> HandleAsync(CountThings request, CancellationToken cancellationToken) => Task.FromResult(42);
}

/// <summary>Has no handler on purpose.</summary>
public sealed record Orphan : IQuery<int>;

/// <summary>A command the platform may run with no tenant in scope.</summary>
public sealed record PlatformPing : ICommand<string>, IPlatformRequest;

public sealed class PlatformPingHandler : IRequestHandler<PlatformPing, string>
{
    public Task<string> HandleAsync(PlatformPing request, CancellationToken cancellationToken) => Task.FromResult("platform");
}