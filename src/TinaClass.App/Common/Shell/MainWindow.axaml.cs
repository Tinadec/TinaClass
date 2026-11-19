using TinaClass.App.Common.Settings;
using TinaClass.App.Common.Theme;
using TKCode.Arc;

namespace TinaClass.App.Common.Shell;

/// <summary>The desktop window; its content is the shell of the chosen layout (<see cref="ShellLayout"/>).</summary>
public partial class MainWindow : ArcWindow
{
    public MainWindow()
    {
        InitializeComponent();
        Content = ShellLayout.Create();
        UserSettings.Current.TrackWindow(this);
        ThemeTransition.Register(this);
    }

    /// <summary>The shell inside the window.</summary>
    public ShellBase Shell => (ShellBase)Content!;
}
