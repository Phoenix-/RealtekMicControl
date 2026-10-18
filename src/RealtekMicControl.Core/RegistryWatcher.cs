using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace RealtekMicControl.Core;

/// <summary>
/// Calls back, on a worker thread, whenever a value directly under one of the given
/// <c>HKLM</c> keys changes. Keys that do not exist are skipped.
/// </summary>
/// <remarks>
/// Expect a lot of calls: on the GA503QS <c>FMService64</c> rewrites K9001 about twice a
/// second, so a consumer should compare what it reads with what it shows.
/// </remarks>
public sealed partial class RegistryWatcher : IDisposable
{
    private readonly ManualResetEvent _stop = new(false);
    private readonly Thread _thread;

    public RegistryWatcher(IEnumerable<string> localMachineKeys, Action changed)
    {
        var keys = localMachineKeys.Select(path => Registry.LocalMachine.OpenSubKey(path)).OfType<RegistryKey>().ToList();
        _thread = new Thread(() => Watch(keys, changed)) { IsBackground = true, Name = "RegistryWatcher" };
        _thread.Start();
    }

    private void Watch(List<RegistryKey> keys, Action changed)
    {
        var events = keys.Select(_ => new AutoResetEvent(false)).ToList();
        try
        {
            for (var i = 0; i < keys.Count; i++)
            {
                Arm(keys[i], events[i]);
            }

            WaitHandle[] handles = [_stop, .. events];
            while (true)
            {
                var signalled = WaitHandle.WaitAny(handles);
                if (signalled == 0)
                {
                    return;
                }

                // A notification is one-shot: arm it again before reading, so a change made
                // while the callback runs is not lost.
                Arm(keys[signalled - 1], events[signalled - 1]);
                changed();
            }
        }
        finally
        {
            events.ForEach(e => e.Dispose());
            keys.ForEach(k => k.Dispose());
        }
    }

    private static void Arm(RegistryKey key, AutoResetEvent signal)
    {
        var error = RegNotifyChangeKeyValue(key.Handle, watchSubtree: false, REG_NOTIFY_CHANGE_LAST_SET,
            signal.SafeWaitHandle, asynchronous: true);
        if (error != 0)
        {
            throw new Win32Exception(error, "RegNotifyChangeKeyValue failed");
        }
    }

    public void Dispose()
    {
        _stop.Set();
        _thread.Join(TimeSpan.FromSeconds(2));
        _stop.Dispose();
    }

    private const uint REG_NOTIFY_CHANGE_LAST_SET = 0x4;

    [LibraryImport("advapi32.dll")]
    private static partial int RegNotifyChangeKeyValue(
        SafeRegistryHandle key,
        [MarshalAs(UnmanagedType.Bool)] bool watchSubtree,
        uint filter,
        SafeWaitHandle signal,
        [MarshalAs(UnmanagedType.Bool)] bool asynchronous);
}
