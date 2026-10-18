namespace RealtekMicControl.Core;

/// <summary>Whether Windows puts the Fortemedia engine into the microphone's processing pipeline.</summary>
public enum EffectChainState
{
    /// <summary>The mode-effect list is missing or does not carry the Fortemedia APO.</summary>
    Unknown,

    /// <summary>The Fortemedia APO is the last mode effect, so Windows runs it.</summary>
    Ok,

    /// <summary>
    /// Another mode effect follows the Fortemedia APO, so Windows discards the whole chain and the
    /// microphone modes do nothing.
    /// </summary>
    Broken,
}

/// <summary>
/// The microphone's composite mode-effect list, <c>PKEY_CompositeFX_ModeEffectClsid</c>, as CLSID
/// strings in processing order.
/// </summary>
/// <remarks>
/// On a capture endpoint, Windows' audio service drops every mode effect when an APO implementing
/// <c>IApoAcousticEchoCancellation</c> is not the last of them. The Fortemedia capture APO is one,
/// and the ASUS driver lists it between Realtek's two volume APOs. See
/// notes/capture-mfx-dropped-when-aec-apo-is-not-last.md.
/// </remarks>
internal static class ModeEffectChain
{
    public static EffectChainState Classify(IReadOnlyList<string>? clsids)
    {
        if (clsids is null)
        {
            return EffectChainState.Unknown;
        }

        var forte = IndexOfForte(clsids);
        return forte < 0 ? EffectChainState.Unknown
            : forte == clsids.Count - 1 ? EffectChainState.Ok
            : EffectChainState.Broken;
    }

    /// <summary>The same list with the Fortemedia APO moved to the end, the others in their order.</summary>
    public static string[] WithForteLast(IReadOnlyList<string> clsids)
    {
        var forte = IndexOfForte(clsids);
        if (forte < 0)
        {
            return [.. clsids];
        }

        return [.. clsids.Where((_, i) => i != forte), clsids[forte]];
    }

    public static bool SameOrder(IReadOnlyList<string> a, IReadOnlyList<string> b) =>
        a.Count == b.Count && a.Zip(b).All(pair => SameClsid(pair.First, pair.Second));

    private static int IndexOfForte(IReadOnlyList<string> clsids)
    {
        for (var i = 0; i < clsids.Count; i++)
        {
            if (Guid.TryParse(clsids[i], out var clsid) && clsid == ForteProperties.CaptureApoClsid)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool SameClsid(string a, string b) =>
        Guid.TryParse(a, out var x) && Guid.TryParse(b, out var y) ? x == y : string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
