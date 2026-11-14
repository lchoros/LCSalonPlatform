using FluentValidation;
using Salon.Core.Cqrs;
using Salon.Web.Framework.Pipeline;

namespace Salon.Tests.Unit.Pipeline;

public class ValidationBehaviorTests
{
    private static readonly RequestHandlerDelegate<string> Handler = () => Task.FromResult("handled");

    [Fact]
    public async Task Invalid_request_throws_with_every_failure_listed()
    {
        var behavior = new ValidationBehavior<Ping, string>([new PingValidator(), new ForbiddenWordValidator()]);
        var handled = false;

        var ex = await Should.ThrowAsync<ValidationException>(
            () => behavior.HandleAsync(new Ping("hello world!"), () => { handled = true; return Handler(); }, CancellationToken.None));

        handled.ShouldBeFalse();
        ex.Errors.Count().ShouldBe(2);
        ex.Errors.Select(e => e.ErrorMessage).ShouldContain(m => m.Contains("10", StringComparison.Ordinal));
        ex.Errors.Select(e => e.ErrorMessage).ShouldContain("No greetings please");
    }

    [Fact]
    public async Task Valid_request_reaches_the_handler()
    {
        var behavior = new ValidationBehavior<Ping, string>([new PingValidator()]);

        var result = await behavior.HandleAsync(new Ping("fine"), Handler, CancellationToken.None);

        result.ShouldBe("handled");
    }

    [Fact]
    public async Task No_validators_means_pass_through()
    {
        var behavior = new ValidationBehavior<Ping, string>([]);

        (await behavior.HandleAsync(new Ping(""), Handler, CancellationToken.None)).ShouldBe("handled");
    }

    private sealed class ForbiddenWordValidator : AbstractValidator<Ping>
    {
        public ForbiddenWordValidator()
            => RuleFor(p => p.Message).Must(m => !m.Contains("hello", StringComparison.Ordinal)).WithMessage("No greetings please");
    }
}