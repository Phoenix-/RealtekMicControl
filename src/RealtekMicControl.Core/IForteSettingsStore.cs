namespace RealtekMicControl.Core;

/// <summary>
/// The Fortemedia APO's user property store, reduced to the 32-bit values it holds, so the
/// read-modify-write logic can be tested without Core Audio.
/// </summary>
internal interface IForteSettingsStore
{
    bool TryRead(uint propertyId, out uint value);

    void Write(uint propertyId, uint value);

    void Commit();
}

internal static class ForteProperties
{
    /// <summary>The Fortemedia SAMSoft capture APO, CFMAPOCAP (mode effect on the mic array).</summary>
    public static readonly Guid CaptureApoClsid = new("70306EF0-7851-11DC-8314-0800200C9A66");

    /// <summary>Format id shared by K2, K7 and K9001.</summary>
    public static readonly Guid FormatId = new("2B24BE42-A892-11DC-8314-0800200C9A66");

    public const uint Mode = 2;
    public const uint Flags = 7;
    public const uint Features = 9001;

    public static bool TryRead(IForteSettingsStore store, out ForteState state)
    {
        state = default;
        if (!store.TryRead(Mode, out var mode) || !store.TryRead(Flags, out var flags))
        {
            return false;
        }

        uint? features = store.TryRead(Features, out var f) ? f : null;
        state = new ForteState(mode, flags, features);
        return true;
    }

    /// <summary>Writes only what changed, then commits.</summary>
    public static void Write(IForteSettingsStore store, ForteState before, ForteState after)
    {
        if (after.ModeValue != before.ModeValue)
        {
            store.Write(Mode, after.ModeValue);
        }

        if (after.Flags != before.Flags)
        {
            store.Write(Flags, after.Flags);
        }

        if (after.Features is { } features && features != before.Features)
        {
            store.Write(Features, features);
        }

        store.Commit();
    }
}
