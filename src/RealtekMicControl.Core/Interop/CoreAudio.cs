using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace RealtekMicControl.Core.Interop;

// Core Audio (mmdeviceapi.h) declared for source-generated COM. The classic [ComImport] interop
// is not available under Native AOT; [GeneratedComInterface] is. Only the vtable slots this tool
// calls are declared -- trailing slots may be left out, slots in the middle may not.

internal enum EDataFlow
{
    Render = 0,
    Capture = 1,
}

internal static class DeviceState
{
    public const uint Active = 0x1;
}

internal static class Stgm
{
    public const uint Read = 0x0;
    public const uint ReadWrite = 0x2;
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
internal partial interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection? devices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, int role, out IMMDevice? device);

    [PreserveSig]
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice? device);
}

[GeneratedComInterface]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
internal partial interface IMMDeviceCollection
{
    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int Item(uint index, out IMMDevice? device);
}

[GeneratedComInterface]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
internal partial interface IMMDevice
{
    [PreserveSig]
    int Activate(in Guid iid, uint clsCtx, nint activationParams, out nint instance);

    [PreserveSig]
    int OpenPropertyStore(uint stgmAccess, out IPropertyStore? properties);

    /// <remarks>The string is CoTaskMem-allocated and owned by the caller.</remarks>
    [PreserveSig]
    int GetId(out nint id);
}

[GeneratedComInterface]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
internal partial interface IPropertyStore
{
    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int GetAt(uint index, out PropertyKey key);

    [PreserveSig]
    int GetValue(in PropertyKey key, out PropVariant value);

    [PreserveSig]
    int SetValue(in PropertyKey key, in PropVariant value);

    [PreserveSig]
    int Commit();
}

/// <summary>
/// Windows 11's door into an APO's own property stores, the <c>FxProperties\{apo}\Default|User</c>
/// keys. Obtained through <see cref="IMMDevice.Activate"/> with the APO's CLSID passed in a
/// <c>VT_CLSID</c> PROPVARIANT. This is what Armoury Crate itself uses ("V3 APO Open User Property
/// Store" in its log), and it needs no elevation, unlike writing the registry keys directly.
/// </summary>
[GeneratedComInterface]
[Guid("302AE7F9-D7E0-43E4-971B-1F8293613D2A")]
internal partial interface IAudioSystemEffectsPropertyStore
{
    [PreserveSig]
    int OpenDefaultPropertyStore(uint stgmAccess, out IPropertyStore? store);

    [PreserveSig]
    int OpenUserPropertyStore(uint stgmAccess, out IPropertyStore? store);
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct PropertyKey(Guid formatId, uint propertyId)
{
    public readonly Guid FormatId = formatId;
    public readonly uint PropertyId = propertyId;
}

/// <summary>
/// The parts of PROPVARIANT this tool reads and writes: a type tag and an 8-byte payload slot.
/// 24 bytes is its size on x64, which is the only platform built.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PropVariant
{
    public const ushort VT_I4 = 3;
    public const ushort VT_UI4 = 19;
    public const ushort VT_LPWSTR = 31;
    public const ushort VT_CLSID = 72;

    [FieldOffset(0)] public ushort VarType;
    [FieldOffset(8)] public uint UInt32Value;
    [FieldOffset(8)] public nint Pointer;
}

internal static partial class NativeMethods
{
    public static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    public static readonly Guid IID_IMMDeviceEnumerator = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    public static readonly Guid IID_IAudioSystemEffectsPropertyStore = new("302AE7F9-D7E0-43E4-971B-1F8293613D2A");

    public const uint CLSCTX_INPROC_SERVER = 0x1;
    public const uint CLSCTX_ALL = 0x17;
    public const uint COINIT_MULTITHREADED = 0x0;

    [LibraryImport("ole32.dll")]
    public static partial int CoCreateInstance(in Guid clsid, nint outer, uint clsContext, in Guid iid, out nint instance);

    [LibraryImport("ole32.dll")]
    public static partial int CoInitializeEx(nint reserved, uint coInit);

    [LibraryImport("ole32.dll")]
    public static partial int PropVariantClear(ref PropVariant value);

    public static void ThrowOnFailure(int hr, string what)
    {
        if (hr < 0)
        {
            throw new COMException($"{what} failed (0x{hr:X8})", hr);
        }
    }
}
