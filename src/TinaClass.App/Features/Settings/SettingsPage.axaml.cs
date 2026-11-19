using Avalonia.Controls;
using Avalonia.Interactivity;
using TinaClass.App.Features.Settings.Localization;
using TKCode.Arc;
using TKCode.Arc.Localization;

namespace TinaClass.App.Features.Settings;

/// <summary>
/// The settings page: a left rail that scrolls the right column to a group, and the selects/segments whose options are
/// text (filled here so they follow the language). Choices are written back to the view model as small key strings.
/// </summary>
public partial class SettingsPage : UserControl
{
    private SettingsViewModel? _viewModel;

    public SettingsPage()
    {
        InitializeComponent();
        ArcLanguage.Changed += (_, _) => Fill();
        Fill();
        Loaded += (_, _) => Sync();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is SettingsViewModel vm && vm != _viewModel)
        {
            _viewModel = vm;
            Sync();
        }
    }

    private static string T(SettingsLangResourceKind kind) => SettingsLangResources.Get(kind);

    private void Fill()
    {
        RoleBox.Items.Clear();
        RoleBox.Items.Add(new SelectOption("teaching_assistant", T(SettingsLangResourceKind.RoleAssistant)));
        RoleBox.Items.Add(new SelectOption("tutor", T(SettingsLangResourceKind.RoleTutor)));
        RoleBox.Items.Add(new SelectOption("coach", T(SettingsLangResourceKind.RoleCoach)));
        AudienceBox.Items.Clear();
        AudienceBox.Items.Add(new SelectOption("colleague", T(SettingsLangResourceKind.AudienceColleague)));
        AudienceBox.Items.Add(new SelectOption("students", T(SettingsLangResourceKind.AudienceStudents)));
        AppearanceBox.Items.Clear();
        AppearanceBox.Items.Add(new SelectOption("system", T(SettingsLangResourceKind.AppearanceSystem)));
        AppearanceBox.Items.Add(new SelectOption("light", T(SettingsLangResourceKind.AppearanceLight)));
        AppearanceBox.Items.Add(new SelectOption("dark", T(SettingsLangResourceKind.AppearanceDark)));
        LanguageBox.Items.Clear();
        LanguageBox.Items.Add(new SelectOption("system", T(SettingsLangResourceKind.AppearanceSystem)));
        LanguageBox.Items.Add(new SelectOption("en-US", "English"));
        LanguageBox.Items.Add(new SelectOption("zh-CN", "简体中文"));
        LanguageBox.Items.Add(new SelectOption("zh-TW", "繁體中文"));
        MotionBox.Items.Clear();
        MotionBox.Items.Add(new SelectOption("system", T(SettingsLangResourceKind.MotionSystem)));
        MotionBox.Items.Add(new SelectOption("full", T(SettingsLangResourceKind.MotionFull)));
        MotionBox.Items.Add(new SelectOption("reduced", T(SettingsLangResourceKind.MotionReduced)));
        MotionBox.Items.Add(new SelectOption("off", T(SettingsLangResourceKind.MotionOff)));
        AutonomyTabs.Items.Clear();
        AutonomyTabs.Items.Add(new SegmentedOption(0, T(SettingsLangResourceKind.AutonomyAsk)));
        AutonomyTabs.Items.Add(new SegmentedOption(1, T(SettingsLangResourceKind.AutonomyAuto)));
        AutonomyTabs.Items.Add(new SegmentedOption(2, T(SettingsLangResourceKind.AutonomyPlan)));
        AutonomyTabs.SelectionChanged += (_, _) =>
        {
            if (_viewModel is { } vm && AutonomyTabs.SelectedItem is SegmentedOption o)
            {
                vm.Autonomy = o.Value switch { 1 => "auto", 2 => "plan", _ => "ask" };
            }
        };
        Hook(RoleBox, v => _viewModel?.PersonaRole = v);
        Hook(AudienceBox, v => _viewModel?.PersonaAudience = v);
        Hook(AppearanceBox, v => { if (_viewModel is { } vm) vm.Appearance = v switch { "light" => TKCode.Arc.ArcAppearance.Light, "dark" => TKCode.Arc.ArcAppearance.Dark, _ => TKCode.Arc.ArcAppearance.System }; });
        Hook(LanguageBox, v => { if (_viewModel is { } vm) vm.Language = v; });
        Hook(MotionBox, v => { if (_viewModel is { } vm) vm.Motion = v switch { "full" => Common.Theme.MotionPreference.Full, "reduced" => Common.Theme.MotionPreference.Reduced, "off" => Common.Theme.MotionPreference.Off, _ => Common.Theme.MotionPreference.System }; });
        Sync();
    }

    private static void Hook(Select select, Action<string> apply)
    {
        select.SelectionChanged += (_, _) =>
        {
            if (select.SelectedItem is SelectOption o && o.Value is string v)
            {
                apply(v);
            }
        };
    }

    private static void Pick(Select select, string? value)
    {
        for (var i = 0; i < select.Items.Count; i++)
        {
            if (select.Items[i] is SelectOption o && Equals(o.Value as string, value))
            {
                select.SelectedIndex = i;
                return;
            }
        }
        select.SelectedIndex = 0;
    }

    /// <summary>Pushes the view model's values into the selects (load, language change, reset).</summary>
    private void Sync()
    {
        if (_viewModel is not { } vm)
        {
            return;
        }
        Pick(RoleBox, vm.PersonaRole);
        Pick(AudienceBox, vm.PersonaAudience);
        Pick(AppearanceBox, vm.Appearance switch { TKCode.Arc.ArcAppearance.Light => "light", TKCode.Arc.ArcAppearance.Dark => "dark", _ => "system" });
        Pick(LanguageBox, vm.Language);
        Pick(MotionBox, vm.Motion switch { Common.Theme.MotionPreference.Full => "full", Common.Theme.MotionPreference.Reduced => "reduced", Common.Theme.MotionPreference.Off => "off", _ => "system" });
        AutonomyTabs.SelectedIndex = vm.Autonomy switch { "auto" => 1, "plan" => 2, _ => 0 };
    }

    /// <summary>Scrolls the right column to the group whose key the rail button carries.</summary>
    private void OnAnchorClick(object? sender, RoutedEventArgs e)
    {
        var key = (sender as Control)?.Tag as string;
        Control? target = key switch
        {
            "assistant" => GroupAssistant,
            "behavior" => GroupBehavior,
            "tools" => GroupTools,
            "model" => GroupModel,
            "appearance" => GroupAppearance,
            "about" => GroupAbout,
            _ => null,
        };
        target?.BringIntoView();
    }
}
