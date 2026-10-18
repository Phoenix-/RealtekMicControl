# Notes

Gotchas and non-obvious behaviors discovered while working on RealtekMicControl.
One file per trap. Keep entries short: the trap, the fix, and a pointer into
the code so future-you can find the guard.

## Index

- [fortemedia-mic-modes-live-in-the-apo-user-store.md](fortemedia-mic-modes-live-in-the-apo-user-store.md) — Armoury Crate's "Microphone modes" are three 32-bit values (K2/K7/K9001) in the Fortemedia capture APO's user property store on the mic endpoint, applied by `FMService64`; how they were found, and why the tool writes them through `IAudioSystemEffectsPropertyStore` instead of the registry.
- [realtek-audio-console-hides-fortemedia-on-asus.md](realtek-audio-console-hides-fortemedia-on-asus.md) — Realtek Audio Console has Fortemedia controls but shows none once the ASUS Fortemedia extension is installed; they only appear in a broken driver state, which is also when `FMService64` burns a full core.
- [two-way-ai-noise-cancelation-is-armoury-crate.md](two-way-ai-noise-cancelation-is-armoury-crate.md) — the "Two-way AI noise cancelation" switch is Intelligo running inside Armoury Crate, not a driver setting, so it disappears with Armoury Crate and is out of scope here.
- [generated-com-under-native-aot.md](generated-com-under-native-aot.md) — `[ComImport]` interop does not exist under Native AOT; Core Audio is declared with `[GeneratedComInterface]` and raw pointers are wrapped by `StrategyBasedComWrappers`.
