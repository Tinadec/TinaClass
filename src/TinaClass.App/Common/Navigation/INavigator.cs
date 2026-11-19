namespace TinaClass.App.Common.Navigation;

/// <summary>Moves between pages from anywhere (the shell implements it).</summary>
public interface INavigator
{
    /// <summary>Shows the page with this key (<see cref="AppPages"/>).</summary>
    void Navigate(string key);
}
