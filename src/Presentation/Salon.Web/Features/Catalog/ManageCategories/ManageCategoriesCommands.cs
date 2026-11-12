using Salon.Core.Cqrs;

namespace Salon.Web.Features.Catalog.ManageCategories;

/// <summary>Adds a category at the end of the salon's list and returns its id. <see cref="AddCategoryValidator"/> holds the rules; the handler trims the name.</summary>
public sealed record AddCategoryCommand(string Name) : ICommand<Guid>;

/// <summary>Renames one category; false when the salon has no such category. <see cref="RenameCategoryValidator"/> holds the rules.</summary>
public sealed record RenameCategoryCommand(Guid CategoryId, string Name) : ICommand<bool>;

/// <summary>
/// Deactivates or restores one category; false when the salon has no such category. An inactive category leaves the
/// pickers but keeps grouping the services that still point at it (ADR 0013), so nothing on the price list moves.
/// </summary>
public sealed record SetCategoryActiveCommand(Guid CategoryId, bool IsActive) : ICommand<bool>;