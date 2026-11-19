using TKCode.Arc.Localization;

namespace TinaClass.App.Features.Settings.Localization;

/// <summary>The text of the Settings feature (one member per string; the .xlf files next to this one hold en-US, zh-CN and zh-TW).</summary>
[LanguageCatalog]
public enum SettingsLangResourceKind
{
    NavSettings,
    Title,
    Lead,

    // Side anchors (the groups)
    GroupAssistant,
    GroupBehavior,
    GroupTools,
    GroupModel,
    GroupAppearance,
    GroupAbout,

    // Assistant: who the agent is for this person (a role/persona, not business data).
    PersonaName,
    PersonaNameHint,
    PersonaRole,
    PersonaRoleHint,
    PersonaAudience,
    PersonaAudienceHint,
    PersonaPlaceholder,
    RoleAssistant,
    RoleTutor,
    RoleCoach,
    AudienceColleague,
    AudienceStudents,

    // Behavior: how an answer is produced.
    StreamResponses,
    StreamResponsesHint,
    ShowReasoning,
    ShowReasoningHint,
    UseKnowledge,
    UseKnowledgeHint,
    Autonomy,
    AutonomyHint,
    AutonomyAsk,
    AutonomyAuto,
    AutonomyPlan,

    // Tools and safety: what the agent may do.
    ConfirmExternal,
    ConfirmExternalHint,
    ToolsNote,

    // Model: a read-only summary plus the jump to the Models page.
    DefaultModel,
    DefaultModelHint,
    OpenModels,
    ModelSummaryNone,
    ModelManagedNote,

    // Appearance (the existing preferences, collected in one place).
    Appearance,
    AppearanceHint,
    AppearanceLight,
    AppearanceDark,
    AppearanceSystem,
    Language,
    LanguageHint,
    Motion,
    MotionHint,
    MotionSystem,
    MotionFull,
    MotionReduced,
    MotionOff,

    // Data and about.
    KeepLocal,
    KeepLocalHint,
    ResetSettings,
    ResetSettingsConfirm,
    SettingsReset,
    AboutVersion,
    AboutNote,
    Saved,
}
