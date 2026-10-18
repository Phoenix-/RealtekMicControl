# Windows drops the mic's whole MFX chain when the AEC APO is not last

Found 2026-10-18 on the GA503QS (Windows 11 build 26300, driver set as in
[fortemedia-mic-modes-live-in-the-apo-user-store.md](fortemedia-mic-modes-live-in-the-apo-user-store.md))
by live-debugging `audiodg.exe` and the AudioSrv `svchost`. This is why the microphone modes never
did anything.

## The trap

The internal mic's mode effects are a composite list,
`FxProperties\{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},14` (`PKEY_CompositeFX_ModeEffectClsid`), which
the ASUS/Realtek INFs leave as:

```
{806B324A-6827-46A8-AFD0-8AB09F8E6E61}  CRtkRecVolMFX     (RltkAPOU64.dll)
{70306EF0-7851-11DC-8314-0800200C9A66}  CFMAPOCAP         (FMAPO64.dll, the Fortemedia engine)
{8065FF2D-2394-4E99-999B-8F95C44F1799}  CRtkRecInvVolMFX  (RltkAPOU64.dll)
```

When a capture stream is created, AudioSrv (`EffectPack::GetInitializedSystemEffectInterface` →
`CCompositeSystemEffect::ForEachInitializedApoInterface`) walks the initialized MFX APOs. On a
capture endpoint, any of them that answers `QueryInterface(IApoAcousticEchoCancellation)` and is
**not the last one** fails the walk with `0x887C0037` (`systemeffect.cpp(838)` in the WIL output).
`CFMAPOCAP` is an AEC APO and sits in the middle, so the whole MFX chain -- Realtek's two APOs
included -- is thrown away. The APOs are created in `audiodg` through
`CAPOWrapperSrv::CreateSystemEffect`, initialized successfully, then released without ever being
locked for processing; the stream pipe is built without them. Only the endpoint effects
(`CRtkRecEFX`, Fortemedia's `FMAPOEFX`, whose engine stays a deferred parameter holder) run.

Nothing is logged anywhere a user would see; the APOs return S_OK from every call they get.

## The fix

Move `CFMAPOCAP` to the end of that list, then restart `AudioEndpointBuilder` (AudioSrv caches the
effect configuration):

```
{806B324A-…}  CRtkRecVolMFX
{8065FF2D-…}  CRtkRecInvVolMFX
{70306EF0-…}  CFMAPOCAP
```

The value needs admin (`SetValue` on the endpoint's `FxProperties`). After that `CFMAPOCAP` gets
`IsInputFormatSupported`, an AEC reference stream (`AddAuxiliaryInput`, 48 kHz stereo loopback) and
`LockForProcess`; it creates its SAM instance in normal mode, loads engine version 0x90 from
`samsfpa_10431e8e10ec0289.dat` (`SAM_K_OWN_VER 1100, DSP_VER 9006`), starts voice tracking, and
processes every buffer. No other change is needed (K900 in the `User` store can stay 0).

Measured with MicProbe right after the fix, phone in front of and behind the lid, levels in dBFS,
150-7000 Hz, DEFAULT stream (the SPEECH stream behaves the same):

| | Front | Back | Front − back |
|---|---|---|---|
| Off | −50.4 | −52.0 | 1.6 dB |
| Cardioid | −55.3 | −90.9 | **35.6 dB** |
| Stereo | −39.7 | −44.4 | 4.7 dB |
| Omni | −50.7 | −52.9 | 2.2 dB |

Room noise alone: Cardioid is 22-24 dB below Off; Stereo zeroes the rear channel.

Two side effects to know about:

- The driver has no RAW mode, so with a working MFX a "raw" stream
  (`AUDCLNT_STREAMOPTIONS_RAW`) goes through the DEFAULT mode chain as well. Compare against the Off
  setting, not against raw (`MicProbe --no-raw`).
- The order inside the chain changes: Realtek's volume pair now runs back to back in front of the
  Fortemedia engine instead of around it.
  Levels with the modes off are unchanged.

A driver reinstall or an endpoint rebuild writes the INF order back.

## How it was found

- FMAPO64 has its own log: `HKLM\SOFTWARE\Fortemedia\TraceFlag` (DWORD level mask, read when the
  APO loads) sends `Enter/Leave FMAPOCAP::…` lines through `OutputDebugStringA`. From a service in
  session 0 those reach only a debugger attached to `audiodg`, which is why a DBWIN listener saw
  nothing.
- `cdb -pd -p <audiodg>` with breakpoints on `audiodg!CSystemEffectWrapper::Create/Initialize/
  LockForProcess/~CSystemEffectWrapper` (public symbols) showed the MFX trio being created,
  initialized and destroyed per stream; the stacks pointed at AudioSrv.
- In the AudioSrv `svchost`, the lambda at `audiosrv+0x126f58` (build 26300) is the check:
  `CEndpointCharacteristics::AudioDirection()` = capture, MFX, not last, QI for
  `IApoAcousticEchoCancellation` succeeds → `0x887C0037`.
- Detach cdb from a system process by breaking it in (`DebugBreakProcess`) with
  `sxe -c "bc *; qd" bpe` set, never by killing cdb: that leaves its `int 3` patches in the target,
  and the next session sees corrupted code.
