# Realtek Audio Console hides its Fortemedia controls on ASUS

Investigated 2026-10-18 (GA503QS, Realtek Audio Console 1.24.244.0).

## What the console is capable of

The console package ships `FMAPOCTL.dll`, a WinRT component that asks `FMService64` over RPC, per
function, `IsForte…Support` and `IsForte…Visible`: mic NS, AEC, beamforming, bypass, FFP, "follow
my voice", KSS, multi-voice, "only my voice", VR, speaker NS, howling detection. `RtkAudioComponent`
even has ASUS-specific pages (`AsusForteMicPage`, `RtkMicBssAsusPage`), and `FMAPOCTL` carries
vendor hide rules (`LENOVO_REQ_THINKBOOK_RTK_AUD_CTL_NOT_SHOW_FORTE_ITEM`).

## What it actually shows

Nothing Fortemedia-related -- neither with only the ASUS Package extension (1.0.3.211) nor with the
full Windows Update set (1.0.3.248 + APO 12.1.6003.5906 + service 0.1.1.81). Both install the same
`SAMSfpa_10431E8E10EC0289.dat` tuning file (identical MD5).

The only time buttons appeared was a **broken** state: both Fortemedia extensions removed, so no
tuning file in `System32\drivers`. `FMService64` then fell back to defaults, advertised some
functions to the console -- and spun a full core in a registry-notification loop for as long as
the console was open, rewriting the K-values of every Realtek mic endpoint (dead ones included)
twice a second.

Conclusion: with the proper driver set, ASUS routes Fortemedia control through Armoury Crate on
this model on purpose. The exact flag (tuning file or console code) was not pinned down; it would
take writing an `FMAPOCTL` client or disassembling `RtkAudioCore.dll`, and would change nothing.

Side effect worth knowing: restarting `FMAPOService` breaks Armoury Crate's live view of the modes
until the next reboot (its "K2/K7 registry values changed" watcher goes silent).
