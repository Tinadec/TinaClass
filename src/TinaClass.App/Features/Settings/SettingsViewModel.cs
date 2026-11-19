using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TinaClass.App.Common;
using TinaClass.App.Common.Localization;
using TinaClass.App.Common.Navigation;
using TinaClass.App.Common.Settings;
using TinaClass.App.Common.Theme;
using TinaClass.App.Features.Models;
using TinaClass.App.Features.Settings.Localization;
using TKCode.Arc;
using TKCode.Arc.Localization;

namespace TinaClass.App.Features.Settings;

/// <summary>
/// The settings page: the assistant's persona and behavior, its tool safety, the model it starts with (a summary that
/// jumps to the Models page) and the interface (theme, language, motion). Appearance changes go straight to the same
/// <see cref="UserSettings"/> file the shell reads; the rest go through <see cref="ISettingsService"/>.
/// </summary>
public sealed partial class SettingsViewModel : PageViewModel
{
    private readonly ISettingsService _service;
    private readonly IModelsService _models;
    private readonly INavigator _navigator;

    public SettingsViewModel(ISettingsService service, IModelsService models, INavigator navigator)
    {
        _service = service;
        _models = models;
        _navigator = navigator;
        _appearance = UserSettings.Current.Appearance ?? ArcAppearance.System;
        _language = UserSettings.Current.Language ?? "system";
        _motion = UserSettings.Current.Motion;
    }

    // ---- Assistant ----
    [ObservableProperty]
    private string _personaName = "助手";

    [ObservableProperty]
    private string _personaRole = "teaching_assistant";

    [ObservableProperty]
    private string _personaAudience = "colleague";

    // ---- Behavior ----
    [ObservableProperty]
    private bool _streamResponses = true;

    [ObservableProperty]
    private bool _showReasoning = true;

    [ObservableProperty]
    private bool _useKnowledge = true;

    [ObservableProperty]
    private string _autonomy = "ask";

    // ---- Tools and safety ----
    [ObservableProperty]
    private bool _confirmExternal = true;

    // ---- Model (a summary; the Models page edits it) ----
    [ObservableProperty]
    private string _modelSummary = string.Empty;

    // ---- Data ----
    [ObservableProperty]
    private bool _keepLocal = true;

    // ---- Appearance (drives the same settings the shell applies) ----
    [ObservableProperty]
    private ArcAppearance _appearance;

    [ObservableProperty]
    private string _language;

    [ObservableProperty]
    private MotionPreference _motion;

    /// <summary>The product version shown under About.</summary>
    public string Version => AppInfo.Current.Version;

    protected override async Task LoadAsync()
    {
        var s = _service.Current();
        PersonaName = s.PersonaName;
        PersonaRole = s.PersonaRole;
        PersonaAudience = s.PersonaAudience;
        StreamResponses = s.StreamResponses;
        ShowReasoning = s.ShowReasoning;
        UseKnowledge = s.UseKnowledge;
        Autonomy = s.Autonomy;
        ConfirmExternal = s.ConfirmExternal;
        KeepLocal = s.KeepLocal;
        try
        {
            var model = (await _models.ModelsAsync()).FirstOrDefault(m => m.IsDefault);
            ModelSummary = model is null
                ? SettingsLangResources.Get(SettingsLangResourceKind.ModelSummaryNone)
                : model.DisplayName;
        }
        catch (Exception)
        {
            ModelSummary = s.DefaultModelCode ?? SettingsLangResources.Get(SettingsLangResourceKind.ModelSummaryNone);
        }
    }

    private AgentSettings Snapshot() => new()
    {
        PersonaName = PersonaName,
        PersonaRole = PersonaRole,
        PersonaAudience = PersonaAudience,
        StreamResponses = StreamResponses,
        ShowReasoning = ShowReasoning,
        UseKnowledge = UseKnowledge,
        Autonomy = Autonomy,
        ConfirmExternal = ConfirmExternal,
        KeepLocal = KeepLocal,
    };

    partial void OnPersonaNameChanged(string value) => Persist();

    partial void OnPersonaRoleChanged(string value) => Persist();

    partial void OnPersonaAudienceChanged(string value) => Persist();

    partial void OnStreamResponsesChanged(bool value) => Persist();

    partial void OnShowReasoningChanged(bool value) => Persist();

    partial void OnUseKnowledgeChanged(bool value) => Persist();

    partial void OnAutonomyChanged(string value) => Persist();

    partial void OnConfirmExternalChanged(bool value) => Persist();

    partial void OnKeepLocalChanged(bool value) => Persist();

    private void Persist()
    {
        _ = _service.Save(Snapshot());
        BarNote = SettingsLangResources.Get(SettingsLangResourceKind.Saved);
    }

    // ---- Appearance writes the same keys the shell reads (kept in step with the preferences menu). ----
    partial void OnAppearanceChanged(ArcAppearance value)
    {
        ThemeTransition.Apply(t => t.Appearance(value));
        UserSettings.Current.Appearance = value;
        UserSettings.Current.Save();
    }

    partial void OnLanguageChanged(string value)
    {
        var tag = value is "system" or null ? AppLanguages.Initial.Value : value;
        if (tag != ArcLanguage.Current.Value)
        {
            ArcLanguage.Set(tag);
        }
        UserSettings.Current.Language = value is "system" ? null : value;
        UserSettings.Current.Save();
    }

    partial void OnMotionChanged(MotionPreference value)
    {
        AppMotion.Apply(value);
        UserSettings.Current.Motion = value;
        UserSettings.Current.Save();
    }

    /// <summary>The Models page manages providers, keys and the default; this only opens it.</summary>
    [RelayCommand]
    private void OpenModels() => _navigator.Navigate(AppPages.Models);

    [RelayCommand]
    private void Reset()
    {
        var s = _service.Reset();
        PersonaName = s.PersonaName;
        PersonaRole = s.PersonaRole;
        PersonaAudience = s.PersonaAudience;
        StreamResponses = s.StreamResponses;
        ShowReasoning = s.ShowReasoning;
        UseKnowledge = s.UseKnowledge;
        Autonomy = s.Autonomy;
        ConfirmExternal = s.ConfirmExternal;
        KeepLocal = s.KeepLocal;
        BarNote = SettingsLangResources.Get(SettingsLangResourceKind.SettingsReset);
    }
}
