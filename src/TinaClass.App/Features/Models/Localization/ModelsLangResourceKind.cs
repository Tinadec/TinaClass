using TKCode.Arc.Localization;

namespace TinaClass.App.Features.Models.Localization;

/// <summary>The text of the Models feature (one member per string; the .xlf files next to this one hold en-US, zh-CN and zh-TW).</summary>
[LanguageCatalog]
public enum ModelsLangResourceKind
{
    NavModels,
    Title,
    Lead,
    Providers,
    Keys,
    KeysNote,
    ModelsTable,
    Test,
    Testing,
    StatusOk,
    StatusNoKey,
    StatusError,
    StatusDisabled,
    Latency,
    Offline,
    ColModel,
    ColProvider,
    ColContext,
    ColPrice,
    ColDefault,
    ColEnabled,
    Free,
    Default,
    Embedding,
    EmptyKeys,
    ErrorKeyRequired,
    KeyFormat,
    KConnected,
    KInvalid,
    KNotSet,
    KTesting,
    KNoKey,
    KRemoved,
    KNeverUsed,
    KRejected,
    KAdd,
    KUndo,
    KCancel,
    KSave,
}
