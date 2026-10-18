using System.Security.AccessControl;
using Microsoft.Win32;
using RealtekMicControl.Core;

namespace MicProbe;

/// <summary>
/// The endpoint's root <c>FxProperties</c> key: the classic store the audio engine hands an APO at
/// initialization, separate from the per-APO <c>{clsid}\User</c> store Armoury Crate writes. On the
/// GA503QS it holds its own K2/K7 that nothing keeps in sync. BUILTIN\Users have SetValue on it.
/// </summary>
internal static class RootFxStore
{
    private const string Mode = "{2b24be42-a892-11dc-8314-0800200c9a66},2";
    private const string Flags = "{2b24be42-a892-11dc-8314-0800200c9a66},7";

    private static string KeyPath(string endpointId) =>
        $@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture\{endpointId[(endpointId.LastIndexOf('.') + 1)..]}\FxProperties";

    public static ForteState? Read(string endpointId)
    {
        using var key = Registry.LocalMachine.OpenSubKey(KeyPath(endpointId));
        return key?.GetValue(Mode) is int mode && key.GetValue(Flags) is int flags
            ? new ForteState((uint)mode, (uint)flags, null)
            : null;
    }

    public static void Write(string endpointId, ForteState state)
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            KeyPath(endpointId),
            RegistryKeyPermissionCheck.ReadWriteSubTree,
            RegistryRights.SetValue | RegistryRights.QueryValues)
            ?? throw new InvalidOperationException("Root FxProperties key not found.");
        key.SetValue(Mode, unchecked((int)state.ModeValue), RegistryValueKind.DWord);
        key.SetValue(Flags, unchecked((int)state.Flags), RegistryValueKind.DWord);
    }
}
