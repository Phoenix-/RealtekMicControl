namespace RealtekMicControl.Core.Tests;

public class ModeEffectChainTests
{
    private const string RecVol = "{806B324A-6827-46A8-AFD0-8AB09F8E6E61}";
    private const string Forte = "{70306EF0-7851-11DC-8314-0800200C9A66}";
    private const string RecInvVol = "{8065FF2D-2394-4E99-999B-8F95C44F1799}";

    [Fact]
    public void The_ASUS_driver_order_is_broken()
    {
        Assert.Equal(EffectChainState.Broken, ModeEffectChain.Classify([RecVol, Forte, RecInvVol]));
    }

    [Fact]
    public void Fortemedia_last_or_alone_is_ok()
    {
        Assert.Equal(EffectChainState.Ok, ModeEffectChain.Classify([RecVol, RecInvVol, Forte]));
        Assert.Equal(EffectChainState.Ok, ModeEffectChain.Classify([Forte]));
    }

    [Fact]
    public void A_list_without_Fortemedia_or_no_list_is_unknown()
    {
        Assert.Equal(EffectChainState.Unknown, ModeEffectChain.Classify([RecVol, RecInvVol]));
        Assert.Equal(EffectChainState.Unknown, ModeEffectChain.Classify(null));
    }

    [Fact]
    public void Clsids_match_regardless_of_case()
    {
        Assert.Equal(EffectChainState.Broken, ModeEffectChain.Classify([Forte.ToLowerInvariant(), RecVol]));
    }

    [Fact]
    public void Moving_Fortemedia_last_keeps_the_others_in_order()
    {
        Assert.Equal([RecVol, RecInvVol, Forte], ModeEffectChain.WithForteLast([RecVol, Forte, RecInvVol]));
        Assert.Equal([RecVol, RecInvVol, Forte], ModeEffectChain.WithForteLast([Forte, RecVol, RecInvVol]));
    }
}
