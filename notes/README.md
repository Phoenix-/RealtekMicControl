# Notes

Gotchas and non-obvious behaviors discovered while working on RealtekMicControl.
One file per trap. Keep entries short: the trap, the fix, and a pointer into
the code so future-you can find the guard.

## Index

- [fortemedia-mic-modes-live-in-the-apo-user-store.md](fortemedia-mic-modes-live-in-the-apo-user-store.md) — Armoury Crate's "Microphone modes" are three 32-bit values (K2/K7/K9001) in the Fortemedia capture APO's user property store on the mic endpoint, applied by `FMService64`; how they were found, and why the tool writes them through `IAudioSystemEffectsPropertyStore` instead of the registry.
- [mic-modes-show-no-measurable-effect.md](mic-modes-show-no-measurable-effect.md) — measured with MicProbe (processed vs simultaneous raw capture, DEFAULT and SPEECH streams, switched by the tool and by Armoury Crate): the four settings differ by ±1-2 dB at most and cardioid does not reject a source behind the lid; DEFAULT is dual mono, SPEECH is mono with ~+37 dB gain.
- [realtek-audio-console-hides-fortemedia-on-asus.md](realtek-audio-console-hides-fortemedia-on-asus.md) — Realtek Audio Console has Fortemedia controls but shows none once the ASUS Fortemedia extension is installed; they only appear in a broken driver state, which is also when `FMService64` burns a full core.
- [two-way-ai-noise-cancelation-is-armoury-crate.md](two-way-ai-noise-cancelation-is-armoury-crate.md) — the "Two-way AI noise cancelation" switch is Intelligo running inside Armoury Crate, not a driver setting, so it disappears with Armoury Crate and is out of scope here.
- [generated-com-under-native-aot.md](generated-com-under-native-aot.md) — `[ComImport]` interop does not exist under Native AOT; Core Audio is declared with `[GeneratedComInterface]` and raw pointers are wrapped by `StrategyBasedComWrappers`.
