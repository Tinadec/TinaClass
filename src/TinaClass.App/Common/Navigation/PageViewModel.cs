using CommunityToolkit.Mvvm.ComponentModel;

namespace TinaClass.App.Common.Navigation;

/// <summary>
/// A page's view model: loaded the first time the page is shown (<see cref="ActivateAsync"/>), with a busy flag and the
/// last error to show. Services of a real backend would throw their own exception type; catch it in
/// <see cref="RefreshAsync"/> and set <see cref="Error"/>.
/// </summary>
public abstract partial class PageViewModel : ObservableObject
{
    private bool _loaded;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public bool HasError => Error is not null;

    /// <summary>What follows the page's name in the top bar's breadcrumb ("folder", "conversation").</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _crumbs = [];

    /// <summary>The buttons of the top bar for this page (right side).</summary>
    [ObservableProperty]
    private IReadOnlyList<PageAction> _actions = [];

    /// <summary>A short note before the buttons ("Saved 2 minutes ago", a badge such as "Published v12"); null for none.</summary>
    [ObservableProperty]
    private string? _barNote;

    /// <summary>Loads the page the first time it is shown.</summary>
    public async Task ActivateAsync()
    {
        if (_loaded)
        {
            return;
        }
        _loaded = true;
        await RefreshAsync();
    }

    /// <summary>Loads the page's data again.</summary>
    public async Task RefreshAsync()
    {
        IsLoading = true;
        Error = null;
        try
        {
            await LoadAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    protected abstract Task LoadAsync();
}
