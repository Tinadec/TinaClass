namespace TinaClass.App.Common.Shell;

/// <summary>
/// Picks the shell of the template's <c>layout</c> option: <c>studio</c> (the default: sidebar, page, inspector), <c>focus</c> (a centered
/// column, no side panels) or <c>spaces</c> (an icon rail with a space panel). Each shell lives in its own files, which the manifest removes
/// for the layouts not chosen; each line below is a template block that goes with them (the first line that applies wins, so the studio
/// shell is what the template itself builds).
/// </summary>
public static class ShellLayout
{
    public static ShellBase Create()
    {
        ShellBase? shell = null;
        shell ??= new StudioShell();
        return shell!;
    }
}
