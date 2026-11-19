using Avalonia.Controls;
using TKCode.Arc.Primitives;

namespace TinaClass.App.Common.Api;

/// <summary>
/// Covers the app with the sign-in screen while nobody is signed in. The screen comes and goes through an
/// <see cref="ArcContentSwap"/> in Lift mode, like the pages do: it fades and rises at full motion, only fades when motion is
/// reduced and jumps when it is off. Nothing is hit-tested while it is empty, so the app underneath works once signed in.
/// </summary>
public sealed class SignInOverlay : Decorator
{
    private readonly ArcContentSwap _swap = new()
    {
        Mode = ContentSwapMode.Lift,
        EnterDuration = TimeSpan.FromMilliseconds(180),
        ExitDuration = TimeSpan.FromMilliseconds(120),
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
        VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
    };

    private readonly SignInViewModel _viewModel;
    private readonly SignInView _view;

    public SignInOverlay(SignInViewModel viewModel)
    {
        _viewModel = viewModel;
        _view = new SignInView { DataContext = viewModel };
        Child = _swap;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SignInViewModel.IsOpen))
            {
                Update();
            }
        };
        Update();
    }

    /// <summary>The screen is shown (null while the app is).</summary>
    public Control? Shown => _swap.Content as Control;

    private void Update() => _swap.Content = _viewModel.IsOpen ? _view : null;
}
