# RealtekMicControl

Why the "Microphone modes" of ASUS laptops (Fortemedia cardioid / stereo / omni beamforming) do
nothing on Windows 11, a one-command fix, and a small tool to switch the modes without Armoury
Crate. .NET 10, Native AOT.

> **Status.** On the laptop this was built on — ASUS ROG Zephyrus G15 GA503QS, Realtek ALC289 with
> the Fortemedia SAMSoft effects — the modes had never worked: Windows threw the Fortemedia effect
> out of the microphone pipeline. `micmode fix` puts it back, and then the modes do what they say:
> Cardioid puts **35 dB** between a sound in front of the laptop and the same sound behind the lid.
> Other ASUS models with the same driver probably have the same problem; `micmode status` tells.

## Quick start

1. Settings → System → Sound → the internal microphone ("Microphone Array") → **Audio
   enhancements: Device default effects**. With *Microsoft Voice Clarity* selected, Windows uses its
   own processing instead of the laptop's and none of this applies; with *Off* there are no effects
   at all.
2. In a terminal opened **as administrator**: `micmode fix`. Windows Audio restarts, so sound stops
   for a few seconds.
3. `micmode cardioid` (or `stereo` / `omni` / `off`); no administrator needed for that.
4. `micmode status` should now say `effect chain: ok`.

`micmode unfix` puts the driver's order back. A driver reinstall does the same, and `status` will
say `broken` again: run `micmode fix` once more.

## Why the modes did nothing

The internal microphone's mode effects are a chain of three audio processing objects (APOs), in
the order the ASUS driver writes into the registry:

```
CRtkRecVolMFX  →  CFMAPOCAP (Fortemedia, does the beamforming)  →  CRtkRecInvVolMFX
```

When a recording starts, Windows' audio service checks the chain. On a microphone, an APO that does
acoustic echo cancellation (implements `IApoAcousticEchoCancellation`) is only allowed as the
**last** one. Fortemedia's is such an APO, and it sits in the middle. So Windows rejects the chain
(`0x887C0037`, logged nowhere a user would look) and records without any of the three. Every
switch in Armoury Crate kept writing settings for an engine that was never running.

`micmode fix` moves `CFMAPOCAP` to the end of the list and restarts Windows Audio. After that the
Fortemedia engine loads, gets what the speakers play as its echo reference, and processes every
buffer. Measured with a phone playing the same sound in front of the laptop and behind the lid
(dBFS, 150–7000 Hz):

| Mode | Front | Back | Front − back |
|---|---|---|---|
| Off | −50.4 | −52.0 | 1.6 dB |
| **Cardioid** | −55.3 | −90.9 | **35.6 dB** |
| Stereo | −39.7 | −44.4 | 4.7 dB |
| Omni | −50.7 | −52.9 | 2.2 dB |

Before the fix, all four modes stayed within 1–2 dB of each other in every position. How this was
found (disassembly, the APO's hidden log, live debugging of `audiodg` and the audio service):
[notes/capture-mfx-dropped-when-aec-apo-is-not-last.md](notes/capture-mfx-dropped-when-aec-apo-is-not-last.md).

## What the switch is

Armoury Crate's switch is three 32-bit values in the Fortemedia capture APO's user property store
on the microphone endpoint:

| Value | Meaning |
|---|---|
| `{2b24be42-a892-11dc-8314-0800200c9a66},2` (K2) | pattern: 3 cardioid, 2 stereo, 1 omni |
| `…,7` (K7) | flags; bit `0x4` set = modes off |
| `…,9001` (K9001) | bit `0x10000000` = "intelligent noise suppression"; owned by the Fortemedia service, reverts if written |

`micmode` writes them through `IAudioSystemEffectsPropertyStore`, the same Windows 11 API Armoury
Crate uses, with no elevation. Realtek Audio Console has Fortemedia controls too, but hides them
once the ASUS Fortemedia driver extension is installed. Details: [`notes/`](notes/).

**With Armoury Crate still installed**, its `ArmouryCrate.UserSessionHelper.exe` writes its own
last choice back a few seconds after Windows Audio starts, including the restart `micmode fix`
does. Either pick the mode in Armoury Crate (it writes the same values, so its switch works too
once the chain is fixed) or uninstall it. Its "Two-way AI noise cancelation" is a separate thing — Intelligo, running inside Armoury Crate —
and leaves with it.

## `micmode`

```
micmode [status] [--json]   show the current state
micmode off                 turn microphone modes off
micmode on                  turn them back on with the last pattern
micmode cardioid|stereo|omni
                            pick a pattern (turns modes on)
micmode ns on|off           Fortemedia "intelligent noise suppression" (reverts: the service owns it)
micmode fix                 make Windows actually run the Fortemedia effect (as administrator)
micmode unfix               put the driver's effect order back (as administrator)
micmode --version
```

```
Набор микрофонов (Realtek(R) Audio)
  mode:              cardioid
  noise suppression: on
  effect chain:      ok
```

`effect chain` is `ok`, `broken` (the modes change nothing until `micmode fix`), or `unknown` (a
driver layout this tool does not recognize; it leaves those alone).

Exit codes: 0 ok, 1 bad arguments, 2 no Fortemedia microphone found, 3 the change failed, 4 needs
an administrator. `--json` escapes non-ASCII, so a localized device name survives any console code
page.

## Measuring your own laptop

```
dotnet run --project tools/MicProbe -- --positions front,back --configs default,comms --no-raw
```

Play the same sound from a phone about 60 cm away (speech works best: the call path's noise
suppression eats sine sweeps whole), press Enter at each position and keep quiet; MicProbe cycles
through Off / Cardioid / Stereo / Omni and records each. Compare each mode with Off at the same
position. Once the chain is fixed the "raw" stream also goes through the effects (the driver has no
raw mode), so it is no reference any more: hence `--no-raw`. `--manual` asks you to switch the
modes in Armoury Crate instead. WAVs of every take land in `spike/`; `tools/MicProbe/analysis/` has
numpy scripts for band levels.

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
