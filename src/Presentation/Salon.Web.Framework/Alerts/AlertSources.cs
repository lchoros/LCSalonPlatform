using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Salon.Data;

namespace Salon.Web.Framework.Alerts;

/// <summary>
/// An error nobody caught (ADR 0093): the developer is told what kind it was and where, then the usual error page is
/// shown — this handler never answers the request itself. The address is the path only: no query, nothing typed.
/// </summary>
public sealed class AlertingExceptionHandler(IDeveloperAlerts alerts) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        var kind = exception.GetType().Name;
        await alerts.RaiseAsync(
            $"A page failed: {kind}",
            $"Request: {httpContext.Request.Method} {httpContext.Request.Path}\nRequest number: {httpContext.TraceIdentifier}\nError type: {exception.GetType().FullName}",
            CancellationToken.None).ConfigureAwait(false);
        return false;
    }
}

/// <summary>
/// A background job that gave up (ADR 0093): Hangfire moves a job to "failed" only after its retries are spent, and
/// that is the moment the developer hears of it — which job, which kind of error, never its arguments.
/// </summary>
public sealed class JobFailureAlertFilter(IDeveloperAlerts alerts) : JobFilterAttribute, IApplyStateFilter
{
    public void OnStateApplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.NewState is not FailedState failed)
        {
            return;
        }

        var job = context.BackgroundJob.Job is { } described ? $"{described.Type.Name}.{described.Method.Name}" : "unknown job";
        alerts.RaiseAsync(
            $"A background job failed: {job}",
            $"Job id: {context.BackgroundJob.Id}\nError type: {failed.Exception.GetType().FullName}",
            CancellationToken.None).GetAwaiter().GetResult();
    }

    public void OnStateUnapplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
    }
}

/// <summary>
/// What /health answers for (ADR 0093): the site is healthy only while it can reach its database. An outside monitor
/// asks every few minutes; a page that still loads from memory with the database gone does not count as up.
/// </summary>
public sealed class DatabaseHealthCheck(IServiceScopeFactory scopes) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var reachable = await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
        return reachable ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("The database cannot be reached.");
    }
}

public static class AlertsServiceCollectionExtensions
{
    public const string HealthPath = "/health";

    /// <summary>The developer's alerts, the handler for errors nobody caught, and the check behind /health.</summary>
    public static IServiceCollection AddSalonAlerts(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<AlertOptions>().BindConfiguration(AlertOptions.SectionName);
        services.AddSingleton<IDeveloperAlerts, DeveloperAlerts>();
        services.AddExceptionHandler<AlertingExceptionHandler>();
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
        return services;
    }
}