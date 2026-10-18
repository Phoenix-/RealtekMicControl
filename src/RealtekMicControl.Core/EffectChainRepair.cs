using System.Security.Principal;

namespace RealtekMicControl.Core;

/// <summary>What fix / unfix did to one microphone's mode-effect list.</summary>
public enum EffectChainChange
{
    Fixed,
    AlreadyOk,
    UnknownLayout,
    Restored,
    NothingToRestore,
}

/// <summary>
/// fix / unfix for every Fortemedia microphone, shared by the CLI and the windowless elevated
/// mode of the app: rewrite each list, then restart Windows Audio once if anything changed, since
/// it only reads the lists when it starts.
/// </summary>
public static class EffectChainRepair
{
    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <param name="restore">false = fix, true = put the driver's order back.</param>
    /// <param name="done">Called for each microphone as soon as its list is handled.</param>
    /// <param name="restarting">Called right before Windows Audio is restarted, if it is.</param>
    /// <returns>Whether anything changed (and Windows Audio was restarted).</returns>
    public static bool Run(IReadOnlyList<ForteMicrophone> microphones, bool restore,
        Action<ForteMicrophone, EffectChainChange>? done = null, Action? restarting = null)
    {
        var changed = false;
        foreach (var mic in microphones)
        {
            var change = restore ? Restore(mic) : Fix(mic);
            changed |= change is EffectChainChange.Fixed or EffectChainChange.Restored;
            done?.Invoke(mic, change);
        }

        if (changed)
        {
            restarting?.Invoke();
            AudioService.Restart();
        }

        return changed;
    }

    private static EffectChainChange Fix(ForteMicrophone mic)
    {
        var before = mic.ReadEffectChain();
        return mic.FixEffectChain() ? EffectChainChange.Fixed
            : before == EffectChainState.Ok ? EffectChainChange.AlreadyOk
            : EffectChainChange.UnknownLayout;
    }

    private static EffectChainChange Restore(ForteMicrophone mic) =>
        mic.RestoreEffectChain() ? EffectChainChange.Restored : EffectChainChange.NothingToRestore;
}
