using System.Runtime.InteropServices;
using RealtekMicControl.Core.Interop;

namespace RealtekMicControl.Core;

/// <summary>
/// The user property store of one APO on one endpoint, opened through
/// <see cref="IAudioSystemEffectsPropertyStore"/>.
/// </summary>
internal sealed class ApoUserPropertyStore : IForteSettingsStore
{
    private readonly IPropertyStore _store;

    /// <summary>
    /// The value type each property was read with, so a write puts back the same type. The driver
    /// INF seeds these as REG_DWORD, which the property store hands out as VT_UI4; writing another
    /// type would make the store serialize a blob in its place.
    /// </summary>
    private readonly Dictionary<uint, ushort> _readTypes = [];

    private ApoUserPropertyStore(IPropertyStore store) => _store = store;

    public static ApoUserPropertyStore Open(IMMDevice device, Guid apoClsid, bool writable)
    {
        var instance = ActivateSystemEffectsStore(device, apoClsid);
        var hr = instance.OpenUserPropertyStore(writable ? Stgm.ReadWrite : Stgm.Read, out var store);
        NativeMethods.ThrowOnFailure(hr, "IAudioSystemEffectsPropertyStore.OpenUserPropertyStore");
        return new ApoUserPropertyStore(store!);
    }

    private static unsafe IAudioSystemEffectsPropertyStore ActivateSystemEffectsStore(IMMDevice device, Guid apoClsid)
    {
        // The activation parameter names the APO whose stores are wanted: a VT_CLSID PROPVARIANT.
        var clsid = apoClsid;
        var parameter = new PropVariant { VarType = PropVariant.VT_CLSID, Pointer = (nint)(&clsid) };

        var hr = device.Activate(
            NativeMethods.IID_IAudioSystemEffectsPropertyStore,
            NativeMethods.CLSCTX_ALL,
            (nint)(&parameter),
            out var instance);
        NativeMethods.ThrowOnFailure(hr, "IMMDevice.Activate(IAudioSystemEffectsPropertyStore)");

        try
        {
            return (IAudioSystemEffectsPropertyStore)ComInterop.Wrappers.GetOrCreateObjectForComInstance(instance, CreateObjectFlags.UniqueInstance);
        }
        finally
        {
            Marshal.Release(instance);
        }
    }

    public bool TryRead(uint propertyId, out uint value)
    {
        value = 0;
        var key = new PropertyKey(ForteProperties.FormatId, propertyId);
        if (_store.GetValue(key, out var variant) < 0)
        {
            return false;
        }

        try
        {
            if (variant.VarType is not (PropVariant.VT_UI4 or PropVariant.VT_I4))
            {
                return false;
            }

            value = variant.UInt32Value;
            _readTypes[propertyId] = variant.VarType;
            return true;
        }
        finally
        {
            NativeMethods.PropVariantClear(ref variant);
        }
    }

    public void Write(uint propertyId, uint value)
    {
        var variant = new PropVariant
        {
            VarType = _readTypes.GetValueOrDefault(propertyId, PropVariant.VT_UI4),
            UInt32Value = value,
        };
        var key = new PropertyKey(ForteProperties.FormatId, propertyId);
        NativeMethods.ThrowOnFailure(_store.SetValue(key, variant), $"IPropertyStore.SetValue(pid {propertyId})");
    }

    public void Commit() => NativeMethods.ThrowOnFailure(_store.Commit(), "IPropertyStore.Commit");
}
