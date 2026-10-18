# WinUI 3 + NativeAOT traps met in this app

All four hide in a Debug build (JIT, untrimmed payload) and show only in the published exe, so
check the published one. Code: `src/RealtekMicControl.App/MainWindow.xaml.cs` (constructor,
`ApplyCaptionButtonColors`) and the trim target in `RealtekMicControl.App.csproj`.

- **`AppWindow.Presenter is OverlappedPresenter` is false under NativeAOT.** The guarded
  `IsResizable = false` / `IsMaximizable = false` never ran, and the released 0.2.0 came out
  resizable and maximizable while the Debug build was fine. Window styles told it apart:
  `0x14CF0000` (`WS_THICKFRAME | WS_MAXIMIZEBOX`) published vs `0x14CA0000` in Debug. Create the
  presenter instead (`OverlappedPresenter.Create()`, set it up, `AppWindow.SetPresenter`). Meridian
  had met the same cast failure and works around it by measuring the window.
- **The payload trim must keep `Microsoft.UI.Xaml\Assets\NoiseAsset_256x256_PNG.png`.**
  Acrylic loads it; without it the settings flyout's background was fully transparent, the window
  behind showing straight through. ForceAutoHDR.Net's bisection had marked the folder safe, but
  its hello-world had no acrylic surface. Found by publishing with `-p:SkipPayloadTrim=true`
  (flyout fine), then putting files back into the trimmed folder one group at a time.
- **System caption buttons do not follow the XAML theme** once content extends into the title
  bar: on a dark window the close glyph stayed dark. Set `AppWindowTitleBar.Button*Color` from
  `ActualTheme`, again on `ActualThemeChanged` (Meridian's `TitleBarTheming`, compacted).
- **A `TitleBar` with a `RightHeader` is 48 DIP high, the caption buttons default to a 32 DIP
  band**, which put the close button visibly above the title and the gear.
  `AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall` aligns them.

Related, not AOT: Windows cannot hide Maximize while Minimize stays -- without `WS_MAXIMIZEBOX`
it only greys it out. Dropping both leaves a close button alone, which is what this window does.
