using TinaClass.Contracts.Sample;

namespace TinaClass.Contracts.Knowledge;

public sealed record SampleBase(Guid Id, string Name, string Description, int Documents, int Chunks, string Visibility);

public sealed record SampleDocument(Guid Id, Guid BaseId, string Name, string Mime, long Size, string State, int Progress, int Chunks, string? Error, Guid UploaderId, int MinutesAgo);

public sealed record SampleChunk(Guid Id, Guid DocumentId, int Ordinal, string Text, int? Page, string? Heading);

/// <summary>The sample knowledge bases (128, 46, 312 and 23 documents), the nine documents of the first one that tell every state, and the chunks retrieval finds.</summary>
public static class KnowledgeSample
{
    public static IReadOnlyList<SampleBase> Bases { get; } =
    [
        new(SampleIds.Base(1), "产品文档库", "PRD、技术方案、变更日志", 128, 4312, "workspace"),
        new(SampleIds.Base(2), "用户调研与访谈", "问卷、访谈纪要、NPS 开放题", 46, 1980, "space"),
        new(SampleIds.Base(3), "客服工单知识", "工单周报与标准回复，每日同步", 312, 9845, "workspace"),
        new(SampleIds.Base(4), "法务与合规", "合同模板、隐私条款", 23, 760, "private"),
    ];

    /// <summary>The nine named documents of the first base: five ready, two in progress (62% and 28%), one queued, one failed.</summary>
    public static IReadOnlyList<SampleDocument> Documents { get; } =
    [
        new(SampleIds.Document(1), SampleIds.Base(1), "2026 Q3 用户调研报告.pdf", "application/pdf", 4_800_000, DocumentStates.Ready, 100, 214, null, SampleIds.Admin, 4_300),
        new(SampleIds.Document(2), SampleIds.Base(1), "导出模块技术方案 v2.md", "text/markdown", 38_000, DocumentStates.Ready, 100, 46, null, SampleIds.He, 2_900),
        new(SampleIds.Document(3), SampleIds.Base(1), "客服工单周报 第 37 周.xlsx", "application/vnd.ms-excel", 1_200_000, DocumentStates.Ready, 100, 132, null, SampleIds.Tang, 3_600),
        new(SampleIds.Document(4), SampleIds.Base(1), "权限模型说明（RBAC）.docx", "application/msword", 620_000, DocumentStates.Ready, 100, 58, null, SampleIds.Zhou, 11_000),
        new(SampleIds.Document(5), SampleIds.Base(1), "数据字典 v5.xlsx", "application/vnd.ms-excel", 880_000, DocumentStates.Ready, 100, 87, null, SampleIds.Admin, 14_000),
        new(SampleIds.Document(6), SampleIds.Base(1), "API 变更日志 2026.md", "text/markdown", 96_000, DocumentStates.Embedding, 62, 0, null, SampleIds.He, 0),
        new(SampleIds.Document(7), SampleIds.Base(1), "竞品功能对照表.xlsx", "application/vnd.ms-excel", 2_300_000, DocumentStates.Parsing, 28, 0, null, SampleIds.Owner, 0),
        new(SampleIds.Document(8), SampleIds.Base(1), "新人上手指南.pdf", "application/pdf", 12_600_000, DocumentStates.Queued, 0, 0, null, SampleIds.Admin, 2),
        new(SampleIds.Document(9), SampleIds.Base(1), "旧版导出流程图.png", "image/png", 3_100_000, DocumentStates.Failed, 0, 0, "图片里没有可识别的文字", SampleIds.Tang, 1_500),
    ];

    /// <summary>Spreads <paramref name="total"/> over <paramref name="count"/> documents so that the parts add up exactly.</summary>
    public static IEnumerable<int> Spread(int total, int count)
    {
        for (var i = 0; i < count; i++)
        {
            yield return total / count + (i < total % count ? 1 : 0);
        }
    }

    /// <summary>Chunks with real text, enough for the retrieval test and the cited sources; the other documents only carry their chunk count.</summary>
    public static IReadOnlyList<SampleChunk> Chunks { get; } =
    [
        new(SampleIds.Chunk(1), SampleIds.Document(1), 14, "在 312 份有效问卷中，38% 的受访者提到大文件导出等待时间过长；超过 5 万行时平均等待 4 分 12 秒，期间无法离开页面。", 14, "导出体验"),
        new(SampleIds.Chunk(2), SampleIds.Document(2), 7, "导出任务进入异步队列，由工作进程分片生成文件；完成后通过站内信与邮件通知，文件保留期可配置（默认 7 天）。", null, "3.2 异步队列"),
        new(SampleIds.Chunk(3), SampleIds.Document(3), 3, "近 90 天导出相关工单 811 张：超时或卡住 412、格式错乱 268、找不到历史导出 131。", null, "导出分类"),
        new(SampleIds.Chunk(4), SampleIds.Document(2), 6, "同步导出在超过 5 万行时会占满一个请求线程，生成期间无法响应其他操作，这是等待慢的主要原因。", null, "3.1 现状"),
        new(SampleIds.Chunk(5), SampleIds.Document(1), 15, "大文件导出平均等待 4 分 12 秒，38% 的受访者认为必须守着页面。", 14, "导出体验"),
        new(SampleIds.Chunk(6), SampleIds.Document(4), 2, "导出权限分为查看、导出和批量导出三级，批量导出默认只授予管理员，导出记录保留 30 天。", null, "导出权限"),
        new(SampleIds.Chunk(7), SampleIds.Document(5), 9, "export_job 表记录导出任务：id、owner_id、rows、state、created_at；state 取 queued、running、done、failed。", null, "export_job"),
        new(SampleIds.Chunk(8), SampleIds.Document(2), 8, "合并单元格与日期列的格式由模板文件决定，WPS 与 Excel 对日期序列号的解释不同，需要显式写入文本格式。", null, "3.3 文件格式"),
        new(SampleIds.Chunk(9), SampleIds.Document(1), 16, "格式错乱的反馈集中在合并单元格和日期列，使用 WPS 的用户占其中的六成。", 15, "导出体验"),
        new(SampleIds.Chunk(10), SampleIds.Document(3), 4, "找不到历史导出的工单里，多数用户在 24 小时后才回来下载，记录已经过期。", null, "导出分类"),
    ];
}
