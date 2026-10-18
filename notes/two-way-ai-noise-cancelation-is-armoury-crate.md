# "Two-way AI noise cancelation" is Armoury Crate, not the driver

Found 2026-10-18 while diffing Armoury Crate's audio page.

Toggling "Двустороннее интеллектуальное шумоподавление" (input and output sliders included) changes
nothing under the endpoints' `FxProperties` and nothing under `HKLM\SOFTWARE\Fortemedia`. It only
rewrites `C:\ProgramData\ASUS\DenoiseAIPlugin\Config.ini` (`MuteNoise`, `MuteNoiseUL`,
`MuteNoiseDL`, plus `ForteMode`/`ForteMuteNoise` mirrors of the Fortemedia switches).

Armoury Crate's log settles which engine runs it on this machine:
`[DenoiseAI] Final Support IG=1 RTK=0 Forte=1 RTKG5=0 IGAPO=0` -- Intelligo ("IG", launched as
`ArmouryCrate.DenoiseAI.exe` from `C:\Program Files\ASUS\Armoury Crate Service\DenoiseAIPlugin`,
with `IgoAudAgent.dll` and `portaudio_x64.dll`) for the AI part, Fortemedia for the mic modes.
No Intelligo APO is registered (`IGAPO=0`).

So the AI noise cancelation leaves together with Armoury Crate, and there is nothing for this tool
to switch. Discord's Krisp or NVIDIA Broadcast replace it; the Fortemedia modes and its own
"intelligent noise suppression" bit (K9001) stay, because they live in the driver.
