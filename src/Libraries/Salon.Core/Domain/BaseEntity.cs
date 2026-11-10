namespace Salon.Core.Domain;

/// <summary>Root of every persisted entity. Ids are UUIDv7 so they sort by creation time in Postgres.</summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
}