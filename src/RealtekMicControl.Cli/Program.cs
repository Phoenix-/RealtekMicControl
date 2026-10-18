using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using RealtekMicControl.Core;

namespace RealtekMicControl.Cli;

internal static class Program
{
    private const int Ok = 0;
    private const int UsageError = 1;
    private const int NoMicrophone = 2;
    private const int Failed = 3;

    private const string Usage = """
        micmode - switch the Fortemedia microphone modes of ASUS laptops (what Armoury Crate
        calls "Microphone modes") without Armoury Crate.

        Usage:
          micmode [status] [--json]   show the current state
          micmode off                 turn microphone modes off
          micmode on                  turn them back on with the last pattern
          micmode cardioid|stereo|omni
                                      pick a pattern (turns modes on)
          micmode ns on|off           Fortemedia "intelligent noise suppression"
          micmode --version

        Exit codes: 0 ok, 1 bad arguments, 2 no Fortemedia microphone found, 3 the change failed.
        """;

    private static int Main(string[] args)
    {
        var json = args.Contains("--json", StringComparer.OrdinalIgnoreCase);
        var words = args.Where(a => !a.Equals("--json", StringComparison.OrdinalIgnoreCase))
            .Select(a => a.ToLowerInvariant())
            .ToArray();

        if (words is ["--help" or "-h" or "/?" or "help"])
        {
            Console.WriteLine(Usage);
            return Ok;
        }

        if (words is ["--version"])
        {
            Console.WriteLine(Version());
            return Ok;
        }

        Func<ForteMicrophone, ForteState>? action = words switch
        {
            [] or ["status"] => mic => mic.Read(),
            ["off"] => mic => mic.Apply(MicSetting.Off),
            ["on"] => mic => mic.Update(state => state.WithModesEnabled(true)),
            ["cardioid"] => mic => mic.Apply(MicSetting.Cardioid),
            ["stereo"] => mic => mic.Apply(MicSetting.Stereo),
            ["omni"] => mic => mic.Apply(MicSetting.Omni),
            ["ns", "on"] => mic => mic.SetNoiseSuppression(true),
            ["ns", "off"] => mic => mic.SetNoiseSuppression(false),
            _ => null,
        };

        if (action is null)
        {
            Console.Error.WriteLine($"micmode: unknown command '{string.Join(' ', args)}'.");
            Console.Error.WriteLine(Usage);
            return UsageError;
        }

        try
        {
            var microphones = ForteMicrophone.FindAll();
            if (microphones.Count == 0)
            {
                Console.Error.WriteLine("micmode: no active microphone with the Fortemedia effects was found. " +
                                        "Is the ASUS Fortemedia driver installed and the internal microphone enabled?");
                return NoMicrophone;
            }

            var results = microphones.Select(mic => (mic, state: action(mic))).ToList();
            if (json)
            {
                WriteJson(results);
            }
            else
            {
                foreach (var (mic, state) in results)
                {
                    WriteText(mic, state);
                }
            }

            return Ok;
        }
        catch (Exception e) when (e is COMException or InvalidOperationException)
        {
            Console.Error.WriteLine($"micmode: {e.Message}");
            return Failed;
        }
    }

    private static string Describe(MicSetting? setting) => setting switch
    {
        MicSetting.Off => "off",
        MicSetting.Cardioid => "cardioid",
        MicSetting.Stereo => "stereo",
        MicSetting.Omni => "omni",
        _ => "unknown",
    };

    private static string Describe(bool? value) => value switch
    {
        true => "on",
        false => "off",
        null => "n/a",
    };

    private static void WriteText(ForteMicrophone mic, ForteState state)
    {
        var mode = Describe(state.Setting);
        if (state.Setting == MicSetting.Off && state.Mode is { } last)
        {
            mode += $" (last pattern: {last.ToString().ToLowerInvariant()})";
        }

        Console.WriteLine(mic.Name);
        Console.WriteLine($"  {"mode:",-19}{mode}");
        Console.WriteLine($"  {"noise suppression:",-19}{Describe(state.NoiseSuppression)}");
    }

    /// <summary>
    /// Hand-written with Utf8JsonWriter: no reflection for the AOT compiler to choke on, and the
    /// default encoder escapes non-ASCII, so a localized device name survives any console code page.
    /// </summary>
    private static void WriteJson(List<(ForteMicrophone Mic, ForteState State)> results)
    {
        using var stdout = Console.OpenStandardOutput();
        using (var writer = new Utf8JsonWriter(stdout, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartArray();
            foreach (var (mic, state) in results)
            {
                writer.WriteStartObject();
                writer.WriteString("name", mic.Name);
                writer.WriteString("id", mic.Id);
                writer.WriteString("mode", Describe(state.Setting));
                writer.WriteBoolean("modesEnabled", state.ModesEnabled);
                if (state.Mode is { } pattern)
                {
                    writer.WriteString("pattern", pattern.ToString().ToLowerInvariant());
                }
                else
                {
                    writer.WriteNull("pattern");
                }

                if (state.NoiseSuppression is { } ns)
                {
                    writer.WriteBoolean("noiseSuppression", ns);
                }
                else
                {
                    writer.WriteNull("noiseSuppression");
                }

                writer.WriteStartObject("raw");
                writer.WriteNumber("k2", state.ModeValue);
                writer.WriteNumber("k7", state.Flags);
                if (state.Features is { } features)
                {
                    writer.WriteNumber("k9001", features);
                }

                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        stdout.Write("\n"u8);
    }

    /// <summary>The version resource stamped into the exe; assembly attributes may be trimmed by AOT.</summary>
    private static string Version() =>
        Environment.ProcessPath is { } path
            ? FileVersionInfo.GetVersionInfo(path).ProductVersion ?? "unknown"
            : "unknown";
}
