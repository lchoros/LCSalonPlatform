namespace Salon.Web.Features.Catalog.StaffOverrides;

/// <summary>
/// The staff form as rendered and as posted: the service (display only) and one row per bookable staff member with an
/// "offers" box, minutes and price. Mutable with a settable collection because MVC binds <c>Rows[i].Price</c> into it;
/// the controller maps it to <see cref="SaveStaffOverridesCommand"/>, whose failures carry the same property paths back
/// to the fields.
/// </summary>
public sealed class StaffOverridesForm
{
    /// <summary>From the route, not the form body.</summary>
    public Guid ServiceId { get; set; }

    /// <summary>Display only; the controller fills these from the database on every render.</summary>
    public string ServiceName { get; set; } = string.Empty;

    public int DefaultDurationMinutes { get; set; }

    public decimal DefaultPrice { get; set; }

    public string Currency { get; set; } = string.Empty;

    public IList<StaffOverrideRowForm> Rows { get; init; } = [];

    /// <summary>The form for a service, one row per bookable person, blanks where the defaults apply.</summary>
    public static StaffOverridesForm From(StaffOverridesData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var form = new StaffOverridesForm { ServiceId = data.Service.Id };
        form.Describe(data);
        foreach (var row in data.Rows)
        {
            form.Rows.Add(new StaffOverrideRowForm
            {
                StaffMemberId = row.StaffMemberId,
                DisplayName = row.DisplayName,
                Offers = row.Offers,
                DurationMinutes = row.DurationMinutes,
                Price = row.Price,
            });
        }

        return form;
    }

    /// <summary>Refreshes the display-only parts (service header, staff names) from the database on a re-render; the posted values stay.</summary>
    public void Describe(StaffOverridesData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        ServiceId = data.Service.Id;
        ServiceName = data.Service.Name;
        DefaultDurationMinutes = (int)data.Service.Duration.TotalMinutes;
        DefaultPrice = data.Service.Price;
        Currency = data.Service.Currency;
        var names = data.Rows.ToDictionary(r => r.StaffMemberId, r => r.DisplayName);
        foreach (var row in Rows)
        {
            row.DisplayName = names.GetValueOrDefault(row.StaffMemberId, row.DisplayName);
        }
    }

    public SaveStaffOverridesCommand ToCommand()
        => new(ServiceId, Rows.Select(r => new StaffOverrideInput(r.StaffMemberId, r.Offers, r.DurationMinutes, r.Price)).ToList());
}

public sealed class StaffOverrideRowForm
{
    public Guid StaffMemberId { get; set; }

    /// <summary>Display only.</summary>
    public string DisplayName { get; set; } = string.Empty;

    public bool Offers { get; set; } = true;

    public int? DurationMinutes { get; set; }

    public decimal? Price { get; set; }
}