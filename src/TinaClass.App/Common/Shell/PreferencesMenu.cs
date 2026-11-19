using Avalonia.Controls;
using Avalonia.Data;
using TinaClass.App.Common.Localization;
using TKCode.Arc;
using TKCode.Arc.Icons.Lucide;
using TKCode.Arc.Localization;

namespace TinaClass.App.Common.Shell;

/// <summary>
/// The preferences menu every layout opens from its user button: appearance, language and motion as radio entries on Arc's
/// <c>MenuSurface</c> (it scales out of the button on a spring, rows fade in with a short stagger, one highlight glides between them).
/// Built in code so the three shells share one menu; the entries bind to <see cref="ShellViewModel"/>.
/// </summary>
public static class PreferencesMenu
{
    public static MenuSurface Create(ShellViewModel vm)
    {
        var menu = new MenuSurface { Variant = MenuSurfaceVariant.Account, Side = MenuSide.Right, Alignment = MenuAlignment.End, AnchorGap = 10 };
        var appearance = Parent(AppLangResourceKind.Appearance, LucideIconKind.Sun, vm);
        appearance.Items.Add(Radio(vm, "pref-appearance", () => Text.Get(AppLangResourceKind.AppearanceLight), nameof(ShellViewModel.IsLight), vm.SetAppearanceCommand, "Light"));
        appearance.Items.Add(Radio(vm, "pref-appearance", () => Text.Get(AppLangResourceKind.AppearanceDark), nameof(ShellViewModel.IsDark), vm.SetAppearanceCommand, "Dark"));
        appearance.Items.Add(Radio(vm, "pref-appearance", () => Text.Get(AppLangResourceKind.AppearanceSystem), nameof(ShellViewModel.IsSystem), vm.SetAppearanceCommand, "System"));
        var language = Parent(AppLangResourceKind.Language, LucideIconKind.Languages, vm);
        language.Items.Add(Radio(vm, "pref-language", () => "English", nameof(ShellViewModel.IsEnglish), vm.SetLanguageCommand, "en-US"));
        language.Items.Add(Radio(vm, "pref-language", () => "简体中文", nameof(ShellViewModel.IsSimplifiedChinese), vm.SetLanguageCommand, "zh-CN"));
        language.Items.Add(Radio(vm, "pref-language", () => "繁體中文", nameof(ShellViewModel.IsTraditionalChinese), vm.SetLanguageCommand, "zh-TW"));
        var motion = Parent(AppLangResourceKind.Motion, LucideIconKind.Sparkles, vm);
        motion.Items.Add(Radio(vm, "pref-motion", () => Text.Get(AppLangResourceKind.MotionSystem), nameof(ShellViewModel.IsMotionSystem), vm.SetMotionCommand, "System"));
        motion.Items.Add(Radio(vm, "pref-motion", () => Text.Get(AppLangResourceKind.MotionFull), nameof(ShellViewModel.IsMotionFull), vm.SetMotionCommand, "Full"));
        motion.Items.Add(Radio(vm, "pref-motion", () => Text.Get(AppLangResourceKind.MotionReduced), nameof(ShellViewModel.IsMotionReduced), vm.SetMotionCommand, "Reduced"));
        motion.Items.Add(Radio(vm, "pref-motion", () => Text.Get(AppLangResourceKind.MotionOff), nameof(ShellViewModel.IsMotionOff), vm.SetMotionCommand, "Off"));
        menu.Items.Add(appearance);
        menu.Items.Add(language);
        menu.Items.Add(motion);
        // Full settings live on their own page; the menu keeps the quick preferences and a way in.
        var settings = new MenuEntry
        {
            Header = Text.Get(AppLangResourceKind.Settings),
            Icon = new LucideIcon { Kind = LucideIconKind.Settings, Size = 16 },
            Command = vm.OpenSettingsCommand,
        };
        ArcLanguage.Changed += (_, _) => settings.Header = Text.Get(AppLangResourceKind.Settings);
        menu.Items.Add(settings);
        return menu;
    }

    private static MenuEntry Parent(AppLangResourceKind header, LucideIconKind icon, ShellViewModel vm)
    {
        var entry = new MenuEntry { Header = Text.Get(header), Icon = new LucideIcon { Kind = icon, Size = 16 } };
        ArcLanguage.Changed += (_, _) => entry.Header = Text.Get(header);
        return entry;
    }

    private static MenuEntry Radio(ShellViewModel vm, string group, Func<string> header, string property, System.Windows.Input.ICommand command, string parameter)
    {
        var entry = new MenuEntry { Header = header(), ToggleType = MenuEntryToggleType.Radio, GroupName = group, Command = command, CommandParameter = parameter };
        entry.Bind(MenuEntry.IsCheckedProperty, new Binding(property) { Source = vm, Mode = BindingMode.OneWay });
        ArcLanguage.Changed += (_, _) => entry.Header = header();
        return entry;
    }
}
