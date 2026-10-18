# Fitting a WinUI 3 window to its content

The app window has a fixed width and is exactly as tall as its content, which changes when the
"Windows skips the Fortemedia effect" or error bar opens. Two traps on the way
(`src/RealtekMicControl.App/MainWindow.xaml.cs`, `FitToContent` and `SetOpen`):

- **`AppWindow.ResizeClient` ignored the height** on this window (custom title bar via
  `ExtendsContentIntoTitleBar`, `OverlappedPresenter.IsResizable = false`): asked for a 466 px
  client, `ClientSize` stayed at 504. `AppWindow.Resize` with the frame added back
  (`Size - ClientSize`) lands exactly. Measure the root with an infinite height
  (`Root.Measure(new Size(width, ∞))`) rather than reading `ActualHeight`: the root is arranged
  inside the current window, so it can never report more than the window already has.
- **A closed `InfoBar` still takes the `StackPanel`'s `Spacing`.** `IsOpen = false` collapses its
  template, not the control, so two closed bars left 24 px of gap above the first visible
  element. Collapse `Visibility` together with `IsOpen`.

Verifying a window without a person looking at it: `PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT)`
captures WinUI content, but call `SetThreadDpiAwarenessContext(PER_MONITOR_AWARE_V2)` first, or
`GetWindowRect` hands a DPI-unaware PowerShell scaled-down coordinates and the capture comes out
cropped -- which looks exactly like a layout bug. UI Automation from Windows PowerShell 5.1
(`InvokePattern`, `SelectionItemPattern`, `TogglePattern`) drives the buttons; set
`AutomationProperties.Name` on buttons whose content is more than a single text. One exception:
a `ToggleButton`'s `TogglePattern.Toggle()` flips `IsChecked` without raising `Click`, so the mode
buttons are tested with a real mouse click at the centre of the element's
`BoundingRectangle` (again from a per-monitor-DPI-aware thread).
