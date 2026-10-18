using System.Globalization;
using System.Text;
using RealtekMicControl.Core;

namespace MicProbe;

/// <summary>
/// Acoustic test bench for the Fortemedia microphone modes. Play the same sound from a phone at
/// each position in turn; for every position the probe cycles through the settings, records a few
/// seconds of each and prints per-channel levels, so the modes can be compared by the numbers
/// instead of by ear. WAV files of every take are kept for listening.
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
                         [--seconds 4] [--settle 1.5] [--ns off|on|keep] [--reopen] [--out DIR]

                --reopen  open a new capture stream for every take (default: one stream for the
                          whole run, which also shows whether a change applies live)
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

        var stream = options.Reopen ? null : CaptureStream.Open(mic.Id);
        Console.WriteLine($"Microphone: {mic.Name} -- {CaptureStream.DescribeEndpointVolume(mic.Id)}");
        Console.WriteLine($"Noise suppression during the run: {Describe(mic.Read().NoiseSuppression)}");
        Console.WriteLine($"Takes: {options.Seconds:0.#} s each after {options.Settle:0.#} s settle, " +
                          $"{(options.Reopen ? "new stream per take" : "one stream for the whole run")}");
        Console.WriteLine($"Output: {Path.GetFullPath(options.OutputDirectory)}");

        var results = new List<Take>();
        var csv = new StringBuilder("position,setting,channel,rms_dbfs,peak_dbfs\n");

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

            foreach (var setting in options.Settings)
            {
                mic.Apply(setting);
                using var takeStream = options.Reopen ? CaptureStream.Open(mic.Id) : null;
                var source = takeStream ?? stream!;
                if (results.Count == 0)
                {
                    Console.WriteLine($"Capture format: {source.FormatDescription}");
                }

                source.Record(TimeSpan.FromSeconds(options.Settle));
                var samples = source.Record(TimeSpan.FromSeconds(options.Seconds));

                var take = Take.From(position, setting, samples);
                results.Add(take);
                Wav.WriteFloat(Path.Combine(options.OutputDirectory, $"{position}-{setting.ToString().ToLowerInvariant()}.wav"), samples, source.SampleRate);
                for (var c = 0; c < take.RmsDb.Length; c++)
                {
                    csv.Append(CultureInfo.InvariantCulture, $"{position},{setting},{c},{take.RmsDb[c]:0.00},{take.PeakDb[c]:0.00}\n");
                }

                Console.WriteLine($"  {setting,-9} {take.Describe()}");
            }
        }

        stream?.Dispose();
        File.WriteAllText(Path.Combine(options.OutputDirectory, "results.csv"), csv.ToString());
        PrintSummary(results, options);
        return 0;
    }

    private static void PrintSummary(List<Take> takes, Options options)
    {
        if (takes.Count == 0)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Overall level, dBFS (L-R balance in dB in brackets):");
        Console.Write($"{"",-8}");
        foreach (var setting in options.Settings)
        {
            Console.Write($"{setting,16}");
        }

        Console.WriteLine();
        foreach (var position in takes.Select(t => t.Position).Distinct())
        {
            Console.Write($"{position,-8}");
            foreach (var setting in options.Settings)
            {
                var take = takes.FirstOrDefault(t => t.Position == position && t.Setting == setting);
                Console.Write(take is null ? $"{"-",16}" : $"{$"{take.TotalDb,6:0.0} ({take.BalanceDb,5:+0.0;-0.0})",16}");
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

internal sealed record Take(string Position, MicSetting Setting, double[] RmsDb, double[] PeakDb, double TotalDb, double BalanceDb, double Correlation)
{
    public static Take From(string position, MicSetting setting, float[][] channels)
    {
        var rms = channels.Select(c => Db(Math.Sqrt(c.Average(s => (double)s * s)))).ToArray();
        var peak = channels.Select(c => Db(c.Max(s => Math.Abs((double)s)))).ToArray();
        var total = Db(Math.Sqrt(channels.Average(c => c.Average(s => (double)s * s))));
        var balance = channels.Length >= 2 ? rms[0] - rms[1] : 0;
        var correlation = channels.Length >= 2 ? Correlate(channels[0], channels[1]) : 1;
        return new Take(position, setting, rms, peak, total, balance, correlation);
    }

    public string Describe()
    {
        var perChannel = string.Join("  ", RmsDb.Select((db, c) => $"ch{c} {db,6:0.0}"));
        return $"total {TotalDb,6:0.0} dBFS   {perChannel}   L-R {BalanceDb,5:+0.0;-0.0} dB   corr {Correlation,5:0.00}";
    }

    private static double Db(double linear) => linear > 0 ? 20 * Math.Log10(linear) : -150;

    private static double Correlate(float[] a, float[] b)
    {
        double ab = 0, aa = 0, bb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            ab += a[i] * b[i];
            aa += a[i] * a[i];
            bb += b[i] * b[i];
        }

        return aa > 0 && bb > 0 ? ab / Math.Sqrt(aa * bb) : 0;
    }
}

internal sealed record Options(
    string[] Positions,
    MicSetting[] Settings,
    double Seconds,
    double Settle,
    bool? NoiseSuppression,
    bool Reopen,
    string OutputDirectory)
{
    public static Options? Parse(string[] args)
    {
        var positions = new[] { "front", "left", "right", "back" };
        var settings = new[] { MicSetting.Off, MicSetting.Cardioid, MicSetting.Stereo, MicSetting.Omni };
        double seconds = 4, settle = 1.5;
        bool? ns = false;
        var reopen = false;
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
                    case "--seconds": seconds = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--settle": settle = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--ns": ns = Next() switch { "on" => true, "off" => false, "keep" => null, var v => throw new ArgumentException(v) }; break;
                    case "--reopen": reopen = true; break;
                    case "--out": output = Next(); break;
                    default: return null;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        return new Options(positions, settings, seconds, settle, ns, reopen, output);
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
