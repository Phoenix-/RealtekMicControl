using Microsoft.UI.Xaml;
using RealtekMicControl.Core;

namespace RealtekMicControl.App;

public partial class App : Application
{
    /// <summary>The arguments the window passes when it relaunches itself elevated.</summary>
    public const string FixArgument = "--fix";
    public const string UnfixArgument = "--unfix";

    private MainWindow? _window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (Environment.GetCommandLineArgs() is [_, FixArgument or UnfixArgument] commandLine)
        {
            Environment.Exit(RunWindowless(restore: commandLine[1] == UnfixArgument));
            return;
        }

        _window = new MainWindow();
        _window.Activate();
    }

    /// <summary>
    /// The elevated half of the Fix button: no window, just the repair and an exit code the window
    /// waits for -- the same codes micmode uses.
    /// </summary>
    private static int RunWindowless(bool restore)
    {
        if (!EffectChainRepair.IsAdministrator())
        {
            return 4;
        }

        try
        {
            var microphones = ForteMicrophone.FindAll();
            if (microphones.Count == 0)
            {
                return 2;
            }

            EffectChainRepair.Run(microphones, restore);
            return 0;
        }
        catch (Exception)
        {
            // Nobody sees this process; the window reports the exit code instead.
            return 3;
        }
    }
}
