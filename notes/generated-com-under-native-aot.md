# Core Audio under Native AOT needs source-generated COM

`[ComImport]` interfaces and `new SomeCoClass()` are built-in COM interop, which Native AOT does
not support -- the PowerShell prototype in `spike/` used exactly that and could not be carried
over. The Core Audio interfaces are therefore declared with `[GeneratedComInterface]` + `[Guid]`
(`src/RealtekMicControl.Core/Interop/CoreAudio.cs`); the published `micmode.exe` reads and writes
the Fortemedia store with them.

What that means in practice:

- **Creation is manual.** `CoCreateInstance` through `[LibraryImport]` returns a raw pointer;
  `StrategyBasedComWrappers.GetOrCreateObjectForComInstance(ptr, UniqueInstance)` turns it into the
  interface, and the caller's own reference is released right after (`ComInterop.cs`).
- **Only the slots you call need declaring**, in vtable order. Trailing ones may be omitted
  (`IMMDeviceEnumerator` declares one method of five); one missing in the middle shifts every slot
  after it and calls the wrong function.
- **`IMMDevice.Activate` returns `out nint`**, not `out object`: the result type depends on the IID,
  so it is wrapped by hand like the enumerator.
- **Strings from Core Audio are declared as `nint`** and freed with `Marshal.FreeCoTaskMem`, so
  ownership is explicit rather than left to a marshaller default.
- **PROPVARIANT is a 24-byte explicit-layout struct** with just the tag and the payload slot; values
  read through `GetValue` are released with `PropVariantClear`.
- **COM must be initialized on the calling thread.** The CLI joins the MTA explicitly
  (`ComInterop.EnsureInitialized`); on the WinUI thread the same call gets `RPC_E_CHANGED_MODE`,
  which is harmless.
