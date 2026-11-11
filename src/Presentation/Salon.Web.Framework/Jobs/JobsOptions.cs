namespace Salon.Web.Framework.Jobs;

/// <summary>Host-level background job settings (section "Jobs").</summary>
public sealed class JobsOptions
{
    public const string SectionName = "Jobs";

    /// <summary>Start the Hangfire server in this process. Tests turn it off; storage and recurring jobs stay registered.</summary>
    public bool Enabled { get; set; } = true;

    public int WorkerCount { get; set; } = 2;

    public string SchemaName { get; set; } = "hangfire";
}