using Microsoft.Win32;

namespace RealtekMicControl.App;

/// <summary>
/// The app's own preferences, in <c>HKCU\Software\RealtekMicControl</c>: an unpackaged app has no
/// <c>ApplicationData</c>, and a registry value needs no file next to an exe that may sit in a
/// read-only folder.
/// </summary>
internal static class AppSettings
{
    private const string KeyPath = @"Software\RealtekMicControl";

    public static bool CloseOnFocusLoss
    {
        get => Read(nameof(CloseOnFocusLoss)) == 1;
        set => Write(nameof(CloseOnFocusLoss), value ? 1 : 0);
    }

    private static int? Read(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(name) as int?;
    }

    private static void Write(string name, int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue(name, value, RegistryValueKind.DWord);
    }
}
