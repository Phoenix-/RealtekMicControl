using System.Runtime.InteropServices;
using RealtekMicControl.Core.Interop;

namespace RealtekMicControl.Core;

/// <summary>
/// A capture endpoint that carries the Fortemedia SAMSoft capture APO -- on the ASUS laptops this
/// was built for, the internal microphone array ("Microphone Array (Realtek(R) Audio)").
/// </summary>
/// <remarks>
/// Every read and every change opens the store afresh: Armoury Crate or the Fortemedia service may
/// have changed the values in between, and a change is a read-modify-write of raw values.
/// </remarks>
public sealed class ForteMicrophone
{
    private readonly Func<bool, IForteSettingsStore> _openStore;
    private readonly IEffectChainStore _effectChain;

    internal ForteMicrophone(string id, string name, Func<bool, IForteSettingsStore> openStore, IEffectChainStore effectChain)
    {
        Id = id;
        Name = name;
        _openStore = openStore;
        _effectChain = effectChain;
    }

    /// <summary>The endpoint id, <c>{0.0.1.00000000}.{guid}</c>.</summary>
    public string Id { get; }

    /// <summary>The endpoint's friendly name as Windows shows it.</summary>
    public string Name { get; }

    public ForteState Read()
    {
        if (!ForteProperties.TryRead(_openStore(false), out var state))
        {
            throw new InvalidOperationException($"'{Name}' no longer exposes the Fortemedia settings.");
        }

        return state;
    }

    /// <summary>Applies <paramref name="change"/> to the current values and returns what was stored.</summary>
    public ForteState Update(Func<ForteState, ForteState> change)
    {
        var store = _openStore(true);
        if (!ForteProperties.TryRead(store, out var before))
        {
            throw new InvalidOperationException($"'{Name}' no longer exposes the Fortemedia settings.");
        }

        var after = change(before);
        if (after != before)
        {
            ForteProperties.Write(store, before, after);
        }

        return Read();
    }

    public ForteState Apply(MicSetting setting) => Update(state => state.WithSetting(setting));

    public ForteState SetNoiseSuppression(bool enabled) => Update(state => state.WithNoiseSuppression(enabled));

    /// <summary>Whether Windows actually runs the Fortemedia engine the settings above configure.</summary>
    public EffectChainState ReadEffectChain() => ModeEffectChain.Classify(_effectChain.ReadChain());

    /// <summary>
    /// Moves the Fortemedia APO to the end of the mode-effect list, keeping the driver's order for
    /// <see cref="RestoreEffectChain"/>. Returns false when there is nothing to fix. Needs an
    /// administrator; takes effect when <see cref="AudioService.Restart"/> runs.
    /// </summary>
    public bool FixEffectChain()
    {
        var chain = _effectChain.ReadChain();
        if (ModeEffectChain.Classify(chain) != EffectChainState.Broken)
        {
            return false;
        }

        // The copy first: if it cannot be written, the list is left as the driver set it.
        _effectChain.WriteBackup(chain);
        _effectChain.WriteChain(ModeEffectChain.WithForteLast(chain!));
        return true;
    }

    /// <summary>
    /// Puts the driver's order saved by <see cref="FixEffectChain"/> back. Returns false when there
    /// is none, or when the list has changed since (a driver reinstall writes its own order back).
    /// </summary>
    public bool RestoreEffectChain()
    {
        if (_effectChain.ReadBackup() is not { } original)
        {
            return false;
        }

        var fixedByUs = _effectChain.ReadChain() is { } current
                        && ModeEffectChain.SameOrder(current, ModeEffectChain.WithForteLast(original));
        if (fixedByUs)
        {
            _effectChain.WriteChain(original);
        }

        _effectChain.WriteBackup(null);
        return fixedByUs;
    }

    /// <summary>
    /// Active capture endpoints whose Fortemedia capture APO exposes the mode settings. Normally
    /// exactly one, the internal mic array; none when the driver set is incomplete.
    /// </summary>
    public static IReadOnlyList<ForteMicrophone> FindAll()
    {
        ComInterop.EnsureInitialized();
        var enumerator = ComInterop.CreateDeviceEnumerator();
        NativeMethods.ThrowOnFailure(
            enumerator.EnumAudioEndpoints(EDataFlow.Capture, DeviceState.Active, out var devices),
            "IMMDeviceEnumerator.EnumAudioEndpoints");

        devices!.GetCount(out var count);
        var found = new List<ForteMicrophone>();
        for (uint i = 0; i < count; i++)
        {
            if (devices.Item(i, out var device) < 0 || device is null)
            {
                continue;
            }

            IForteSettingsStore Open(bool writable) =>
                ApoUserPropertyStore.Open(device, ForteProperties.CaptureApoClsid, writable);

            try
            {
                // Endpoints without the APO fail activation or simply lack the keys.
                if (!ForteProperties.TryRead(Open(false), out _))
                {
                    continue;
                }
            }
            catch (COMException)
            {
                continue;
            }

            var id = GetId(device);
            found.Add(new ForteMicrophone(id, GetFriendlyName(device) ?? "Microphone", Open, new RegistryEffectChainStore(id)));
        }

        return found;
    }

    private static string GetId(IMMDevice device)
    {
        NativeMethods.ThrowOnFailure(device.GetId(out var id), "IMMDevice.GetId");
        try
        {
            return Marshal.PtrToStringUni(id) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeCoTaskMem(id);
        }
    }

    private static readonly PropertyKey FriendlyNameKey = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

    private static string? GetFriendlyName(IMMDevice device)
    {
        if (device.OpenPropertyStore(Stgm.Read, out var properties) < 0 || properties is null)
        {
            return null;
        }

        if (properties.GetValue(FriendlyNameKey, out var value) < 0)
        {
            return null;
        }

        try
        {
            return value.VarType == PropVariant.VT_LPWSTR ? Marshal.PtrToStringUni(value.Pointer) : null;
        }
        finally
        {
            NativeMethods.PropVariantClear(ref value);
        }
    }
}
