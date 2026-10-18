# Fortemedia microphone modes live in the APO's user property store

Found 2026-10-18 on an ASUS ROG Zephyrus G15 GA503QS (Realtek ALC289, driver 6.0.9123.1, Fortemedia
APO 12.1.6003.5906, `FMService64` 0.1.1.81) by snapshotting the registry while flipping each switch
in Armoury Crate.

## Where

```
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture\{endpoint}\
    FxProperties\{70306EF0-7851-11DC-8314-0800200C9A66}\User
```

`{70306EF0…}` is `CFMAPOCAP`, the Fortemedia SAMSoft capture APO, sitting in the mode-effect slot
of the internal "Microphone Array (Realtek(R) Audio)" endpoint. The values share the format id
`{2b24be42-a892-11dc-8314-0800200c9a66}`:

| pid | Name used by the driver | Meaning |
|---|---|---|
| 2 | K2 | pattern: 3 = cardioid, 2 = stereo, 1 = omni |
| 7 | K7 | flags, driver default `0x890000`; bit `0x4` set = "Microphone modes" off |
| 9001 | K9001 | bit `0x10000000` = the separate "intelligent noise suppression" switch |

The driver INF seeds both `Default` and `User` from `oemxaudioextfm_asus.inf`
(`SysRecFx_win11.AddReg`); Armoury Crate only ever changed `User`.

## Who applies them

`FMService64.exe` ("Fortemedia APO Control Service") watches these keys with
`RegNotifyChangeKeyValue` ("k2k7 change event from reg notification" in its strings) and pushes the
result into the APO. Armoury Crate's `DenoiseAIPlugin` also has an RPC client for the same service
(`FMAPOCTL`), but the values it persists are just these.

Two other writers show up in Process Monitor around a restart of Windows Audio:

- `ArmouryCrate.UserSessionHelper.exe` writes the K2/K7 last chosen in Armoury Crate back about
  five seconds after the service starts, so while Armoury Crate is installed a mode set by this
  tool lasts only until the next audio restart: every boot (confirmed by rebooting), and the
  restart `micmode fix` does.
- `FMService64.exe` rewrites K9001 over and over, cycling `0`, `0x20000000` and `0x10000000`
  (118 writes in a minute) -- which is why K9001 never keeps a value written from outside.

## How the tool writes them

Not through the registry: those keys are writable by the audio services only. Windows 11's
`IAudioSystemEffectsPropertyStore` (from `IMMDevice::Activate`, activation parameter = the APO
CLSID as a `VT_CLSID` PROPVARIANT) opens the same `User` store for a normal user, and it is what
Armoury Crate uses ("V3 APO Open User Property Store" in its log). Writes keep the value type that
was read (`VT_UI4`); another type would be serialized as a blob instead of the `REG_DWORD` the
driver reads. See `src/RealtekMicControl.Core/ApoUserPropertyStore.cs`.

Verified: every change made with `micmode` lands in the registry as `REG_DWORD` with only the
intended bits changed, no elevation, and Armoury Crate writes exactly the same K2/K7 for the same
choice. K2 and K7 stay put; **K9001 does not** -- written this way it is back to `0x10000000` within
about a second, closed Realtek Audio Console or not, so it is state `FMService64` owns rather than a
setting (Armoury Crate presumably switches it over its RPC client).

Out of the box the modes do nothing to the sound, because Windows never puts `CFMAPOCAP` into the
pipeline: [capture-mfx-dropped-when-aec-apo-is-not-last.md](capture-mfx-dropped-when-aec-apo-is-not-last.md).
Once it does, they work as advertised.
