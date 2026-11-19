using Avalonia.Controls;
using Avalonia.Layout;
using TKCode.Arc;
using TKCode.Arc.Icons.Lucide;
using TKCode.Arc.Localization;

namespace TinaClass.App.Features.Home;

/// <summary>
/// Home. The composer takes the first message and hands it to the chat (<see cref="HomeViewModel.Hub"/>); the starter prompts fill the
/// composer. The models come from the chat view model, which loads them.
/// </summary>
public partial class HomePage : UserControl
{
    private HomeViewModel? _viewModel;

    public HomePage()
    {
        InitializeComponent();
        Composer.Sent += (_, e) =>
        {
            var text = e.Message.Text?.Trim();
            if (_viewModel is { } vm && !string.IsNullOrEmpty(text))
            {
                Composer.Clear();
                vm.Hub.Start(text);
            }
        };
        ArcLanguage.Changed += (_, _) => FillStarters();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is HomeViewModel vm && vm != _viewModel)
        {
            _viewModel = vm;
            vm.PropertyChanged += (_, p) =>
            {
                if (p.PropertyName == nameof(HomeViewModel.IsLoading) && !vm.IsLoading)
                {
                    Fill(vm);
                }
            };
        }
    }

    private void Fill(HomeViewModel vm)
    {
        if (Composer.Models.Count == 0)
        {
            Composer.Models.AddRange(vm.Models);
            Composer.SelectedModel = vm.DefaultModel;
        }
        FillStarters();
    }

    private void FillStarters()
    {
        if (_viewModel is not { } vm)
        {
            return;
        }
        Starters.Items.Clear();
        foreach (var suggestion in vm.Suggestions)
        {
            var button = new TKCode.Arc.Button
            {
                Variant = ButtonVariant.Secondary,
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { new LucideIcon { Kind = LucideIconKind.Sparkles, Size = 14 }, new TextBlock { Text = suggestion.Label } },
                },
            };
            var prompt = suggestion.Prompt;
            button.Click += (_, _) =>
            {
                Composer.SetPrompt(prompt ?? "");
                Composer.FocusInput();
            };
            Starters.Items.Add(button);
        }
    }
}
