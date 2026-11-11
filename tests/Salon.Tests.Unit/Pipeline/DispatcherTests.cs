using Microsoft.Extensions.DependencyInjection;

using Salon.Core.Cqrs;
using Salon.Web.Framework.Pipeline;

namespace Salon.Tests.Unit.Pipeline;

public class DispatcherTests
{
    private static IDispatcher BuildDispatcher(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<IDispatcher, Dispatcher>();
        services.AddScoped<IRequestHandler<Ping, string>, PingHandler>();
        services.AddScoped<IRequestHandler<CountThings, int>, CountThingsHandler>();
        configure?.Invoke(services);
        return services.BuildServiceProvider(validateScopes: true).CreateScope().ServiceProvider.GetRequiredService<IDispatcher>();
    }

    [Fact]
    public async Task Resolves_the_handler_and_returns_its_result()
    {
        var dispatcher = BuildDispatcher();

        var result = await dispatcher.SendAsync(new Ping("hi"), CancellationToken.None);

        result.ShouldBe("pong: hi");
    }

    [Fact]
    public async Task Works_for_queries_too()
    {
        var dispatcher = BuildDispatcher();

        (await dispatcher.SendAsync(new CountThings(), CancellationToken.None)).ShouldBe(42);
    }

    [Fact]
    public async Task Missing_handler_is_a_clear_error()
    {
        var dispatcher = BuildDispatcher();

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => dispatcher.SendAsync(new Orphan(), CancellationToken.None));

        ex.Message.ShouldContain(nameof(Orphan));
        ex.Message.ShouldContain("IRequestHandler");
    }

    [Fact]
    public async Task Null_request_is_rejected()
    {
        var dispatcher = BuildDispatcher();

        await Should.ThrowAsync<ArgumentNullException>(() => dispatcher.SendAsync<string>(null!, CancellationToken.None));
    }

    [Fact]
    public async Task Behaviours_run_in_registration_order_around_the_handler()
    {
        var log = new List<string>();
        var dispatcher = BuildDispatcher(services =>
        {
            services.AddSingleton(log);
            services.AddScoped(typeof(IPipelineBehavior<,>), typeof(RecordingBehaviorA<,>));
            services.AddScoped(typeof(IPipelineBehavior<,>), typeof(RecordingBehaviorB<,>));
        });

        var result = await dispatcher.SendAsync(new Ping("x"), CancellationToken.None);

        result.ShouldBe("pong: x");
        log.ShouldBe(["A:before", "B:before", "B:after", "A:after"]);
    }

    private sealed class RecordingBehaviorA<TRequest, TResult>(List<string> log) : IPipelineBehavior<TRequest, TResult>
        where TRequest : IRequest<TResult>
    {
        public async Task<TResult> HandleAsync(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
        {
            log.Add("A:before");
            var result = await next();
            log.Add("A:after");
            return result;
        }
    }

    private sealed class RecordingBehaviorB<TRequest, TResult>(List<string> log) : IPipelineBehavior<TRequest, TResult>
        where TRequest : IRequest<TResult>
    {
        public async Task<TResult> HandleAsync(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
        {
            log.Add("B:before");
            var result = await next();
            log.Add("B:after");
            return result;
        }
    }
}