namespace Salon.Core.Settings;

/// <summary>A step of setting a salon up (ADR 0068). The steps are read from the salon's own data, never ticked.</summary>
public enum SetupStepKey
{
    SalonDetails = 1,
    Team = 2,
    Hours = 3,
    PriceList = 4,
    Clients = 5,
    Diary = 6,
    Reminders = 7,
    Policy = 8,
    PrivacyNotice = 9,
}