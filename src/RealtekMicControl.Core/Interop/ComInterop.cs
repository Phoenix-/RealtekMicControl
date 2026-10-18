using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace RealtekMicControl.Core.Interop;

internal static class ComInterop
{
    /// <summary>Turns raw COM pointers into the source-generated interfaces.</summary>
    public static readonly StrategyBasedComWrappers Wrappers = new();

    public static IMMDeviceEnumerator CreateDeviceEnumerator()
    {
        var hr = NativeMethods.CoCreateInstance(
            NativeMethods.CLSID_MMDeviceEnumerator,
            0,
            NativeMethods.CLSCTX_INPROC_SERVER,
            NativeMethods.IID_IMMDeviceEnumerator,
            out var instance);
        NativeMethods.ThrowOnFailure(hr, "CoCreateInstance(MMDeviceEnumerator)");

        try
        {
            return (IMMDeviceEnumerator)Wrappers.GetOrCreateObjectForComInstance(instance, CreateObjectFlags.UniqueInstance);
        }
        finally
        {
            Marshal.Release(instance);
        }
    }

    /// <summary>
    /// Joins the multithreaded apartment unless the thread already lives in one. A console host
    /// calls this once; a UI thread is already in its STA and gets RPC_E_CHANGED_MODE, which is
    /// fine -- Core Audio works from either.
    /// </summary>
    public static void EnsureInitialized() => NativeMethods.CoInitializeEx(0, NativeMethods.COINIT_MULTITHREADED);
}
