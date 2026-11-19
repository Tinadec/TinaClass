namespace TinaClass.App.Features.Settings;

/// <summary>
/// The settings the app can change on this device, for the assistant (persona, behavior, tool safety, the default model)
/// and for the interface (theme, language, motion). Backed by <c>UserSettings</c> under the application data folder.
/// </summary>
public interface ISettingsService
{
    /// <summary>The current settings.</summary>
    AgentSettings Current();

    /// <summary>Saves the settings the page changed; returns the persisted snapshot.</summary>
    AgentSettings Save(AgentSettings settings);

    /// <summary>Restores every setting to its default and saves.</summary>
    AgentSettings Reset();
}

/// <summary>The assistant's settings as the page edits them (the model code is a summary the Models page owns).</summary>
public sealed record AgentSettings
{
    public string PersonaName { get; set; } = "助手";

    public string PersonaRole { get; set; } = "teaching_assistant";

    public string PersonaAudience { get; set; } = "colleague";

    public bool StreamResponses { get; set; } = true;

    public bool ShowReasoning { get; set; } = true;

    public bool UseKnowledge { get; set; } = true;

    public string Autonomy { get; set; } = "ask";

    public bool ConfirmExternal { get; set; } = true;

    public string? DefaultModelCode { get; set; }

    public bool KeepLocal { get; set; } = true;
}
