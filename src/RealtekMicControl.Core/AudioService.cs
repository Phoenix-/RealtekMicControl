using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RealtekMicControl.Core;

/// <summary>
/// The Windows Audio service (Audiosrv). It reads the endpoints' effect lists when it starts, so a
/// changed list takes effect after a restart; restarting the endpoint builder is not needed.
/// </summary>
public static partial class AudioService
{
    private const string Name = "Audiosrv";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Stops the services that depend on Audiosrv (on ASUS laptops the Aura lighting service, and
    /// the agent activation runtime), restarts it, then starts those dependents again. Needs an
    /// administrator. Sound stops for a few seconds.
    /// </summary>
    public static void Restart()
    {
        using var manager = Open(OpenSCManagerW(null, null, SC_MANAGER_CONNECT), "OpenSCManager");
        using var audio = Open(
            OpenServiceW(manager.Handle, Name, SERVICE_STOP | SERVICE_START | SERVICE_QUERY_STATUS | SERVICE_ENUMERATE_DEPENDENTS),
            $"OpenService({Name})");

        // In the order the service manager gives them: dependents of dependents first.
        var dependents = RunningDependents(audio.Handle);
        foreach (var dependent in dependents)
        {
            using var service = Open(OpenServiceW(manager.Handle, dependent, SERVICE_STOP | SERVICE_QUERY_STATUS), $"OpenService({dependent})");
            Stop(service.Handle, dependent);
        }

        Stop(audio.Handle, Name);
        Start(audio.Handle, Name);

        // Best effort: a dependent that does not come back is not a reason to fail the restart.
        foreach (var dependent in Enumerable.Reverse(dependents))
        {
            var handle = OpenServiceW(manager.Handle, dependent, SERVICE_START);
            if (handle != 0)
            {
                StartServiceW(handle, 0, 0);
                CloseServiceHandle(handle);
            }
        }
    }

    private static List<string> RunningDependents(nint service)
    {
        EnumDependentServicesW(service, SERVICE_ACTIVE, 0, 0, out var needed, out _);
        if (needed == 0)
        {
            return [];
        }

        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!EnumDependentServicesW(service, SERVICE_ACTIVE, buffer, needed, out _, out var count))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "EnumDependentServices failed");
            }

            // ENUM_SERVICE_STATUSW: service name, display name, then a SERVICE_STATUS.
            var stride = 2 * nint.Size + Marshal.SizeOf<ServiceStatus>();
            stride = (stride + nint.Size - 1) / nint.Size * nint.Size;
            var names = new List<string>((int)count);
            for (var i = 0; i < count; i++)
            {
                names.Add(Marshal.PtrToStringUni(Marshal.ReadIntPtr(buffer, i * stride)) ?? string.Empty);
            }

            return names;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void Stop(nint service, string name)
    {
        if (!ControlService(service, SERVICE_CONTROL_STOP, out _) && Marshal.GetLastPInvokeError() != ERROR_SERVICE_NOT_ACTIVE)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Stopping {name} failed");
        }

        WaitFor(service, name, SERVICE_STOPPED);
    }

    private static void Start(nint service, string name)
    {
        if (!StartServiceW(service, 0, 0) && Marshal.GetLastPInvokeError() != ERROR_SERVICE_ALREADY_RUNNING)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Starting {name} failed");
        }

        WaitFor(service, name, SERVICE_RUNNING);
    }

    private static void WaitFor(nint service, string name, uint state)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            if (!QueryServiceStatus(service, out var status))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Querying {name} failed");
            }

            if (status.CurrentState == state)
            {
                return;
            }

            if (clock.Elapsed > Timeout)
            {
                throw new InvalidOperationException($"{name} did not {(state == SERVICE_RUNNING ? "start" : "stop")} within {Timeout.TotalSeconds:0} s.");
            }

            Thread.Sleep(200);
        }
    }

    private static ServiceHandle Open(nint handle, string what) =>
        handle != 0 ? new ServiceHandle(handle) : throw new Win32Exception(Marshal.GetLastPInvokeError(), $"{what} failed");

    private readonly struct ServiceHandle(nint handle) : IDisposable
    {
        public nint Handle { get; } = handle;

        public void Dispose() => CloseServiceHandle(Handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
    }

    private const uint SC_MANAGER_CONNECT = 0x0001;
    private const uint SERVICE_QUERY_STATUS = 0x0004;
    private const uint SERVICE_ENUMERATE_DEPENDENTS = 0x0008;
    private const uint SERVICE_START = 0x0010;
    private const uint SERVICE_STOP = 0x0020;
    private const uint SERVICE_ACTIVE = 0x1;
    private const uint SERVICE_CONTROL_STOP = 0x1;
    private const uint SERVICE_STOPPED = 0x1;
    private const uint SERVICE_RUNNING = 0x4;
    private const int ERROR_SERVICE_ALREADY_RUNNING = 1056;
    private const int ERROR_SERVICE_NOT_ACTIVE = 1062;

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint OpenSCManagerW(string? machine, string? database, uint access);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint OpenServiceW(nint manager, string name, uint access);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseServiceHandle(nint handle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ControlService(nint service, uint control, out ServiceStatus status);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool StartServiceW(nint service, uint argc, nint argv);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryServiceStatus(nint service, out ServiceStatus status);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumDependentServicesW(nint service, uint state, nint buffer, uint size, out uint needed, out uint count);
}
