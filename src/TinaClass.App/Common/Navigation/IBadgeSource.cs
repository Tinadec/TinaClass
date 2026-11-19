namespace TinaClass.App.Common.Navigation;

/// <summary>
/// A small count or note shown beside a page in the sidebar ("38", "1 pending"). A feature registers one for its page, so the shell
/// shows the number without knowing the feature (the shell calls only this interface).
/// </summary>
public interface IBadgeSource
{
    /// <summary>The page this badge belongs to (<see cref="AppPages"/>).</summary>
    string PageKey { get; }

    /// <summary>The text, or null for none; changes raise <see cref="Changed"/>.</summary>
    string? Text { get; }

    event EventHandler? Changed;

    /// <summary>Loads the number (the shell asks once the window is shown, and after a sign-in).</summary>
    Task RefreshAsync();
}
