# Notes

Gotchas and non-obvious behaviors discovered while working on RealtekMicControl.
One file per trap. Keep entries short: the trap, the fix, and a pointer into
the code so future-you can find the guard.

## Index

- [fortemedia-mic-modes-live-in-the-apo-user-store.md](fortemedia-mic-modes-live-in-the-apo-user-store.md) — Armoury Crate's "Microphone modes" are three 32-bit values (K2/K7/K9001) in the Fortemedia capture APO's user property store on the mic endpoint, applied by `FMService64`; how they were found, and why the tool writes them through `IAudioSystemEffectsPropertyStore` instead of the registry.
- [capture-mfx-dropped-when-aec-apo-is-not-last.md](capture-mfx-dropped-when-aec-apo-is-not-last.md) — **the root cause**: AudioSrv discards a capture mode-effect chain in which an `IApoAcousticEchoCancellation` APO is not last (`0x887C0037`), and ASUS puts Fortemedia's in the middle; moving `CFMAPOCAP` to the end makes the engine run (Cardioid: 35 dB front/back). Also: FMAPO64's `TraceFlag` log, and detaching cdb from system processes safely.
- [voice-clarity-replaces-the-oem-capture-chain.md](voice-clarity-replaces-the-oem-capture-chain.md) — with Windows 11's "Audio enhancements" set to Microsoft Voice Clarity, none of the driver's capture APOs load for that mic.
- [mic-modes-show-no-measurable-effect.md](mic-modes-show-no-measurable-effect.md) — superseded; measured with MicProbe before the root cause was known: the four settings differ by ±1-2 dB and cardioid does not reject a source behind the lid; the chase through drivers, stores and the APO's debug switches.
- [realtek-audio-console-hides-fortemedia-on-asus.md](realtek-audio-console-hides-fortemedia-on-asus.md) — Realtek Audio Console has Fortemedia controls but shows none once the ASUS Fortemedia extension is installed; they only appear in a broken driver state, which is also when `FMService64` burns a full core.
- [two-way-ai-noise-cancelation-is-armoury-crate.md](two-way-ai-noise-cancelation-is-armoury-crate.md) — the "Two-way AI noise cancelation" switch is Intelligo running inside Armoury Crate, not a driver setting, so it disappears with Armoury Crate and is out of scope here.
- [generated-com-under-native-aot.md](generated-com-under-native-aot.md) — `[ComImport]` interop does not exist under Native AOT; Core Audio is declared with `[GeneratedComInterface]` and raw pointers are wrapped by `StrategyBasedComWrappers`.
