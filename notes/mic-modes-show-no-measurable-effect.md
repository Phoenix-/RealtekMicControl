# The microphone modes show no measurable effect on the GA503QS

> **Superseded by the root cause.** These measurements stand, but they measured a pipeline in
> which the Fortemedia engine never ran: first Windows' Voice Clarity had replaced the OEM capture
> chain ([voice-clarity-replaces-the-oem-capture-chain.md](voice-clarity-replaces-the-oem-capture-chain.md)),
> then AudioSrv kept dropping the mode-effect chain because the AEC APO was not last in it
> ([capture-mfx-dropped-when-aec-apo-is-not-last.md](capture-mfx-dropped-when-aec-apo-is-not-last.md)).
> With that fixed, Cardioid puts 35 dB between a source in front and one behind the lid.

Measured 2026-10-18 with `tools/MicProbe` on an ASUS ROG Zephyrus G15 GA503QS (driver set as in
[fortemedia-mic-modes-live-in-the-apo-user-store.md](fortemedia-mic-modes-live-in-the-apo-user-store.md)).

## Method

A phone played the same sound about 60 cm from the laptop, behind the lid (`back`) and in front of
it (`front`), plus left and right in an earlier run. For every position MicProbe recorded each
setting -- Off, Cardioid, Stereo, Omni -- twice at the same time:

- the **processed** stream an app gets: DEFAULT processing mode (an ordinary recorder) or the
  Communications category, i.e. SPEECH mode (Discord, Teams), kept open or reopened per take;
- a **raw** stream (`AUDCLNT_STREAMOPTIONS_RAW`, every APO bypassed) as the reference, so the result
  is the gain of processed over raw for the same seconds of sound, in the 150-7000 Hz band.

The settings were switched both by this tool and, in the decisive run, by hand in Armoury Crate
(MicProbe `--manual`); after each Armoury Crate click the driver store held exactly the K2/K7 the
tool writes.

## Results

| Stream | What the effects do | Difference between the four settings |
|---|---|---|
| DEFAULT | stereo mix format, but both channels bit-identical; a gate on pauses; otherwise within ~1 dB of raw above 400 Hz, low end cut 5-8 dB | none: ±1 dB in every band and position |
| SPEECH (Communications) | mono; about +37 dB of gain; a test signal of sine sweeps is suppressed to digital silence, speech passes | none: ±1-2 dB, no direction-dependent pattern, same whether switched by the tool or by Armoury Crate |

Cardioid did not attenuate a source behind the lid relative to Omni in any configuration; the gain
for `back` and `front` was the same within the take-to-take noise. "Stereo" cannot be stereo here
at all: the DEFAULT stream is dual mono and the SPEECH stream is one channel.

The raw stream has four channels: 0 and 1 are a front-facing pair, 2 and 3 carry one rear-facing
microphone twice. So the hardware for a front/back beamformer exists; nothing measured here used it
differently per mode.

The one thing the switch does move, consistently across every run: in the DEFAULT stream, with
modes on, more of the pauses between words are gated to digital silence -- 12-22 % of 100 ms
windows against 2-7 % with modes off. So the APO reacts to K7; it just does nothing directional.

## Chasing a broken chain

Followed up on the suspicion that the ASUS / Realtek / Fortemedia stack simply never assembled:

- **The APO is loaded but its engine is not.** During capture `audiodg.exe` has `FMAPO64.dll`
  12.01.6003.5906 (1.5 MB) loaded, but none of the engines it loads by name (`fmaudvb..f.dll`, up to
  43 MB each) nor `FM_Speech_PP64.dll`. Its strings show silent fallbacks:
  `FindForteMediaInterface() fail`, `IOCTL_SOUNDSOURCE_SET_DIGIMIC_HWGAIN fail`,
  `SAM_InitEx fail`, `...bypass`. Its status keys under `HKLM\SOFTWARE\Fortemedia` were last
  written at install time (2025-01-03) and never since.
- **ASUS's own monolithic APO changes nothing.** The two `SWC\VEN_1319&AID_0002` components were
  forced onto ASUS's 12.1.6003.5872 package (`UpdateDriverForPlugAndPlayDevices` with
  `INSTALLFLAG_FORCE`), which puts a 31 MB `FMAPO64.dll` with the engine built in and
  `FMService64` 0.0.5.55 in System32; `audiodg` loaded it. Same measurements as before.
- **Two stores, out of sync.** The endpoint's root `FxProperties` -- the classic store an APO is
  handed at initialization -- holds its own K2/K7 (plus K4/K31/K50/K90/K91 that the per-APO store
  lacks), and its K7 said *modes off* (`0x890004`) the whole time, whatever Armoury Crate set in
  `{CFMAPOCAP}\User`. Nothing syncs the two. BUILTIN\Users have SetValue on the root key. Writing
  both stores per take (`MicProbe --root`, fresh stream each time) on 5872: same measurements.

Where exactly the chain breaks was not found; the leading suspect remains the APO not finding the
Fortemedia interface it expects from the Realtek driver (6.0.9123.1, February 2021 -- the newest
ASUS ships for this model).

The APO has a debug path (`C:\FMSAM\dbglog_%p.log`, dumps under `C:\FMSAMzj42u6\`) and its strings
put `TraceFlag` right next to `SOFTWARE\Fortemedia` and "FMAPO: Debug function is disable".
Setting `HKLM\SOFTWARE\Fortemedia\TraceFlag`, `DebugMode` and `DebugFunction` (to 1 and to
0xFFFFFFFF), restarting the audio engine and opening streams produced neither files nor a single
line of session-0 debug output from `audiodg` -- so the switch lives somewhere else, and finding it
means disassembling `FMAPO64.dll`. That is where this was left.

Everything above was rolled back afterwards: the components are on the Windows Update set again
(APO 5906, service 0.1.1.81), the trace values are gone, root K7 is back to `0x890004`. Switching a
component's driver for an experiment like this is one call to `UpdateDriverForPlugAndPlayDevices`
with `INSTALLFLAG_FORCE` and the INF in the driver store; switching back is the same call with the
other INF.

## Caveats

- One point source and level measurements. A beamformer could in principle change the balance
  between a voice and *diffuse* noise without moving a single source's level; that was not tested.
- Both ways of switching were measured both ways of streaming: the tool's writes and Armoury
  Crate's clicks, each with the stream kept open and with a fresh SPEECH stream per take (the case
  of a call app started after the change). Armoury Crate + fresh stream, source behind the lid:
  +37.2 / +36.4 / +36.9 / +37.0 dB for Off / Cardioid / Stereo / Omni -- cardioid 0.6 dB *above*
  omni, i.e. noise.
- The Fortemedia "intelligent noise suppression" bit (K9001) could not be turned off for the runs:
  written through the property store it snaps back within a second (`FMService64` owns it).

## Consequence

As configured out of the box, the switch on this laptop was cosmetic: the engine it configures was
never in the pipeline. The SPEECH-stream numbers above are Voice Clarity's processing; what made
the DEFAULT-stream gate react to K7 was not pinned down. The disassembly and live debugging that found the
real break are in
[capture-mfx-dropped-when-aec-apo-is-not-last.md](capture-mfx-dropped-when-aec-apo-is-not-last.md).

The analysis scripts used for the band tables are in `tools/MicProbe/analysis/` (numpy).
