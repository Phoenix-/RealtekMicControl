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

    private sealed class FakeChain : IEffectChainStore
    {
        public string[]? Chain { get; set; }

        public string[]? Backup { get; set; }

        public List<string> Log { get; } = [];

        public string[]? ReadChain() => Chain;

        public void WriteChain(string[] clsids)
        {
            Chain = clsids;
            Log.Add("chain");
        }

        public string[]? ReadBackup() => Backup;

        public void WriteBackup(string[]? clsids)
        {
            Backup = clsids;
            Log.Add(clsids is null ? "backup removed" : "backup");
        }
    }

    private const string RecVol = "{806B324A-6827-46A8-AFD0-8AB09F8E6E61}";
    private const string Forte = "{70306EF0-7851-11DC-8314-0800200C9A66}";
    private const string RecInvVol = "{8065FF2D-2394-4E99-999B-8F95C44F1799}";

    private static (ForteMicrophone Mic, FakeStore Store) Create(uint mode, uint flags, uint? features)
    {
        var store = new FakeStore();
        store.Values[ForteProperties.Mode] = mode;
        store.Values[ForteProperties.Flags] = flags;
        if (features is { } f)
        {
            store.Values[ForteProperties.Features] = f;
        }

        return (new ForteMicrophone("id", "Microphone Array", _ => store, new FakeChain()), store);
    }

    private static (ForteMicrophone Mic, FakeChain Chain) CreateWithChain(params string[] chain)
    {
        var fake = new FakeChain { Chain = chain };
        return (new ForteMicrophone("id", "Microphone Array", _ => new FakeStore(), fake), fake);
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
        var mic = new ForteMicrophone("id", "Microphone Array", _ => new FakeStore(), new FakeChain());

        Assert.Throws<InvalidOperationException>(() => mic.Read());
    }

    [Fact]
    public void Fix_saves_the_driver_order_before_moving_Fortemedia_last()
    {
        var (mic, chain) = CreateWithChain(RecVol, Forte, RecInvVol);

        Assert.True(mic.FixEffectChain());

        Assert.Equal(["backup", "chain"], chain.Log);
        Assert.Equal([RecVol, Forte, RecInvVol], chain.Backup!);
        Assert.Equal([RecVol, RecInvVol, Forte], chain.Chain!);
        Assert.Equal(EffectChainState.Ok, mic.ReadEffectChain());
    }

    [Fact]
    public void Fix_leaves_a_working_or_unknown_chain_alone()
    {
        var (working, workingChain) = CreateWithChain(RecVol, RecInvVol, Forte);
        var (unknown, unknownChain) = CreateWithChain(RecVol, RecInvVol);

        Assert.False(working.FixEffectChain());
        Assert.False(unknown.FixEffectChain());
        Assert.Empty(workingChain.Log);
        Assert.Empty(unknownChain.Log);
    }

    [Fact]
    public void Unfix_puts_the_driver_order_back_and_forgets_it()
    {
        var (mic, chain) = CreateWithChain(RecVol, Forte, RecInvVol);
        mic.FixEffectChain();

        Assert.True(mic.RestoreEffectChain());

        Assert.Equal([RecVol, Forte, RecInvVol], chain.Chain!);
        Assert.Null(chain.Backup);
        Assert.Equal(EffectChainState.Broken, mic.ReadEffectChain());
    }

    [Fact]
    public void Unfix_does_not_overwrite_a_list_someone_else_rewrote()
    {
        var (mic, chain) = CreateWithChain(RecVol, Forte, RecInvVol);
        mic.FixEffectChain();
        chain.Chain = [RecVol, Forte]; // e.g. a driver update with a different list

        Assert.False(mic.RestoreEffectChain());

        Assert.Equal([RecVol, Forte], chain.Chain!);
        Assert.Null(chain.Backup);
    }

    [Fact]
    public void Unfix_without_a_saved_order_does_nothing()
    {
        var (mic, chain) = CreateWithChain(RecVol, RecInvVol, Forte);

        Assert.False(mic.RestoreEffectChain());
        Assert.Empty(chain.Log);
    }
}
