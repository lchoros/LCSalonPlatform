using Microsoft.EntityFrameworkCore;

using Salon.Core.Cqrs;
using Salon.Data;

namespace Salon.Web.Features.Catalog.ManageCategories;

/// <summary>Runs inside the pipeline's transaction (ADR 0003). Through the tenant filter: another salon's id is "not found", never "changed".</summary>
public sealed class SetCategoryActiveHandler(SalonDbContext db) : IRequestHandler<SetCategoryActiveCommand, bool>
{
    public async Task<bool> HandleAsync(SetCategoryActiveCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var category = await db.ServiceCategories.SingleOrDefaultAsync(c => c.Id == request.CategoryId, cancellationToken);
        if (category is null)
        {
            return false;
        }

        category.IsActive = request.IsActive;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}