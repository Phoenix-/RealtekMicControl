# Voice Clarity replaces the OEM capture chain

On Windows 11 (seen on build 26300) Settings → System → Sound → the microphone → *Audio
enhancements* offers **Off / Device default effects / Microsoft Voice Clarity**. Voice Clarity is
an effect pack (`SWD\DRIVERENUM\{96bedf2c-…}#VocaEffectPack…`, `voiceclaritycpuapo.dll`) linked to
the endpoint through `FxProperties\{9e6136e0-57ab-4949-b57a-3627be142855},100`. While it is
selected, `audiodg` loads none of the driver's capture APOs for that mic -- no `RltkAPOU64`, no
`FMAPO64` -- so no Fortemedia setting can have any effect, and the SPEECH stream gets Voice
Clarity's own processing (mono, about +37 dB of gain, noise suppression strong enough to silence
sine sweeps).

It was found selected on the GA503QS during the measurements in
[mic-modes-show-no-measurable-effect.md](mic-modes-show-no-measurable-effect.md). Switching to
*Device default effects* brings the OEM chain back; the effect-pack link stays in the registry
either way.

With the OEM chain back, the modes still did nothing until the MFX order was fixed:
[capture-mfx-dropped-when-aec-apo-is-not-last.md](capture-mfx-dropped-when-aec-apo-is-not-last.md).
