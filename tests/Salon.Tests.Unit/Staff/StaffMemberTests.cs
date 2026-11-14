using NodaTime;
using Salon.Core.Staff;
using Salon.Core.Tenancy;

namespace Salon.Tests.Unit.Staff;

/// <summary>Defaults and the colour rule of the StaffMember entity, plus the WorkingHours range view (ADR 0010).</summary>
public class StaffMemberTests
{
    [Fact]
    public void A_new_staff_member_is_active_bookable_grey_and_unlinked()
    {
        var staff = new StaffMember { DisplayName = "Anna Kowalska" };

        staff.Id.Version.ShouldBe(7);
        staff.TenantId.ShouldBe(Guid.Empty, "the DbContext stamps the current tenant on save");
        staff.IsActive.ShouldBeTrue();
        staff.IsBookable.ShouldBeTrue();
        staff.Colour.ShouldBe("#78716C");
        staff.Colour.ShouldBe(StaffMember.DefaultColour);
        staff.Email.ShouldBeNull();
        staff.Phone.ShouldBeNull();
        staff.UserId.ShouldBeNull();
        staff.SortOrder.ShouldBe(0);
        staff.CreatedAt.ShouldBe(default(Instant));
    }

    [Fact]
    public void Both_entities_are_tenant_owned()
    {
        typeof(ITenantEntity).IsAssignableFrom(typeof(StaffMember)).ShouldBeTrue();
        typeof(ITenantEntity).IsAssignableFrom(typeof(WorkingHours)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("#78716C")]
    [InlineData("#be185d")]
    [InlineData("#000000")]
    [InlineData("#FFFFFF")]
    public void A_six_digit_hex_colour_in_either_case_is_valid(string colour)
    {
        StaffMember.IsValidColour(colour).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("78716C")]
    [InlineData("#78716")]
    [InlineData("#78716CC")]
    [InlineData("#GGGGGG")]
    [InlineData("red")]
    [InlineData(" #78716C")]
    public void Anything_but_hash_RRGGBB_is_not_a_colour(string? colour)
    {
        StaffMember.IsValidColour(colour).ShouldBeFalse();
    }

    [Fact]
    public void The_length_limits_match_the_mapping_and_the_colour_format()
    {
        StaffMember.DisplayNameMaxLength.ShouldBe(100);
        StaffMember.EmailMaxLength.ShouldBe(254);
        StaffMember.PhoneMaxLength.ShouldBe(30);
        StaffMember.ColourLength.ShouldBe("#RRGGBB".Length);
    }

    [Fact]
    public void A_working_hours_row_exposes_its_range()
    {
        var row = new WorkingHours { StaffMemberId = Guid.CreateVersion7(), Day = IsoDayOfWeek.Tuesday, Start = new LocalTime(10, 0), End = new LocalTime(18, 0) };

        row.Range.ShouldBe(new TimeRange(new LocalTime(10, 0), new LocalTime(18, 0)));
        row.Id.Version.ShouldBe(7);
        row.SortOrder.ShouldBe(0);
    }
}