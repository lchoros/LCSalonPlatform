using Salon.Core.Cqrs;
using Salon.Core.Products;

namespace Salon.Web.Features.Products.SaveProduct;

/// <summary>
/// Keeps a product's details (ADR 0036): adds one when <paramref name="Id"/> is null, changes it otherwise. Bound
/// straight from the admin form; <see cref="SaveProductValidator"/> holds the rules, the handler trims the text and
/// takes the currency from the tenant. Returns the product's id, or null when the salon has no product to change.
/// The shelf itself is not changed here, with one exception: a new product may start with a count.
/// </summary>
/// <param name="Name">What the desk calls it; unique per salon.</param>
/// <param name="Brand">The maker or the range; optional.</param>
/// <param name="Code">The barcode or the supplier's code; optional.</param>
/// <param name="RetailPrice">What a client pays for one; blank for a product the salon only uses itself.</param>
/// <param name="LowStockAt">Show the product as low at this many or fewer; blank never warns.</param>
/// <param name="IsActive">False takes the product out of the list of products in use; its history stays.</param>
/// <param name="OpeningStock">Only for a new product: what is on the shelf today. Recorded as its first count.</param>
/// <param name="ByUserId">The login that saves; set by the controller, never from the form.</param>
public sealed record SaveProductCommand(
    Guid? Id,
    string Name,
    string? Brand,
    string? Code,
    decimal? RetailPrice,
    int? LowStockAt,
    bool IsActive,
    int? OpeningStock = null,
    Guid? ByUserId = null) : ICommand<Guid?>
{
    public bool IsNew => Id is null;

    /// <summary>The form for an existing product.</summary>
    public static SaveProductCommand From(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);
        return new SaveProductCommand(product.Id, product.Name, product.Brand, product.Code, product.RetailPrice, product.LowStockAt, product.IsActive);
    }

    /// <summary>The empty form for a new product.</summary>
    public static SaveProductCommand Empty { get; } = new(null, string.Empty, null, null, null, null, true);
}

/// <summary>The product behind the edit form; null when the salon has none with this id.</summary>
public sealed record GetProductForEditQuery(Guid Id) : IQuery<Product?>;