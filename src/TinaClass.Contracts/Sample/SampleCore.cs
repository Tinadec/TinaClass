using TinaClass.Contracts.Chat;
using TinaClass.Contracts.Models;

namespace TinaClass.Contracts.Sample;

public sealed record SamplePerson(Guid Id, string Name, string Email, string Role, string Initial);

public sealed record SampleProvider(Guid Id, string Name, string Protocol, string BaseUrl, bool RequiresKey, string? KeyLast4, string Status, int? LatencyMs, int Order);

public sealed record SampleModel(Guid Id, Guid ProviderId, string Code, string Name, string Kind, int Context, long PriceIn, long PriceOut, long PriceCache, string[] Caps,
    string[] Efforts, string Note, bool Enabled, bool IsDefault);

public sealed record SampleConversation(Guid Id, string Title, Guid? FolderId, bool Pinned, int DaysAgo, int Hour, int Minute, Guid OwnerId);

/// <summary>
/// The shared sample company: 栖云科技, product development, working on the export revamp. Numbers agree with each other (usage by model
/// and by member add up to the total, the chunk counts of the documents add up to the knowledge base's).
/// </summary>
public static class SampleCore
{
    public const string Company = "Tinadec";
    public const string Department = "产品研发部";

    public static IReadOnlyList<SamplePerson> People { get; } =
    [
        new(SampleIds.Owner, "沈知远", "shen.zhiyuan@qiyun.example", "owner", "沈"),
        new(SampleIds.Admin, "陆嘉禾", "lu.jiahe@qiyun.example", "admin", "陆"),
        new(SampleIds.He, "何砚秋", "he.yanqiu@qiyun.example", "member", "何"),
        new(SampleIds.Zhou, "周以宁", "zhou.yining@qiyun.example", "member", "周"),
        new(SampleIds.Tang, "唐书瑶", "tang.shuyao@qiyun.example", "member", "唐"),
    ];

    public static string NameOf(Guid? id) => id is null ? "智能体自动运行" : People.FirstOrDefault(p => p.Id == id)?.Name ?? "智能体自动运行";

    public static IReadOnlyList<SampleProvider> Providers { get; } =
    [
        new(SampleIds.Provider(1), "Northlight Cloud", ProviderProtocols.OpenAiCompatible, "https://api.northlight.example/v1", true, "4f2a", "ok", 412, 1),
        new(SampleIds.Provider(2), "Halo 网关", ProviderProtocols.OpenAiCompatible, "https://halo.qiyun.example/v1", true, "91c7", "ok", 286, 2),
        new(SampleIds.Provider(3), "本机运行时", ProviderProtocols.Local, "http://127.0.0.1:11434", false, null, "ok", 38, 3),
        new(SampleIds.Provider(4), "Meridian", ProviderProtocols.Messages, "https://api.meridian.example", true, null, "no_key", null, 4),
        new(SampleIds.Provider(5), "TinaClass 内置示例", ProviderProtocols.Echo, "builtin://echo", false, null, "ok", null, 5),
    ];

    /// <summary>Prices are minor units per million tokens: Aurora Pro 1800 in, 7200 out is ¥18 and ¥72.</summary>
    public static IReadOnlyList<SampleModel> Models { get; } =
    [
        new(SampleIds.Model(1), SampleIds.Provider(1), "aurora-pro", "Aurora Pro", "chat", 400_000, 1800, 7200, 180, ["vision", "tools", "reasoning"], ["deep", "balanced", "fast"], "综合最强，适合分析与长文", true, true),
        new(SampleIds.Model(2), SampleIds.Provider(1), "aurora-flash", "Aurora Flash", "chat", 200_000, 120, 480, 12, ["vision", "tools"], ["balanced", "fast"], "响应快、成本低，适合日常问答", true, false),
        new(SampleIds.Model(3), SampleIds.Provider(2), "vega-reasoner", "Vega Reasoner", "chat", 256_000, 2600, 10400, 260, ["tools", "reasoning"], ["deep", "balanced"], "多步推理，适合代码审查与数据核对", true, false),
        new(SampleIds.Model(4), SampleIds.Provider(3), "orion-local-8b", "Orion Local 8B", "chat", 32_000, 0, 0, 0, ["tools"], ["fast"], "离线可用，数据不出本机", true, false),
        new(SampleIds.Model(5), SampleIds.Provider(1), "nebula-embed-v2", "Nebula Embed v2", "embedding", 8_000, 40, 0, 0, ["embedding"], [], "向量嵌入，1024 维", true, false),
        new(SampleIds.Model(6), SampleIds.Provider(5), "echo-1", "Echo 示例", "chat", 128_000, 0, 0, 0, ["demo"], ["fast"], "无需密钥，回放内置的示例回答", true, false),
    ];

    public static IReadOnlyList<SpaceDto> Spaces { get; } =
    [
        new(SampleIds.Space(1), "导出功能改版", "围绕导出功能改版的资料与对话，回答要引用产品文档库。", 3),
        new(SampleIds.Space(2), "竞品分析", "整理三家竞品的功能与定价，结论要有出处。", 2),
        new(SampleIds.Space(3), "周报与纪要", "周报、周会纪要的整理与摘要。", 2),
    ];

    public static IReadOnlyList<FolderDto> Folders { get; } =
    [
        new(SampleIds.Folder(1), null, "导出功能改版", 1, 3),
        new(SampleIds.Folder(2), null, "竞品分析", 2, 2),
        new(SampleIds.Folder(3), null, "周报与纪要", 3, 2),
    ];

    /// <summary>The conversations (the first one has the full two-turn story). <c>DaysAgo</c>, <c>Hour</c>, <c>Minute</c> are relative to the moment the data is made.</summary>
    public static IReadOnlyList<SampleConversation> Conversations { get; } =
    [
        new(SampleIds.Conversation(1), "导出功能痛点分析", SampleIds.Folder(1), true, 0, 14, 32, SampleIds.Owner),
        new(SampleIds.Conversation(2), "解释这段导出 SQL 为什么慢", SampleIds.Folder(1), false, 0, 11, 5, SampleIds.Owner),
        new(SampleIds.Conversation(3), "竞品 B 的定价页拆解", SampleIds.Folder(2), false, 0, 10, 18, SampleIds.Owner),
        new(SampleIds.Conversation(4), "把工单周报改成管理层摘要", SampleIds.Folder(3), false, 0, 9, 40, SampleIds.Owner),
        new(SampleIds.Conversation(5), "用户访谈提纲：财务角色", null, false, 1, 17, 26, SampleIds.Owner),
        new(SampleIds.Conversation(6), "周会纪要整理（11/12）", SampleIds.Folder(3), false, 1, 15, 2, SampleIds.Owner),
        new(SampleIds.Conversation(7), "PRD：批量导出权限", SampleIds.Folder(1), false, 1, 10, 44, SampleIds.Owner),
        new(SampleIds.Conversation(8), "三家竞品的导出能力对照", SampleIds.Folder(2), false, 2, 16, 20, SampleIds.Owner),
        new(SampleIds.Conversation(9), "翻译：导出错误码文档", null, false, 3, 14, 8, SampleIds.Owner),
        new(SampleIds.Conversation(10), "NPS 开放题聚类", null, false, 4, 11, 30, SampleIds.Owner),
        new(SampleIds.Conversation(11), "接口限流方案评审", null, false, 6, 16, 0, SampleIds.Owner),
    ];

    public static SampleModel DefaultModel => Models.First(m => m.IsDefault);

    public static ModelDto ToDto(SampleModel m) => new(m.Id, m.ProviderId, Providers.First(p => p.Id == m.ProviderId).Name, m.Code, m.Name, m.Kind, m.Context, m.PriceIn, m.PriceOut,
        m.PriceCache, m.Caps, m.Efforts, m.Note, m.Enabled, m.IsDefault, 1);

    public static ProviderDto ToDto(SampleProvider p) => new(p.Id, p.Name, p.Protocol, p.BaseUrl, p.RequiresKey, p.KeyLast4 is not null, p.KeyLast4 is null ? null : "••••••••" + p.KeyLast4,
        true, p.Status, p.LatencyMs, p.LatencyMs is null ? null : DateTimeOffset.UtcNow.AddMinutes(-2), Models.Count(m => m.ProviderId == p.Id), 1);
}
