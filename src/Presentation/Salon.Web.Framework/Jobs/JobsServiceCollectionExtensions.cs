using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Salon.Core.Tasks;
using Salon.Services.Tasks;

namespace Salon.Web.Framework.Jobs;

public static class JobsServiceCollectionExtensions
{
    /// <summary>Hangfire on the same Postgres database (own schema), server optional per JobsOptions.Enabled.</summary>
    public static IServiceCollection AddSalonJobs(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration.GetSection(JobsOptions.SectionName).Get<JobsOptions>() ?? new JobsOptions();
        services.AddOptions<JobsOptions>().BindConfiguration(JobsOptions.SectionName);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(
                storage => storage.UseNpgsqlConnection(connectionString),
                new PostgreSqlStorageOptions { SchemaName = options.SchemaName, PrepareSchemaIfNecessary = true }));

        if (options.Enabled)
        {
            services.AddHangfireServer(server => server.WorkerCount = options.WorkerCount);
        }

        return services;
    }

    /// <summary>Registers one recurring Hangfire job per IScheduleTask (job id = SystemName). Idempotent on every start.</summary>
    public static IApplicationBuilder UseSalonScheduleTasks(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var manager = app.ApplicationServices.GetRequiredService<IRecurringJobManager>();
        using var scope = app.ApplicationServices.CreateScope();
        foreach (var task in scope.ServiceProvider.GetServices<IScheduleTask>())
        {
            var systemName = task.SystemName;
            manager.AddOrUpdate<ScheduleTaskRunner>(
                systemName,
                runner => runner.RunAsync(systemName, CancellationToken.None),
                task.CronExpression,
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
        }

        return app;
    }
}