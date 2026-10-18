# RealtekMicControl

Switch the microphone modes of ASUS laptops — cardioid, stereo, omni or off, what Armoury Crate
calls "Microphone modes" — without Armoury Crate. .NET 10 and Native AOT.

> **Status: work in progress.** The core library and the `micmode` console tool work and are
> verified to change the driver's settings; a small WinUI 3 window is next. Built and tested on an
> ASUS ROG Zephyrus G15 (GA503QS) with Realtek audio and the Fortemedia SAMSoft effects.

## What it switches

ASUS ships a Fortemedia beamforming effect for the internal microphone array. Armoury Crate is the
only UI for it — Realtek Audio Console deliberately hides its own Fortemedia controls on these
models — so dropping Armoury Crate (say, for [G-Helper](https://github.com/seerge/g-helper)) used
to mean losing the switch. The settings themselves live in the driver, as three values in the
Fortemedia APO's property store on the microphone endpoint; this tool writes those, through the
same Windows API Armoury Crate uses, with no elevation.

"Two-way AI noise cancelation" is a different thing: it runs inside Armoury Crate itself and leaves
with it. See [`notes/`](notes/) for how all of this was worked out.

## `micmode`

```
micmode [status] [--json]   show the current state
micmode off                 turn microphone modes off
micmode on                  turn them back on with the last pattern
micmode cardioid|stereo|omni
                            pick a pattern (turns modes on)
micmode ns on|off           Fortemedia "intelligent noise suppression"
```

Exit codes: 0 ok, 1 bad arguments, 2 no Fortemedia microphone found, 3 the change failed.

## Building

```
dotnet test tests/RealtekMicControl.Core.Tests
dotnet publish src/RealtekMicControl.Cli -c Release -o publish/cli
```

The Native AOT link step needs the MSVC toolchain and `vswhere.exe` on `PATH`
(`C:\Program Files (x86)\Microsoft Visual Studio\Installer`).

## License

MIT
