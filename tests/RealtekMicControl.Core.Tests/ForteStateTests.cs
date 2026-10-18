namespace RealtekMicControl.Core.Tests;

public class ForteStateTests
{
    // Values observed on an ASUS ROG Zephyrus G15 GA503QS while toggling Armoury Crate.
    private const uint DriverDefaultFlags = 0x890000;
    private const uint ObservedFeatures = 0x1000_0000;

    [Theory]
    [InlineData(3u, MicSetting.Cardioid)]
    [InlineData(2u, MicSetting.Stereo)]
    [InlineData(1u, MicSetting.Omni)]
    public void Modes_on_reports_the_stored_pattern(uint modeValue, MicSetting expected)
    {
        var state = new ForteState(modeValue, DriverDefaultFlags, ObservedFeatures);

        Assert.True(state.ModesEnabled);
        Assert.Equal(expected, state.Setting);
    }

    [Fact]
    public void The_off_bit_wins_over_the_pattern_but_keeps_it()
    {
        var state = new ForteState(3, DriverDefaultFlags | ForteState.ModesOffBit, ObservedFeatures);

        Assert.False(state.ModesEnabled);
        Assert.Equal(MicSetting.Off, state.Setting);
        Assert.Equal(MicMode.Cardioid, state.Mode);
    }

    [Fact]
    public void An_unknown_pattern_is_reported_as_unknown()
    {
        var state = new ForteState(7, DriverDefaultFlags, null);

        Assert.Null(state.Mode);
        Assert.Null(state.Setting);
    }

    [Fact]
    public void Turning_off_sets_exactly_the_off_bit()
    {
        var state = new ForteState(3, DriverDefaultFlags, ObservedFeatures).WithSetting(MicSetting.Off);

        // What Armoury Crate wrote: 0x890000 -> 0x890004, pattern untouched.
        Assert.Equal(0x890004u, state.Flags);
        Assert.Equal(3u, state.ModeValue);
    }

    [Fact]
    public void Picking_a_pattern_turns_modes_back_on()
    {
        var state = new ForteState(1, DriverDefaultFlags | ForteState.ModesOffBit, ObservedFeatures)
            .WithSetting(MicSetting.Stereo);

        Assert.Equal(DriverDefaultFlags, state.Flags);
        Assert.Equal(2u, state.ModeValue);
        Assert.Equal(MicSetting.Stereo, state.Setting);
    }

    [Fact]
    public void Turning_on_returns_to_the_last_pattern()
    {
        var off = new ForteState(2, DriverDefaultFlags | ForteState.ModesOffBit, null);

        Assert.Equal(MicSetting.Stereo, off.WithModesEnabled(true).Setting);
    }

    [Fact]
    public void Unknown_flag_bits_survive_every_change()
    {
        const uint noise = 0xA5A5_0000 | 0x10 | 0x1;
        var state = new ForteState(3, noise, 0x2000_0001);

        var changed = state.WithSetting(MicSetting.Off).WithSetting(MicSetting.Omni).WithNoiseSuppression(true);

        Assert.Equal(noise, changed.Flags);
        Assert.Equal(0x3000_0001u, changed.Features);
    }

    [Fact]
    public void Noise_suppression_reads_and_toggles_one_bit()
    {
        var on = new ForteState(3, DriverDefaultFlags, ObservedFeatures);

        Assert.True(on.NoiseSuppression);
        Assert.Equal(0u, on.WithNoiseSuppression(false).Features);
        Assert.Equal(ObservedFeatures, on.WithNoiseSuppression(false).WithNoiseSuppression(true).Features);
    }

    [Fact]
    public void Noise_suppression_is_unknown_and_unsettable_without_K9001()
    {
        var state = new ForteState(3, DriverDefaultFlags, null);

        Assert.Null(state.NoiseSuppression);
        Assert.Throws<InvalidOperationException>(() => state.WithNoiseSuppression(true));
    }
}
