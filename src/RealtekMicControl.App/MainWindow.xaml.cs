using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RealtekMicControl.Core;
using Windows.Foundation;
using Windows.Graphics;
using WinRT.Interop;

namespace RealtekMicControl.App;

public sealed partial class MainWindow : Window
{
    private const double LogicalWidth = 380;
    private const int ErrorCancelled = 1223;

    private static readonly MicSetting[] Settings = [MicSetting.Off, MicSetting.Cardioid, MicSetting.Stereo, MicSetting.Omni];

    private ForteMicrophone? _microphone;
    private RegistryWatcher? _watcher;
    private int _refreshQueued;

    /// <summary>Set while the radio buttons are made to show the stored value, not a click.</summary>
    private bool _showingState;

    /// <summary>The UAC prompt and the elevated run take the focus away; that is no reason to close.</summary>
    private bool _fixing;

    private bool _settingsOpen;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SetIconFromExecutable();
        ShowBuildVersion();
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }

        CenterOnScreen(logicalHeight: 420);
        Root.Loaded += (_, _) => FitToContent();

        CloseOnFocusLossSwitch.IsOn = AppSettings.CloseOnFocusLoss;
        SettingsFlyout.Opened += (_, _) => _settingsOpen = true;
        SettingsFlyout.Closed += (_, _) => _settingsOpen = false;
        Activated += OnActivated;
        Closed += (_, _) => _watcher?.Dispose();

        Connect();
    }

    /// <summary>(Re)finds the microphone and starts watching it; again after Windows Audio restarts.</summary>
    private void Connect()
    {
        _watcher?.Dispose();
        _watcher = null;
        _microphone = null;

        try
        {
            _microphone = ForteMicrophone.FindAll().FirstOrDefault();
        }
        catch (COMException e)
        {
            ShowError(e.Message);
        }

        ModeButtons.IsEnabled = _microphone is not null;
        if (_microphone is null)
        {
            MicrophoneName.Text = string.Empty;
            ShowError("No microphone with the Fortemedia effects was found. Is the ASUS Fortemedia driver installed and the internal microphone enabled?");
            return;
        }

        MicrophoneName.Text = _microphone.Name;
        _watcher = _microphone.Watch(QueueRefresh);
        Refresh();
    }

    /// <summary>
    /// Called by the registry watcher on its own thread, often (the Fortemedia service rewrites
    /// one of the values twice a second): fold a burst into one refresh on the UI thread.
    /// </summary>
    private void QueueRefresh()
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 0)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                Volatile.Write(ref _refreshQueued, 0);
                Refresh();
            });
        }
    }

    private void Refresh()
    {
        if (_microphone is not { } microphone)
        {
            return;
        }

        try
        {
            var state = microphone.Read();
            var chain = microphone.ReadEffectChain();

            _showingState = true;
            ModeButtons.SelectedIndex = state.Setting is { } setting ? Array.IndexOf(Settings, setting) : -1;
            _showingState = false;

            ChainStatus.Text = chain switch
            {
                EffectChainState.Ok => "Effect chain: ok. Windows runs the Fortemedia effect.",
                EffectChainState.Broken => "Effect chain: broken. Windows skips the Fortemedia effect.",
                _ => "Effect chain: unknown driver layout.",
            };

            SetOpen(ChainBar, chain == EffectChainState.Broken);
        }
        catch (Exception e) when (e is COMException or InvalidOperationException)
        {
            ShowError(e.Message);
        }
    }

    private void OnModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_showingState || _microphone is not { } microphone || ModeButtons.SelectedIndex < 0)
        {
            return;
        }

        try
        {
            microphone.Apply(Settings[ModeButtons.SelectedIndex]);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            ShowError(ex.Message);
        }

        Refresh();
    }

    /// <summary>
    /// Relaunches this exe elevated with <see cref="App.FixArgument"/>: UAC, then a windowless
    /// run that rewrites the list and restarts Windows Audio. The window waits and reconnects.
    /// </summary>
    private async void OnFixClick(object sender, RoutedEventArgs e)
    {
        if (Environment.ProcessPath is not { } executable)
        {
            return;
        }

        _fixing = true;
        FixButton.IsEnabled = false;
        try
        {
            using var process = Process.Start(new ProcessStartInfo(executable, App.FixArgument)
            {
                UseShellExecute = true,
                Verb = "runas",
            });
            if (process is not null)
            {
                await process.WaitForExitAsync();
                if (process.ExitCode != 0)
                {
                    ShowError($"The fix did not go through (exit code {process.ExitCode}).");
                }
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            // The UAC prompt was declined: nothing happened, nothing to report.
        }
        finally
        {
            _fixing = false;
            FixButton.IsEnabled = true;
        }

        // Windows Audio was restarted under us.
        Connect();
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            if (AppSettings.CloseOnFocusLoss && !_fixing && !_settingsOpen)
            {
                Close();
            }

            return;
        }

        var armouryCrate = Process.GetProcessesByName("ArmouryCrate.UserSessionHelper");
        var visibility = armouryCrate.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var process in armouryCrate)
        {
            process.Dispose();
        }

        if (ArmouryHint.Visibility != visibility)
        {
            ArmouryHint.Visibility = visibility;
            FitToContent();
        }

        Refresh();
    }

    private void OnCloseOnFocusLossToggled(object sender, RoutedEventArgs e) =>
        AppSettings.CloseOnFocusLoss = CloseOnFocusLossSwitch.IsOn;

    private void ShowError(string message)
    {
        ErrorBar.Message = message;
        SetOpen(ErrorBar, true);
    }

    private void OnErrorBarClosed(InfoBar sender, InfoBarClosedEventArgs args) => SetOpen(ErrorBar, false);

    /// <summary>
    /// A closed InfoBar is still a child of the StackPanel and still gets its Spacing, so it is
    /// collapsed as well; then the window is refitted to the new height.
    /// </summary>
    private void SetOpen(InfoBar bar, bool open)
    {
        var visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (bar.IsOpen == open && bar.Visibility == visibility)
        {
            return;
        }

        bar.IsOpen = open;
        bar.Visibility = visibility;
        FitToContent();
    }

    /// <summary>Makes the client area exactly as tall as the content wants, at a fixed width.</summary>
    private void FitToContent()
    {
        // Resize by the outer size plus the frame: ResizeClient left the height where it was on
        // this non-resizable, title-bar-less window.
        var scale = Scale();
        Root.Measure(new Size(LogicalWidth, double.PositiveInfinity));
        var frameWidth = AppWindow.Size.Width - AppWindow.ClientSize.Width;
        var frameHeight = AppWindow.Size.Height - AppWindow.ClientSize.Height;
        AppWindow.Resize(new SizeInt32(
            (int)Math.Ceiling(LogicalWidth * scale) + frameWidth,
            (int)Math.Ceiling(Root.DesiredSize.Height * scale) + frameHeight));
    }

    /// <summary>
    /// Places the window in the middle of its display before the first layout pass sizes it.
    /// <see cref="AppWindow"/> works in physical pixels; the DPI has to come from the HWND because
    /// there is no XamlRoot to ask yet.
    /// </summary>
    private void CenterOnScreen(double logicalHeight)
    {
        var scale = Scale();
        var width = (int)Math.Round(LogicalWidth * scale);
        var height = (int)Math.Round(logicalHeight * scale);
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        AppWindow.MoveAndResize(new RectInt32(
            workArea.X + ((workArea.Width - width) / 2),
            workArea.Y + ((workArea.Height - height) / 2),
            width,
            height));
    }

    private double Scale() => GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96.0;

    /// <summary>
    /// Takes the window icon from the one embedded in the exe: publish does not copy loose
    /// assets next to it, and a missing icon file kills a published WinUI app at startup.
    /// </summary>
    private void SetIconFromExecutable()
    {
        if (Environment.ProcessPath is not { } executablePath)
        {
            return;
        }

        var icon = ExtractIcon(IntPtr.Zero, executablePath, 0);
        if (icon != IntPtr.Zero)
        {
            AppWindow.SetIcon(Win32Interop.GetIconIdFromIcon(icon));
        }
    }

    /// <summary>The version resource, which says "-dev+sha" for anything that is not a release.</summary>
    private void ShowBuildVersion()
    {
        if (Environment.ProcessPath is { } executablePath)
        {
            AppTitleBar.Subtitle = FileVersionInfo.GetVersionInfo(executablePath).ProductVersion ?? string.Empty;
        }
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(IntPtr hwnd);

    [LibraryImport("shell32.dll", EntryPoint = "ExtractIconW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr ExtractIcon(IntPtr hInst, string executablePath, int iconIndex);
}
