namespace Salon.Core.Cqrs;

/// <summary>A use-case message with a result. Slices implement <see cref="ICommand{TResult}"/> or <see cref="IQuery{TResult}"/>.</summary>
/// <typeparam name="TResult">What the handler returns.</typeparam>
public interface IRequest<out TResult>;

/// <summary>Changes state. Runs inside a database transaction and requires a tenant unless it is an <see cref="IPlatformRequest"/>.</summary>
public interface ICommand<out TResult> : IRequest<TResult>;

/// <summary>Reads state. No transaction is opened; still requires a tenant unless it is an <see cref="IPlatformRequest"/>.</summary>
public interface IQuery<out TResult> : IRequest<TResult>;

/// <summary>
/// Marks a request that may run with no tenant in scope: sign-up, platform admin, health checks.
/// Everything else is refused by the tenant guard when <c>ITenantContext.TenantId</c> is null.
/// </summary>
public interface IPlatformRequest;