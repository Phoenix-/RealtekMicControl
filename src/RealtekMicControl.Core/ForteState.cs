namespace RealtekMicControl.Core;

/// <summary>The beamforming pattern of the internal microphone array.</summary>
/// <remarks>The values are what the Fortemedia APO stores in its mode key (K2).</remarks>
public enum MicMode
{
    Omni = 1,
    Stereo = 2,
    Cardioid = 3,
}

/// <summary>
/// What the four-way switch shows: microphone modes turned off, or on with one of the patterns.
/// </summary>
public enum MicSetting
{
    Off,
    Cardioid,
    Stereo,
    Omni,
}

/// <summary>
/// The Fortemedia SAMSoft capture APO's settings as Armoury Crate writes them. Raw values are kept
/// so a write never disturbs bits this tool does not understand.
/// </summary>
/// <param name="ModeValue">K2, <c>{2b24be42-a892-11dc-8314-0800200c9a66},2</c>: the pattern, see <see cref="MicMode"/>.</param>
/// <param name="Flags">K7, <c>…,7</c>: driver default 0x890000; <see cref="ModesOffBit"/> set means modes are off.</param>
/// <param name="Features">K9001, <c>…,9001</c>, when present: carries <see cref="NoiseSuppressionBit"/>.</param>
public readonly record struct ForteState(uint ModeValue, uint Flags, uint? Features)
{
    /// <summary>Set by Armoury Crate's "Microphone modes" switch when it is turned off.</summary>
    public const uint ModesOffBit = 0x4;

    /// <summary>Armoury Crate's separate "intelligent noise suppression" switch.</summary>
    public const uint NoiseSuppressionBit = 0x1000_0000;

    public bool ModesEnabled => (Flags & ModesOffBit) == 0;

    /// <summary>The stored pattern, or null for a value this tool does not know.</summary>
    /// <remarks>Kept while modes are off, which is how "on" returns to the last pattern.</remarks>
    public MicMode? Mode => ModeValue is >= 1 and <= 3 ? (MicMode)ModeValue : null;

    /// <summary>Null when the driver does not expose K9001 at all.</summary>
    public bool? NoiseSuppression => Features is { } features ? (features & NoiseSuppressionBit) != 0 : null;

    /// <summary>
    /// The four-way switch position, or null when modes are on with a pattern value no UI writes;
    /// reported as unknown rather than rounded to one of the four.
    /// </summary>
    public MicSetting? Setting => !ModesEnabled
        ? MicSetting.Off
        : Mode switch
        {
            MicMode.Cardioid => MicSetting.Cardioid,
            MicMode.Stereo => MicSetting.Stereo,
            MicMode.Omni => MicSetting.Omni,
            _ => null,
        };

    public ForteState WithModesEnabled(bool enabled) =>
        this with { Flags = enabled ? Flags & ~ModesOffBit : Flags | ModesOffBit };

    /// <summary>Selects a pattern, turning modes on as Armoury Crate does.</summary>
    public ForteState WithMode(MicMode mode) =>
        WithModesEnabled(true) with { ModeValue = (uint)mode };

    public ForteState WithSetting(MicSetting setting) => setting switch
    {
        MicSetting.Off => WithModesEnabled(false),
        MicSetting.Cardioid => WithMode(MicMode.Cardioid),
        MicSetting.Stereo => WithMode(MicMode.Stereo),
        MicSetting.Omni => WithMode(MicMode.Omni),
        _ => throw new ArgumentOutOfRangeException(nameof(setting), setting, null),
    };

    public ForteState WithNoiseSuppression(bool enabled)
    {
        if (Features is not { } features)
        {
            throw new InvalidOperationException("This driver does not expose the intelligent noise suppression setting (K9001).");
        }

        return this with { Features = enabled ? features | NoiseSuppressionBit : features & ~NoiseSuppressionBit };
    }
}
