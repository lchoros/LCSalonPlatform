using Salon.Services.Settings;

namespace Salon.Tests.Unit.Settings;

public class SettingNameTests
{
    [Theory]
    [InlineData("BookingSettings.MinNoticeMinutes")]
    [InlineData("Plugins.Payments.Manual.Enabled")]
    [InlineData("A.B1")]
    public void Accepts_class_dot_property_names(string name)
    {
        Should.NotThrow(() => SettingName.Validate(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("NoDot")]
    [InlineData(".Leading")]
    [InlineData("Trailing.")]
    [InlineData("Has Space.Prop")]
    [InlineData("1Digit.First")]
    [InlineData("Semi;colon.Prop")]
    public void Rejects_names_that_are_not_class_dot_property(string name)
    {
        Should.Throw<ArgumentException>(() => SettingName.Validate(name));
    }

    [Fact]
    public void Rejects_names_over_the_limit()
    {
        var name = "A." + new string('b', SettingName.MaxNameLength);

        Should.Throw<ArgumentException>(() => SettingName.Validate(name));
    }

    [Fact]
    public void Caps_value_length_but_allows_null()
    {
        Should.NotThrow(() => SettingName.ValidateValue(null, "A.B"));
        Should.NotThrow(() => SettingName.ValidateValue(new string('x', SettingName.MaxValueLength), "A.B"));
        Should.Throw<ArgumentException>(() => SettingName.ValidateValue(new string('x', SettingName.MaxValueLength + 1), "A.B"));
    }
}