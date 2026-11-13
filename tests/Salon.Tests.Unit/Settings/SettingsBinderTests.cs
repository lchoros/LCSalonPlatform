using NodaTime;
using Salon.Core.Settings;
using Salon.Services.Settings;

namespace Salon.Tests.Unit.Settings;

public class SettingsBinderTests
{
    public enum Colour
    {
        Red,
        Green,
    }

    /// <summary>Every property type the binder promises to support, each with a non-default default.</summary>
    public sealed class AllTypesSettings : ISettings
    {
        public string Text { get; set; } = "default";

        public string? OptionalText { get; set; } = "set";

        public int Number { get; set; } = 1;

        public int? OptionalInt { get; set; } = 2;

        public long BigNumber { get; set; } = 3L;

        public decimal Amount { get; set; } = 1.5m;

        public bool Bool { get; set; } = true;

        public Guid Identifier { get; set; } = Guid.Parse("018f3f1e-2b7a-7c3e-9d1a-5b6c7d8e9f00");

        public Colour Enum { get; set; } = Colour.Red;

        public DateOnly Date { get; set; } = new(2026, 1, 2);

        public TimeOnly Time { get; set; } = new(9, 30);

        public LocalTime LocalTime { get; set; } = new(10, 15);

        public Duration Duration { get; set; } = Duration.FromMinutes(45);

        public int ReadOnly => Number + 99;
    }

    [Fact]
    public void Round_trips_every_supported_type()
    {
        var original = new AllTypesSettings
        {
            Text = "zażółć",
            OptionalText = null,
            Number = -42,
            OptionalInt = null,
            BigNumber = 1234567890123L,
            Amount = 12345.678m,
            Bool = false,
            Identifier = Guid.Parse("0190a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b"),
            Enum = Colour.Green,
            Date = new DateOnly(2026, 12, 31),
            Time = new TimeOnly(23, 59, 59, 999),
            LocalTime = new LocalTime(8, 5, 30),
            Duration = Duration.FromHours(1) + Duration.FromMinutes(30) + Duration.FromSeconds(15),
        };

        var rows = SettingsBinder.Flatten(original).ToDictionary(r => r.Key, r => r.Value, StringComparer.Ordinal);
        var bound = SettingsBinder.Bind<AllTypesSettings>(rows);

        bound.Text.ShouldBe(original.Text);
        bound.OptionalText.ShouldBeNull();
        bound.Number.ShouldBe(original.Number);
        bound.OptionalInt.ShouldBeNull();
        bound.BigNumber.ShouldBe(original.BigNumber);
        bound.Amount.ShouldBe(original.Amount);
        bound.Bool.ShouldBe(original.Bool);
        bound.Identifier.ShouldBe(original.Identifier);
        bound.Enum.ShouldBe(original.Enum);
        bound.Date.ShouldBe(original.Date);
        bound.Time.ShouldBe(original.Time);
        bound.LocalTime.ShouldBe(original.LocalTime);
        bound.Duration.ShouldBe(original.Duration);
    }

    [Fact]
    public void Rows_are_named_type_dot_property_and_skip_read_only_properties()
    {
        var names = SettingsBinder.Flatten(new AllTypesSettings()).Select(r => r.Key).ToList();

        names.ShouldContain("AllTypesSettings.Number");
        names.ShouldContain("AllTypesSettings.LocalTime");
        names.ShouldNotContain("AllTypesSettings.ReadOnly");
        names.ShouldAllBe(n => n.StartsWith("AllTypesSettings.", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_rows_keep_the_class_defaults_and_unknown_rows_are_ignored()
    {
        var rows = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["AllTypesSettings.Number"] = "7",
            ["AllTypesSettings.Nope"] = "ignored",
            ["OtherSettings.Number"] = "99",
        };

        var bound = SettingsBinder.Bind<AllTypesSettings>(rows);

        bound.Number.ShouldBe(7);
        bound.Text.ShouldBe("default");
        bound.Amount.ShouldBe(1.5m);
        bound.LocalTime.ShouldBe(new LocalTime(10, 15));
    }

    [Fact]
    public void A_null_row_for_a_non_nullable_value_keeps_the_default()
    {
        var rows = new Dictionary<string, string?>(StringComparer.Ordinal) { ["AllTypesSettings.Number"] = null };

        SettingsBinder.Bind<AllTypesSettings>(rows).Number.ShouldBe(1);
    }

    [Fact]
    public void Booking_settings_defaults_match_the_product_rules()
    {
        var settings = new BookingSettings();

        settings.MinNoticeMinutes.ShouldBe(60);
        settings.MaxAdvanceDays.ShouldBe(60);
        settings.RequireSmsVerification.ShouldBeTrue();
        settings.DefaultDepositPercent.ShouldBe(0m);
    }

    public static TheoryData<object?, string?> SerialisedForms => new()
    {
        { null, null },
        { "text", "text" },
        { 12.5m, "12.5" },
        { 1000000, "1000000" },
        { true, "True" },
        { Colour.Green, "Green" },
        { new DateOnly(2026, 3, 9), "2026-03-09" },
        { new TimeOnly(7, 5), "07:05:00.0000000" },
        { new LocalTime(7, 5), "07:05:00" },
        { Duration.FromMinutes(90), "0:01:30:00" },
    };

    /// <summary>Stored text is culture-invariant so a Polish and a British salon share the same format.</summary>
    [Theory]
    [MemberData(nameof(SerialisedForms))]
    public void Serialises_with_the_invariant_culture(object? value, string? expected)
    {
        SettingsBinder.Serialize(value).ShouldBe(expected);
    }

    [Fact]
    public void Parses_decimals_and_enums_case_insensitively_with_the_invariant_culture()
    {
        SettingsBinder.Deserialize("12.5", typeof(decimal)).ShouldBe(12.5m);
        SettingsBinder.Deserialize("green", typeof(Colour)).ShouldBe(Colour.Green);
        SettingsBinder.Deserialize("true", typeof(bool)).ShouldBe(true);
        SettingsBinder.Deserialize("5", typeof(int?)).ShouldBe(5);
        SettingsBinder.Deserialize(null, typeof(int?)).ShouldBeNull();
    }
}