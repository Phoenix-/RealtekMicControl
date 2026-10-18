# The microphone modes show no measurable effect on the GA503QS

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

## Caveats

- One point source and level measurements. A beamformer could in principle change the balance
  between a voice and *diffuse* noise without moving a single source's level; that was not tested.
- Armoury Crate was only measured with the stream kept open (live). The tool's own writes were
  measured live and with a fresh stream per take; neither showed a difference.
- The Fortemedia "intelligent noise suppression" bit (K9001) could not be turned off for the runs:
  written through the property store it snaps back within a second (`FMService64` owns it).

## Consequence

On this laptop the switch is, as far as a meter can tell, cosmetic. The tool still sets exactly what
Armoury Crate sets, so it is a faithful replacement -- just not one that changes how you sound.

The analysis scripts used for the band tables are in `tools/MicProbe/analysis/` (numpy).
