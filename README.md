# RealtekMicControl

What Armoury Crate's "Microphone modes" on ASUS laptops actually are, a tool to switch them without
Armoury Crate, and a test bench that measures whether they do anything. .NET 10 and Native AOT.

> **Status: research project, done.** On the laptop it was built on — an ASUS ROG Zephyrus G15
> (GA503QS), Realtek ALC289 with the Fortemedia SAMSoft effects — the modes turned out to have **no
> measurable effect** on the sound, whether switched by this tool or by Armoury Crate. The tool is
> kept as a faithful replacement for Armoury Crate's switch and as a way to repeat the measurement
> on other models, where the chain may well work. A GUI was planned and dropped for that reason.

## What the switch is

ASUS ships a Fortemedia beamforming effect (an audio processing object, APO) for the internal
microphone array, with three patterns — cardioid, stereo, omni — and an on/off switch. Armoury
Crate is the only UI for it: Realtek Audio Console has Fortemedia controls, but hides them once the
ASUS Fortemedia driver extension is installed. Two-way AI noise cancelation is a different thing —
Intelligo, running inside Armoury Crate — and leaves with it.

The switch itself is three 32-bit values in the Fortemedia capture APO's user property store on the
microphone endpoint, applied by the Fortemedia service:

| Value | Meaning |
|---|---|
| `{2b24be42-a892-11dc-8314-0800200c9a66},2` (K2) | pattern: 3 cardioid, 2 stereo, 1 omni |
| `…,7` (K7) | flags; bit `0x4` set = modes off |
| `…,9001` (K9001) | bit `0x10000000` = "intelligent noise suppression"; owned by the service, reverts if written |

This tool writes them through `IAudioSystemEffectsPropertyStore`, the same Windows 11 API Armoury
Crate uses, with no elevation. Every detail, including how it was found, is in [`notes/`](notes/).

## What the measurements say

[`tools/MicProbe`](tools/MicProbe) plays nothing itself: a phone plays a test signal at positions
around the laptop while MicProbe switches the modes and records, for every take, the processed
stream an app gets **and** a raw stream that bypasses every effect, so each take is measured as
gain over raw for the same seconds of sound.

Across eight runs — ordinary (DEFAULT) and call (SPEECH) streams, kept open or reopened per take,
switched by the tool and by Armoury Crate, with the Windows Update APO and with ASUS's own
monolithic one, with the APO's second, out-of-sync settings store forced on — the four settings
stayed within 1–2 dB of each other and cardioid never rejected a source behind the lid. The only
consistent trace of the switch is that, with modes on, more speech pauses get gated to silence.

The APO is loaded but never loads its processing engine; why was not pinned down (the leading
suspect is the APO not finding the Fortemedia interface it expects from the 2021 Realtek driver).
The full story, numbers and dead ends: [notes/mic-modes-show-no-measurable-effect.md](notes/mic-modes-show-no-measurable-effect.md).

## `micmode`

```
micmode [status] [--json]   show the current state
micmode off                 turn microphone modes off
micmode on                  turn them back on with the last pattern
micmode cardioid|stereo|omni
                            pick a pattern (turns modes on)
micmode ns on|off           Fortemedia "intelligent noise suppression" (reverts: the service owns it)
micmode --version
```

Exit codes: 0 ok, 1 bad arguments, 2 no Fortemedia microphone found, 3 the change failed.
`--json` escapes non-ASCII, so a localized device name survives any console code page.

## Measuring your own laptop

```
dotnet run --project tools/MicProbe -- --positions back,front --configs default,comms-reopen --seconds 6
```

Play continuous **speech** from a phone about 60 cm away (sine sweeps or noise are eaten whole by
the call path's noise suppression), press Enter at each position and keep quiet. `--manual` asks
you to switch the modes in Armoury Crate instead, which tells a broken tool from a broken chain.
WAVs of every take land in `spike/`; `tools/MicProbe/analysis/` has the numpy scripts for band
levels.

## Building

```
dotnet test tests/RealtekMicControl.Core.Tests
dotnet publish src/RealtekMicControl.Cli -c Release -o publish/cli
```

The Native AOT link step needs the MSVC toolchain and `vswhere.exe` on `PATH`
(`C:\Program Files (x86)\Microsoft Visual Studio\Installer`). Core Audio is declared with
source-generated COM because classic `[ComImport]` interop does not exist under Native AOT — see
[notes/generated-com-under-native-aot.md](notes/generated-com-under-native-aot.md).

## License

MIT
