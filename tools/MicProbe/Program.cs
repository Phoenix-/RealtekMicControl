using System.Globalization;
using System.Text;
using RealtekMicControl.Core;

namespace MicProbe;

/// <summary>
/// Acoustic test bench for the Fortemedia microphone modes. Play the same sound from a phone at
/// each position in turn; for every position the probe cycles through the settings and records a
/// few seconds of each, twice at the same time: the processed stream an app gets, and the raw
/// stream that bypasses every effect. The gain of processed over raw cancels out whatever the
/// source happened to be doing during that take, so takes compare by the numbers.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var options = Options.Parse(args);
        if (options is null)
        {
            Console.WriteLine("""
                MicProbe [--positions front,left,right,back] [--settings off,cardioid,stereo,omni]
                         [--configs default,default-reopen,comms,comms-reopen]
                         [--seconds 4] [--settle 1.5] [--ns keep|off|on] [--no-raw] [--out DIR]

                --configs  how the processed stream is opened, each run in turn per position:
                           default        DEFAULT processing mode (an ordinary app), one stream
                                          kept open while the settings change -- tests "live"
                           comms          SPEECH processing mode (a call app: Discord, Teams)
                           *-reopen       a new stream for every take
                --no-raw   do not record the raw reference stream alongside

                Levels are measured in the 150-7000 Hz band the test signal covers, so desk rumble
                and fan noise below it do not skew the comparison.
                """);
            return 1;
        }

        var mic = ForteMicrophone.FindAll().FirstOrDefault();
        if (mic is null)
        {
            Console.Error.WriteLine("No Fortemedia microphone found.");
            return 2;
        }

        var original = mic.Read();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = false;
            Restore(mic, original);
        };

        try
        {
            return Run(mic, options);
        }
        finally
        {
            Restore(mic, original);
        }
    }

    private static int Run(ForteMicrophone mic, Options options)
    {
        Directory.CreateDirectory(options.OutputDirectory);
        if (options.NoiseSuppression is { } ns)
        {
            mic.SetNoiseSuppression(ns);
        }

        CaptureStream? raw = null;
        if (options.Raw)
        {
            try
            {
                raw = CaptureStream.Open(mic.Id, raw: true);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Raw reference not available ({e.Message}); recording processed only.");
            }
        }

        Console.WriteLine($"Microphone: {mic.Name} -- {CaptureStream.DescribeEndpointVolume(mic.Id)}");
        Console.WriteLine($"Noise suppression during the run: {Describe(mic.Read().NoiseSuppression)}");
        Console.WriteLine($"Configs: {string.Join(", ", options.Configs.Select(c => c.Name))}; " +
                          $"takes {options.Seconds:0.#} s after {options.Settle:0.#} s settle" +
                          $"{(raw is null ? string.Empty : "; raw reference alongside")}");
        Console.WriteLine($"Output: {Path.GetFullPath(options.OutputDirectory)}");

        var takes = new List<Take>();
        var csv = new StringBuilder("position,config,setting,processed_db,raw_front_db,raw_rear_db,gain_db,gated_pct,raw_corr01\n");

        foreach (var position in options.Positions)
        {
            Console.WriteLine();
            Console.Write($"[{position}] Put the sound source there, start it, then press Enter (s = skip, q = quit): ");
            var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (answer == "q")
            {
                break;
            }

            if (answer == "s")
            {
                continue;
            }

            foreach (var config in options.Configs)
            {
                Console.WriteLine($" {config.Name}:");
                using var live = config.Reopen ? null : CaptureStream.Open(mic.Id, category: config.Category);
                foreach (var setting in options.Settings)
                {
                    mic.Apply(setting);
                    using var perTake = config.Reopen ? CaptureStream.Open(mic.Id, category: config.Category) : null;
                    var source = perTake ?? live!;
                    CaptureStream[] streams = raw is null ? [source] : [source, raw];

                    CaptureStream.Record(TimeSpan.FromSeconds(options.Settle), streams);
                    var recorded = CaptureStream.Record(TimeSpan.FromSeconds(options.Seconds), streams);

                    var take = Take.From(position, config.Name, setting, recorded[0], raw is null ? null : recorded[1], source.SampleRate);
                    takes.Add(take);
                    var name = $"{position}-{config.Name}-{setting.ToString().ToLowerInvariant()}";
                    Wav.WriteFloat(Path.Combine(options.OutputDirectory, $"{name}.wav"), recorded[0], source.SampleRate);
                    if (raw is not null)
                    {
                        Wav.WriteFloat(Path.Combine(options.OutputDirectory, $"{name}-raw.wav"), recorded[1], raw.SampleRate);
                    }

                    csv.Append(CultureInfo.InvariantCulture,
                        $"{position},{config.Name},{setting},{take.ProcessedDb:0.00},{take.RawFrontDb:0.00},{take.RawRearDb:0.00},{take.GainDb:0.00},{take.GatedPercent:0},{take.RawCorrelation:0.000}\n");
                    Console.WriteLine($"   {setting,-9} {take.Describe()}");
                }
            }
        }

        raw?.Dispose();
        File.WriteAllText(Path.Combine(options.OutputDirectory, "results.csv"), csv.ToString());
        PrintSummary(takes, options);
        return 0;
    }

    private static void PrintSummary(List<Take> takes, Options options)
    {
        if (takes.Count == 0)
        {
            return;
        }

        if (takes.Any(t => t.GainDb is not null))
        {
            Table("Gain of processed over the raw front mic pair, dB, 150-7000 Hz:", takes, options, t => t.GainDb is { } g ? $"{g:+0.0;-0.0}" : "-");
        }

        Table("Processed level, dBFS, 150-7000 Hz:", takes, options, t => $"{t.ProcessedDb:0.0}");
        Table("Gated, % of 100 ms windows below -100 dBFS:", takes, options, t => $"{t.GatedPercent:0}%");
    }

    private static void Table(string title, List<Take> takes, Options options, Func<Take, string> cell)
    {
        Console.WriteLine();
        Console.WriteLine(title);
        Console.Write($"{"",-24}");
        foreach (var setting in options.Settings)
        {
            Console.Write($"{setting,10}");
        }

        Console.WriteLine();
        foreach (var row in takes.Select(t => (t.Position, t.Config)).Distinct())
        {
            Console.Write($"{$"{row.Position}/{row.Config}",-24}");
            foreach (var setting in options.Settings)
            {
                var take = takes.FirstOrDefault(t => t.Position == row.Position && t.Config == row.Config && t.Setting == setting);
                Console.Write($"{(take is null ? "-" : cell(take)),10}");
            }

            Console.WriteLine();
        }
    }

    private static void Restore(ForteMicrophone mic, ForteState original)
    {
        try
        {
            mic.Update(_ => original);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Could not restore the original settings: {e.Message}");
        }
    }

    private static string Describe(bool? value) => value switch { true => "on", false => "off", null => "n/a" };
}

internal sealed record Take(
    string Position,
    string Config,
    MicSetting Setting,
    double ProcessedDb,
    double? RawFrontDb,
    double? RawRearDb,
    double GatedPercent,
    double? RawCorrelation)
{
    public double? GainDb => RawFrontDb is { } raw ? ProcessedDb - raw : null;

    /// <remarks>
    /// Raw channels on the GA503QS: 0 and 1 are the front-facing pair, 2 and 3 a single rear-facing
    /// mic delivered twice. The processed stream is mono in both channels, so channel 0 stands for it.
    /// </remarks>
    public static Take From(string position, string config, MicSetting setting, float[][] processed, float[][]? raw, int sampleRate)
    {
        var p = Band.Filter(processed[0], sampleRate);
        double? front = null, rear = null, corr = null;
        if (raw is { Length: >= 2 })
        {
            var r0 = Band.Filter(raw[0], sampleRate);
            var r1 = Band.Filter(raw[1], sampleRate);
            front = Db(Math.Sqrt((MeanSquare(r0) + MeanSquare(r1)) / 2));
            corr = Correlate(r0, r1);
            if (raw.Length >= 3)
            {
                rear = Db(Math.Sqrt(MeanSquare(Band.Filter(raw[2], sampleRate))));
            }
        }

        return new Take(position, config, setting, Db(Math.Sqrt(MeanSquare(p))), front, rear, Gated(processed[0], sampleRate / 10), corr);
    }

    public string Describe()
    {
        var text = $"processed {ProcessedDb,6:0.0} dB  gated {GatedPercent,3:0}%";
        if (RawFrontDb is { } front)
        {
            text += $"  |  raw front {front,6:0.0}  rear {RawRearDb,6:0.0}  gain {GainDb,5:+0.0;-0.0} dB  raw corr {RawCorrelation,4:0.00}";
        }

        return text;
    }

    private static double MeanSquare(float[] x) => x.Average(s => (double)s * s);

    private static double Gated(float[] samples, int window)
    {
        int windows = 0, silent = 0;
        for (var i = 0; i + window <= samples.Length; i += window, windows++)
        {
            double sum = 0;
            for (var j = i; j < i + window; j++)
            {
                sum += samples[j] * (double)samples[j];
            }

            if (Db(Math.Sqrt(sum / window)) < -100)
            {
                silent++;
            }
        }

        return windows == 0 ? 0 : 100.0 * silent / windows;
    }

    private static double Db(double linear) => linear > 0 ? 20 * Math.Log10(linear) : -150;

    private static double Correlate(float[] a, float[] b)
    {
        double ab = 0, aa = 0, bb = 0;
        var n = Math.Min(a.Length, b.Length);
        for (var i = 0; i < n; i++)
        {
            ab += a[i] * b[i];
            aa += a[i] * a[i];
            bb += b[i] * b[i];
        }

        return aa > 0 && bb > 0 ? ab / Math.Sqrt(aa * bb) : 0;
    }
}

/// <summary>150-7000 Hz band-pass: two 2nd-order Butterworth biquads (RBJ cookbook), high-pass then low-pass.</summary>
internal static class Band
{
    public static float[] Filter(float[] x, int sampleRate) =>
        Biquad(Biquad(x, sampleRate, 150, highPass: true), sampleRate, 7000, highPass: false);

    private static float[] Biquad(float[] x, int sampleRate, double cutoff, bool highPass)
    {
        var w0 = 2 * Math.PI * cutoff / sampleRate;
        var alpha = Math.Sin(w0) / (2 * Math.Sqrt(0.5));
        var cos = Math.Cos(w0);
        double b0, b1, b2;
        if (highPass)
        {
            b0 = (1 + cos) / 2;
            b1 = -(1 + cos);
            b2 = (1 + cos) / 2;
        }
        else
        {
            b0 = (1 - cos) / 2;
            b1 = 1 - cos;
            b2 = (1 - cos) / 2;
        }

        var a0 = 1 + alpha;
        var a1 = -2 * cos;
        var a2 = 1 - alpha;
        var y = new float[x.Length];
        double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
        for (var i = 0; i < x.Length; i++)
        {
            var yi = ((b0 * x[i]) + (b1 * x1) + (b2 * x2) - (a1 * y1) - (a2 * y2)) / a0;
            x2 = x1;
            x1 = x[i];
            y2 = y1;
            y1 = yi;
            y[i] = (float)yi;
        }

        return y;
    }
}

internal sealed record StreamConfig(string Name, int Category, bool Reopen)
{
    public static StreamConfig Parse(string name) => name switch
    {
        "default" => new(name, CaptureStream.CategoryOther, false),
        "default-reopen" => new(name, CaptureStream.CategoryOther, true),
        "comms" => new(name, CaptureStream.CategoryCommunications, false),
        "comms-reopen" => new(name, CaptureStream.CategoryCommunications, true),
        _ => throw new ArgumentException(name),
    };
}

internal sealed record Options(
    string[] Positions,
    MicSetting[] Settings,
    StreamConfig[] Configs,
    double Seconds,
    double Settle,
    bool? NoiseSuppression,
    bool Raw,
    string OutputDirectory)
{
    public static Options? Parse(string[] args)
    {
        var positions = new[] { "front", "left", "right", "back" };
        var settings = new[] { MicSetting.Off, MicSetting.Cardioid, MicSetting.Stereo, MicSetting.Omni };
        var configs = new[] { StreamConfig.Parse("default") };
        double seconds = 4, settle = 1.5;
        // Left alone by default: K9001 written through the property store snaps back within a
        // second (see notes/), so forcing it off would only pretend to.
        bool? ns = null;
        var raw = true;
        var output = Path.Combine("spike", $"probe-{DateTime.Now:yyyyMMdd-HHmmss}");

        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            try
            {
                switch (args[i])
                {
                    case "--positions": positions = Next().Split(',', StringSplitOptions.RemoveEmptyEntries); break;
                    case "--settings": settings = Next().Split(',').Select(s => Enum.Parse<MicSetting>(s, ignoreCase: true)).ToArray(); break;
                    case "--configs": configs = Next().Split(',').Select(StreamConfig.Parse).ToArray(); break;
                    case "--seconds": seconds = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--settle": settle = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--ns": ns = Next() switch { "on" => true, "off" => false, "keep" => null, var v => throw new ArgumentException(v) }; break;
                    case "--no-raw": raw = false; break;
                    case "--out": output = Next(); break;
                    default: return null;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        return new Options(positions, settings, configs, seconds, settle, ns, raw, output);
    }
}

internal static class Wav
{
    /// <summary>32-bit float WAV, interleaved.</summary>
    public static void WriteFloat(string path, float[][] channels, int sampleRate)
    {
        var frames = channels[0].Length;
        var count = channels.Length;
        using var writer = new BinaryWriter(File.Create(path));
        var dataBytes = frames * count * 4;
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)3);
        writer.Write((short)count);
        writer.Write(sampleRate);
        writer.Write(sampleRate * count * 4);
        writer.Write((short)(count * 4));
        writer.Write((short)32);
        writer.Write("data"u8);
        writer.Write(dataBytes);
        for (var f = 0; f < frames; f++)
        {
            for (var c = 0; c < count; c++)
            {
                writer.Write(channels[c][f]);
            }
        }
    }
}
