using TinaClass.App.Common.Settings;

namespace TinaClass.App.Features.Settings;

/// <summary>
/// The settings service over <see cref="UserSettings"/>: the persona, behavior, tool safety and data choices live in
/// <c>settings.json</c>. The theme, language and motion are the same file but are applied by the shell, not here.
/// </summary>
public sealed class SampleSettingsService : ISettingsService
{
    public AgentSettings Current()
    {
        var s = UserSettings.Current;
        return new AgentSettings
        {
            PersonaName = s.AssistantName,
            PersonaRole = s.AssistantRole,
            PersonaAudience = s.AssistantAudience,
            StreamResponses = s.StreamResponses,
            ShowReasoning = s.ShowReasoning,
            UseKnowledge = s.UseKnowledge,
            Autonomy = s.AgentAutonomy,
            ConfirmExternal = s.ConfirmExternalTools,
            DefaultModelCode = s.DefaultModelCode,
            KeepLocal = s.KeepDataLocal,
        };
    }

    public AgentSettings Save(AgentSettings settings)
    {
        var s = UserSettings.Current;
        s.AssistantName = settings.PersonaName;
        s.AssistantRole = settings.PersonaRole;
        s.AssistantAudience = settings.PersonaAudience;
        s.StreamResponses = settings.StreamResponses;
        s.ShowReasoning = settings.ShowReasoning;
        s.UseKnowledge = settings.UseKnowledge;
        s.AgentAutonomy = settings.Autonomy;
        s.ConfirmExternalTools = settings.ConfirmExternal;
        s.DefaultModelCode = settings.DefaultModelCode;
        s.KeepDataLocal = settings.KeepLocal;
        s.Save();
        return Current();
    }

    public AgentSettings Reset()
    {
        var s = UserSettings.Current;
        s.AssistantName = "助手";
        s.AssistantRole = "teaching_assistant";
        s.AssistantAudience = "colleague";
        s.StreamResponses = true;
        s.ShowReasoning = true;
        s.UseKnowledge = true;
        s.AgentAutonomy = "ask";
        s.ConfirmExternalTools = true;
        s.DefaultModelCode = null;
        s.KeepDataLocal = true;
        s.Save();
        return Current();
    }
}
