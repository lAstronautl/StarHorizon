namespace Content.Shared.UniverseClock;

public abstract class SharedUniverseClockSystem : EntitySystem
{
    // No in-universe epoch/calendar system exists in this codebase to source this from, so this
    // is just wall-clock time - enough for RtcDeviceSystem's read-clock/schedule-interrupt use.
    public static DateTimeOffset UniversalDateTimeOffset => DateTimeOffset.UtcNow;
}
