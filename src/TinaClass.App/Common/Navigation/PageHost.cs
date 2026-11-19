using Avalonia;
using Avalonia.Controls;
using TKCode.Arc.Primitives;

namespace TinaClass.App.Common.Navigation;

/// <summary>
/// Shows one <see cref="AppPage"/> at a time. Each page is created the first time it is shown and kept, so it keeps its
/// state (selection, scroll position, filters) when the person comes back; its view model loads on first show.
/// Pages change through an <see cref="ArcContentSwap"/> in Lift mode: the old page fades out, the new one rises 4px
/// into place (opacity only at reduced motion, none when motion is off).
/// </summary>
public sealed class PageHost : Decorator
{
    public static readonly StyledProperty<AppPage?> PageProperty = AvaloniaProperty.Register<PageHost, AppPage?>(nameof(Page));

    private readonly Dictionary<string, Control> _views = new();
    private readonly ArcContentSwap _swap = new()
    {
        Mode = ContentSwapMode.Lift,
        EnterDuration = TimeSpan.FromMilliseconds(180),
        ExitDuration = TimeSpan.FromMilliseconds(90),
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
        VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Stretch
    };

    public PageHost() => Child = _swap;

    /// <summary>The view of the page shown (null before the first page and after <see cref="Clear"/>).</summary>
    public Control? View { get; private set; }

    public AppPage? Page
    {
        get => GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    /// <summary>The view model of the page shown.</summary>
    public PageViewModel? CurrentViewModel => View?.DataContext as PageViewModel;

    /// <summary>Forgets every page (after signing out, so the next user starts fresh).</summary>
    public void Clear()
    {
        _views.Clear();
        View = null;
        _swap.Content = null;
        SetCurrentValue(PageProperty, null);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PageProperty)
        {
            Show(change.GetNewValue<AppPage?>());
        }
    }

    private void Show(AppPage? page)
    {
        if (page is null)
        {
            View = null;
            _swap.Content = null;
            return;
        }
        if (!_views.TryGetValue(page.Key, out var view))
        {
            view = page.Create();
            _views[page.Key] = view;
        }
        View = view;
        _swap.Content = view;
        if (view.DataContext is PageViewModel viewModel)
        {
            _ = viewModel.ActivateAsync();
        }
    }
}
