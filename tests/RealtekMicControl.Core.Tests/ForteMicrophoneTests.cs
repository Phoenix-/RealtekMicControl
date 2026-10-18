namespace RealtekMicControl.Core.Tests;

public class ForteMicrophoneTests
{
    private sealed class FakeStore : IForteSettingsStore
    {
        public Dictionary<uint, uint> Values { get; } = [];

        public List<uint> Written { get; } = [];

        public int Commits { get; private set; }

        public bool TryRead(uint propertyId, out uint value) => Values.TryGetValue(propertyId, out value);

        public void Write(uint propertyId, uint value)
        {
            Values[propertyId] = value;
            Written.Add(propertyId);
        }

        public void Commit() => Commits++;
    }

    private static (ForteMicrophone Mic, FakeStore Store) Create(uint mode, uint flags, uint? features)
    {
        var store = new FakeStore();
        store.Values[ForteProperties.Mode] = mode;
        store.Values[ForteProperties.Flags] = flags;
        if (features is { } f)
        {
            store.Values[ForteProperties.Features] = f;
        }

        return (new ForteMicrophone("id", "Microphone Array", _ => store), store);
    }

    [Fact]
    public void Apply_writes_only_the_values_that_change()
    {
        var (mic, store) = Create(mode: 3, flags: 0x890000, features: 0x1000_0000);

        var after = mic.Apply(MicSetting.Off);

        Assert.Equal([ForteProperties.Flags], store.Written);
        Assert.Equal(1, store.Commits);
        Assert.Equal(MicSetting.Off, after.Setting);
    }

    [Fact]
    public void Apply_of_the_current_setting_writes_nothing()
    {
        var (mic, store) = Create(mode: 2, flags: 0x890000, features: null);

        mic.Apply(MicSetting.Stereo);

        Assert.Empty(store.Written);
        Assert.Equal(0, store.Commits);
    }

    [Fact]
    public void Picking_a_pattern_while_off_writes_both_keys()
    {
        var (mic, store) = Create(mode: 3, flags: 0x890004, features: null);

        var after = mic.Apply(MicSetting.Omni);

        Assert.Equal([ForteProperties.Mode, ForteProperties.Flags], store.Written);
        Assert.Equal(MicSetting.Omni, after.Setting);
    }

    [Fact]
    public void A_store_without_the_keys_is_reported_not_guessed()
    {
        var mic = new ForteMicrophone("id", "Microphone Array", _ => new FakeStore());

        Assert.Throws<InvalidOperationException>(() => mic.Read());
    }
}
