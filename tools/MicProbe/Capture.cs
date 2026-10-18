using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using RealtekMicControl.Core.Interop;

namespace MicProbe;

[GeneratedComInterface]
[Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
internal partial interface IAudioClient
{
    [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, nint format, nint sessionGuid);
    [PreserveSig] int GetBufferSize(out uint frames);
    [PreserveSig] int GetStreamLatency(out long latency);
    [PreserveSig] int GetCurrentPadding(out uint padding);
    [PreserveSig] int IsFormatSupported(int shareMode, nint format, out nint closest);
    [PreserveSig] int GetMixFormat(out nint format);
    [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
    [PreserveSig] int Start();
    [PreserveSig] int Stop();
    [PreserveSig] int Reset();
    [PreserveSig] int SetEventHandle(nint handle);
    [PreserveSig] int GetService(in Guid iid, out nint service);
}

[GeneratedComInterface]
[Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317")]
internal partial interface IAudioCaptureClient
{
    [PreserveSig] int GetBuffer(out nint data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
    [PreserveSig] int ReleaseBuffer(uint frames);
    [PreserveSig] int GetNextPacketSize(out uint frames);
}

[GeneratedComInterface]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
internal partial interface IAudioEndpointVolume
{
    [PreserveSig] int RegisterControlChangeNotify(nint notify);
    [PreserveSig] int UnregisterControlChangeNotify(nint notify);
    [PreserveSig] int GetChannelCount(out uint count);
    [PreserveSig] int SetMasterVolumeLevel(float levelDb, in Guid context);
    [PreserveSig] int SetMasterVolumeLevelScalar(float level, in Guid context);
    [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, in Guid context);
    [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, in Guid context);
    [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
    [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
    [PreserveSig] int SetMute(int mute, in Guid context);
    [PreserveSig] int GetMute(out int mute);
}

/// <summary>
/// A shared-mode WASAPI capture stream in the endpoint's mix format. Shared mode with no special
/// category means the DEFAULT processing mode, so the samples have been through the endpoint's
/// mode effects -- the Fortemedia APO among them -- exactly as an ordinary app would get them.
/// </summary>
internal sealed unsafe class CaptureStream : IDisposable
{
    private static readonly Guid IID_IAudioClient = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    private static readonly Guid IID_IAudioCaptureClient = new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
    private static readonly Guid SubtypeFloat = new("00000003-0000-0010-8000-00AA00389B71");

    private const int SharedMode = 0;
    private const uint SilentFlag = 0x2;

    private readonly IAudioClient _client;
    private readonly IAudioCaptureClient _capture;
    private readonly bool _isFloat;
    private readonly int _bitsPerSample;
    private readonly int _blockAlign;

    public int Channels { get; }

    public int SampleRate { get; }

    public string FormatDescription => $"{SampleRate} Hz, {Channels} ch, {_bitsPerSample}-bit {(_isFloat ? "float" : "PCM")}";

    private CaptureStream(IAudioClient client, IAudioCaptureClient capture, int channels, int sampleRate, bool isFloat, int bits, int blockAlign)
    {
        _client = client;
        _capture = capture;
        Channels = channels;
        SampleRate = sampleRate;
        _isFloat = isFloat;
        _bitsPerSample = bits;
        _blockAlign = blockAlign;
    }

    public static CaptureStream Open(string endpointId)
    {
        ComInterop.EnsureInitialized();
        var enumerator = ComInterop.CreateDeviceEnumerator();
        NativeMethods.ThrowOnFailure(enumerator.GetDevice(endpointId, out var device), "GetDevice");
        NativeMethods.ThrowOnFailure(device!.Activate(IID_IAudioClient, NativeMethods.CLSCTX_ALL, 0, out var clientPtr), "Activate(IAudioClient)");
        var client = Wrap<IAudioClient>(clientPtr);

        NativeMethods.ThrowOnFailure(client.GetMixFormat(out var format), "GetMixFormat");
        try
        {
            var tag = *(ushort*)format;
            var channels = *(ushort*)(format + 2);
            var sampleRate = *(int*)(format + 4);
            var blockAlign = *(ushort*)(format + 12);
            var bits = *(ushort*)(format + 14);
            var isFloat = tag == 3 || (tag == 0xFFFE && *(Guid*)(format + 24) == SubtypeFloat);

            // 200 ms of buffer is plenty for 5 ms polling.
            NativeMethods.ThrowOnFailure(client.Initialize(SharedMode, 0, 2_000_000, 0, format, 0), "IAudioClient.Initialize");
            NativeMethods.ThrowOnFailure(client.GetService(IID_IAudioCaptureClient, out var capturePtr), "GetService(IAudioCaptureClient)");
            var capture = Wrap<IAudioCaptureClient>(capturePtr);
            NativeMethods.ThrowOnFailure(client.Start(), "IAudioClient.Start");
            return new CaptureStream(client, capture, channels, sampleRate, isFloat, bits, blockAlign);
        }
        finally
        {
            Marshal.FreeCoTaskMem(format);
        }
    }

    /// <summary>Windows' own volume and mute for the endpoint, so a muted mic is not mistaken for a mode.</summary>
    public static string DescribeEndpointVolume(string endpointId)
    {
        var enumerator = ComInterop.CreateDeviceEnumerator();
        NativeMethods.ThrowOnFailure(enumerator.GetDevice(endpointId, out var device), "GetDevice");
        var iid = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
        NativeMethods.ThrowOnFailure(device!.Activate(iid, NativeMethods.CLSCTX_ALL, 0, out var pointer), "Activate(IAudioEndpointVolume)");
        var volume = Wrap<IAudioEndpointVolume>(pointer);
        volume.GetMasterVolumeLevelScalar(out var scalar);
        volume.GetMasterVolumeLevel(out var db);
        volume.GetMute(out var mute);
        return $"volume {scalar * 100:0}% ({db:+0.0;-0.0} dB), {(mute != 0 ? "MUTED" : "not muted")}";
    }

    private static T Wrap<T>(nint pointer)
    {
        try
        {
            return (T)ComInterop.Wrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.UniqueInstance);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    /// <summary>Captures <paramref name="duration"/> of audio, one array per channel.</summary>
    public float[][] Record(TimeSpan duration)
    {
        var wanted = (int)(duration.TotalSeconds * SampleRate);
        var channels = Enumerable.Range(0, Channels).Select(_ => new float[wanted]).ToArray();
        var have = 0;

        while (have < wanted)
        {
            Thread.Sleep(5);
            while (_capture.GetNextPacketSize(out var packet) >= 0 && packet > 0)
            {
                NativeMethods.ThrowOnFailure(_capture.GetBuffer(out var data, out var frames, out var flags, out _, out _), "GetBuffer");
                var take = (int)Math.Min(frames, (uint)(wanted - have));
                for (var f = 0; f < take; f++)
                {
                    var frame = (byte*)data + (f * _blockAlign);
                    for (var c = 0; c < Channels; c++)
                    {
                        channels[c][have + f] = (flags & SilentFlag) != 0 ? 0f : ReadSample(frame, c);
                    }
                }

                have += take;
                _capture.ReleaseBuffer(frames);
                if (have >= wanted)
                {
                    break;
                }
            }
        }

        return channels;
    }

    private float ReadSample(byte* frame, int channel)
    {
        var bytes = _bitsPerSample / 8;
        var p = frame + (channel * bytes);
        return (_isFloat, _bitsPerSample) switch
        {
            (true, 32) => *(float*)p,
            (false, 16) => *(short*)p / 32768f,
            (false, 24) => ((p[0] << 8) | (p[1] << 16) | (p[2] << 24)) / 2147483648f,
            (false, 32) => *(int*)p / 2147483648f,
            _ => throw new NotSupportedException($"Sample format {FormatDescription} is not handled."),
        };
    }

    public void Dispose() => _client.Stop();
}
