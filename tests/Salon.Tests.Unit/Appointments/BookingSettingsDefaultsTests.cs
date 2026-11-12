using Salon.Core.Settings;

namespace Salon.Tests.Unit.Appointments;

/// <summary>ADR 0012 adds the slot step and the buffer to <see cref="BookingSettings"/>; a salon that never saved settings gets these.</summary>
public class BookingSettingsDefaultsTests
{
    [Fact]
    public void Slots_are_every_fifteen_minutes_with_no_buffer_by_default()
    {
        var settings = new BookingSettings();

        settings.SlotStepMinutes.ShouldBe(15);
        settings.BufferAfterMinutes.ShouldBe(0);
        settings.MinNoticeMinutes.ShouldBe(60);
        settings.MaxAdvanceDays.ShouldBe(60);
    }
}