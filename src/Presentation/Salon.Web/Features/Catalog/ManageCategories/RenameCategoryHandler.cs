using Microsoft.EntityFrameworkCore;
using Salon.Core.Catalog;
using Salon.Core.Cqrs;
using Salon.Data;

namespace Salon.Web.Features.Catalog.ManageCategories;

/// <summary>Runs inside the pipeline's transaction (ADR 0003). Through the tenant filter: another salon's id is "not found", never "renamed".</summary>
public sealed class RenameCategoryHandler(SalonDbContext db) : IRequestHandler<RenameCategoryCommand, bool>
{
    public async Task<bool> HandleAsync(RenameCategoryCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var category = await db.ServiceCategories.SingleOrDefaultAsync(c => c.Id == request.CategoryId, cancellationToken);
        if (category is null)
        {
            return false;
        }

        category.Name = ServiceRules.NormalizeName(request.Name);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}