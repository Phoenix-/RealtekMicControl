using System.Security.AccessControl;
using Microsoft.Win32;

namespace RealtekMicControl.Core;

/// <summary>
/// Where the microphone's mode-effect list lives, plus a copy of the driver's own order kept for
/// undoing the fix, so the fix logic can be tested without the registry.
/// </summary>
internal interface IEffectChainStore
{
    string[]? ReadChain();

    void WriteChain(string[] clsids);

    string[]? ReadBackup();

    /// <summary>Null removes the copy.</summary>
    void WriteBackup(string[]? clsids);
}

/// <summary>
/// The list is a <c>REG_MULTI_SZ</c> in the endpoint's <c>FxProperties</c>, seeded from the driver
/// INF. Reading it needs nothing; the driver's order is kept under <c>HKLM\SOFTWARE</c>, which needs
/// an administrator, as does restarting the audio service that applies a change.
/// </summary>
internal sealed class RegistryEffectChainStore(string endpointId) : IEffectChainStore
{
    /// <summary><c>PKEY_CompositeFX_ModeEffectClsid</c>.</summary>
    private const string ChainValue = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},14";

    private const string BackupKey = @"SOFTWARE\RealtekMicControl\DriverModeEffectOrder";

    private readonly string _endpointKey = EndpointKey(endpointId);

    private string FxPropertiesPath => FxPropertiesPathOf(endpointId);

    /// <summary>The endpoint id is <c>{0.0.1.00000000}.{guid}</c>; the registry key is the guid.</summary>
    private static string EndpointKey(string endpointId) => endpointId[(endpointId.LastIndexOf('.') + 1)..];

    /// <summary>The endpoint's <c>FxProperties</c> key under <c>HKLM</c>.</summary>
    public static string FxPropertiesPathOf(string endpointId) =>
        $@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture\{EndpointKey(endpointId)}\FxProperties";

    public string[]? ReadChain()
    {
        using var key = Registry.LocalMachine.OpenSubKey(FxPropertiesPath);
        return key?.GetValue(ChainValue) as string[];
    }

    public void WriteChain(string[] clsids)
    {
        // Ask for SetValue alone: the key grants that, but not the full write access the default
        // writable open requests.
        using var key = Registry.LocalMachine.OpenSubKey(
            FxPropertiesPath,
            RegistryKeyPermissionCheck.ReadWriteSubTree,
            RegistryRights.SetValue | RegistryRights.QueryValues)
            ?? throw new InvalidOperationException("The microphone's FxProperties key is missing.");
        key.SetValue(ChainValue, clsids, RegistryValueKind.MultiString);
    }

    public string[]? ReadBackup()
    {
        using var key = Registry.LocalMachine.OpenSubKey(BackupKey);
        return key?.GetValue(_endpointKey) as string[];
    }

    public void WriteBackup(string[]? clsids)
    {
        if (clsids is null)
        {
            using var existing = Registry.LocalMachine.OpenSubKey(BackupKey, writable: true);
            existing?.DeleteValue(_endpointKey, throwOnMissingValue: false);
            return;
        }

        using var key = Registry.LocalMachine.CreateSubKey(BackupKey);
        key.SetValue(_endpointKey, clsids, RegistryValueKind.MultiString);
    }
}
