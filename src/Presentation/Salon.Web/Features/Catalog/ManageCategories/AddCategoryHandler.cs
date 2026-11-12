using Microsoft.EntityFrameworkCore;

using Salon.Core.Catalog;
using Salon.Core.Cqrs;
using Salon.Data;

namespace Salon.Web.Features.Catalog.ManageCategories;

/// <summary>Runs inside the pipeline's transaction (ADR 0003). The new category goes last in the list; reordering is a later slice.</summary>
public sealed class AddCategoryHandler(SalonDbContext db) : IRequestHandler<AddCategoryCommand, Guid>
{
    public async Task<Guid> HandleAsync(AddCategoryCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var lastSortOrder = await db.ServiceCategories.MaxAsync(c => (int?)c.SortOrder, cancellationToken) ?? 0;
        var category = new ServiceCategory { Name = ServiceRules.NormalizeName(request.Name), SortOrder = lastSortOrder + 1 };

        db.ServiceCategories.Add(category);
        await db.SaveChangesAsync(cancellationToken);
        return category.Id;
    }
}