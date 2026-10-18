using System.ComponentModel;
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
    private const int NeedsAdministrator = 4;

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
          micmode fix                 make Windows actually run the Fortemedia effect (as administrator;
                                      restarts Windows Audio, sound stops for a few seconds)
          micmode unfix               put the driver's effect order back (as administrator)
          micmode --version

        "effect chain: broken" means Windows skips the Fortemedia effect, so the modes change nothing
        until "micmode fix" has run. A driver reinstall breaks it again.

        Exit codes: 0 ok, 1 bad arguments, 2 no Fortemedia microphone found, 3 the change failed,
        4 needs an administrator.
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

        if (words is ["fix"] or ["unfix"])
        {
            return ChangeEffectChain(restore: words[0] == "unfix");
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
                return ReportNoMicrophone();
            }

            var results = microphones.Select(mic => (mic, state: action(mic), chain: mic.ReadEffectChain())).ToList();
            if (json)
            {
                WriteJson(results);
            }
            else
            {
                foreach (var (mic, state, chain) in results)
                {
                    WriteText(mic, state, chain);
                }
            }

            return Ok;
        }
        catch (Exception e) when (IsReportable(e))
        {
            Console.Error.WriteLine($"micmode: {e.Message}");
            return Failed;
        }
    }

    /// <summary>
    /// fix / unfix: rewrite the mode-effect order of every Fortemedia microphone, then restart
    /// Windows Audio once if anything changed, since it only reads the order when it starts.
    /// </summary>
    private static int ChangeEffectChain(bool restore)
    {
        if (!EffectChainRepair.IsAdministrator())
        {
            Console.Error.WriteLine($"micmode: '{(restore ? "unfix" : "fix")}' changes a machine-wide driver setting " +
                                    "and restarts Windows Audio; run it from a terminal opened as administrator.");
            return NeedsAdministrator;
        }

        try
        {
            var microphones = ForteMicrophone.FindAll();
            if (microphones.Count == 0)
            {
                return ReportNoMicrophone();
            }

            var restarted = EffectChainRepair.Run(
                microphones,
                restore,
                done: (mic, change) =>
                {
                    Console.WriteLine(mic.Name);
                    Console.WriteLine($"  {"effect chain:",-19}{Describe(change)}");
                },
                restarting: () => Console.Write("Restarting Windows Audio... "));
            if (restarted)
            {
                Console.WriteLine("done.");
            }

            return Ok;
        }
        catch (Exception e) when (IsReportable(e))
        {
            Console.Error.WriteLine($"micmode: {e.Message}");
            return Failed;
        }
    }

    private static int ReportNoMicrophone()
    {
        Console.Error.WriteLine("micmode: no active microphone with the Fortemedia effects was found. " +
                                "Is the ASUS Fortemedia driver installed and the internal microphone enabled?");
        return NoMicrophone;
    }

    private static bool IsReportable(Exception e) =>
        e is COMException or InvalidOperationException or Win32Exception or UnauthorizedAccessException
            or System.Security.SecurityException or IOException;

    private static string Describe(EffectChainChange change) => change switch
    {
        EffectChainChange.Fixed => "fixed: Fortemedia moved to the end",
        EffectChainChange.AlreadyOk => "already ok",
        EffectChainChange.Restored => "driver order restored",
        EffectChainChange.NothingToRestore => "nothing to restore",
        _ => "unknown layout, left alone",
    };

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

    private static string Describe(EffectChainState chain) => chain switch
    {
        EffectChainState.Ok => "ok",
        EffectChainState.Broken => "broken",
        _ => "unknown",
    };

    private static void WriteText(ForteMicrophone mic, ForteState state, EffectChainState chain)
    {
        var mode = Describe(state.Setting);
        if (state.Setting == MicSetting.Off && state.Mode is { } last)
        {
            mode += $" (last pattern: {last.ToString().ToLowerInvariant()})";
        }

        var effectChain = chain == EffectChainState.Broken
            ? "broken: Windows skips the Fortemedia effect, the modes change nothing; run 'micmode fix' as administrator"
            : Describe(chain);

        Console.WriteLine(mic.Name);
        Console.WriteLine($"  {"mode:",-19}{mode}");
        Console.WriteLine($"  {"noise suppression:",-19}{Describe(state.NoiseSuppression)}");
        Console.WriteLine($"  {"effect chain:",-19}{effectChain}");
    }

    /// <summary>
    /// Hand-written with Utf8JsonWriter: no reflection for the AOT compiler to choke on, and the
    /// default encoder escapes non-ASCII, so a localized device name survives any console code page.
    /// </summary>
    private static void WriteJson(List<(ForteMicrophone Mic, ForteState State, EffectChainState Chain)> results)
    {
        using var stdout = Console.OpenStandardOutput();
        using (var writer = new Utf8JsonWriter(stdout, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartArray();
            foreach (var (mic, state, chain) in results)
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

                writer.WriteString("effectChain", Describe(chain));
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
