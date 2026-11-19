using TinaClass.Contracts.Chat;

namespace TinaClass.Contracts.Sample;

/// <summary>One source of the sample answer: a chunk of a sample document with its relevance.</summary>
public sealed record SampleSource(int Position, int DocumentNo, int ChunkNo, string Title, string Where, double Score, string Quote, string BaseName);

/// <summary>The sample conversations' messages. The first conversation has two turns; the second turn has two answers (2 / 2) and the second is shown.</summary>
public static class SampleChat
{
    public const string FirstQuestion = "先帮我看下 Q3 调研里，用户对“导出”的原话都有哪些？";
    public const string FirstAnswer = "共找到 23 条提到导出的原话，集中在三类：**等待时间长**（11 条）、**格式错乱**（7 条）、**找不到历史导出**（5 条）。需要我按角色再拆一下吗？";
    public const string Question = "结合工单数据，帮我总结导出功能的三大痛点，并给出下个迭代的优先级建议。";
    public const string Reasoning = "用户要的是排序而不是罗列。先从调研报告取定性结论，再用近 90 天的工单量校验影响面，最后按“影响用户数 × 修复成本”给优先级。";

    public static IReadOnlyList<SampleSource> Sources { get; } =
    [
        new(1, 1, 1, "2026 Q3 用户调研报告.pdf", "第 14 页", 0.91, "在 312 份有效问卷中，38% 的受访者提到大文件导出等待时间过长；超过 5 万行时平均等待 4 分 12 秒，期间无法离开页面。", "用户调研与访谈"),
        new(2, 2, 2, "导出模块技术方案 v2.md", "§3.2 异步队列", 0.86, "导出任务进入异步队列，由工作进程分片生成文件；完成后通过站内信与邮件通知，文件保留期可配置（默认 7 天）。", "产品文档库"),
        new(3, 3, 3, "客服工单周报 第 37 周.xlsx", "导出分类", 0.81, "近 90 天导出相关工单 811 张：超时或卡住 412、格式错乱 268、找不到历史导出 131。", "客服工单知识"),
    ];

    public const string Answer = """
        导出功能的问题集中在三点，按影响面排序：

        1. **大文件导出等待过长**：超 5 万行平均等 4 分 12 秒，相关工单 412 张，占 51%[1][3]。
        2. **Excel 格式错乱**：合并单元格和日期列在 WPS 里错位，268 张工单[1]。
        3. **找不到历史导出**：记录只保留 24 小时，131 张工单多为重复导出[3]。

        | 痛点 | 影响用户 | 工单量 | 修复成本 | 建议 |
        |---|---|---|---|---|
        | 等待过长 | 38% | 412 | 中，已有异步方案 | **P0**，本迭代 |
        | 格式错乱 | 21% | 268 | 低 | **P1**，本迭代 |
        | 历史记录 | 12% | 131 | 低 | **P2**，下迭代 |

        建议本迭代先上异步导出与邮件通知（见技术方案 §3.2[2]），格式问题一并修复。
        """;

    public const string ShortAnswer = "三个痛点：等待过长、格式错乱、找不到历史导出。建议先做异步导出。";

    public static IReadOnlyList<PlanStepDto> Plan(string state = "done") =>
    [
        new("s1", "检索调研报告里的导出结论", state, 800),
        new("s2", "查询近 90 天导出相关工单", state, 1400),
        new("s3", "交叉核对影响面", state, 300),
        new("s4", "生成优先级建议", state, 600),
    ];

    public static MessageBlock[] ToolBlocks(string state = "done") =>
    [
        new("tool_call", ToolId: "t1", Title: "检索知识库 产品文档库、用户调研与访谈", Icon: "book", State: state, Arguments: "query=导出 痛点 等待 格式 历史记录\ntop_k=6\n阈值=0.72\n重排=开",
            Result: "2026 Q3 用户调研报告 · p.14 · 0.91\n导出模块技术方案 v2 · §3.2 · 0.86\n客服工单周报 第 37 周 · 0.81", Summary: "6 个片段 · 0.8s", Ms: 800),
        new("tool_call", ToolId: "t2", Title: "只读 SQL 查询 tickets", Icon: "database", State: state,
            Arguments: "SELECT category, COUNT(*) FROM tickets WHERE module='export' AND created_at >= :d90 GROUP BY 1",
            Result: "超时或卡住  412\n格式错乱  268\n找不到历史  131", Summary: "3 行 · 1.4s", Ms: 1400),
    ];

    public static MessageUsageDto Usage { get; } = new(41_600, 1_284, 12_900, 420, 14_900, 84, false);

    public static IReadOnlyList<CitationDto> Citations(Guid messageId) =>
        [.. Sources.Select(s => new CitationDto(SampleIds.Message(900 + s.Position), messageId, s.Position, SampleIds.Document(s.DocumentNo), SampleIds.Chunk(s.ChunkNo), s.Score, s.Quote, s.Title, s.Where))];

    public static MessageDto Message(int n, Guid conversation, Guid? parent, string role, IReadOnlyList<MessageBlock> blocks, DateTimeOffset at, string status = MessageStatuses.Done,
        string? model = null, MessageUsageDto? usage = null, int siblingIndex = 0, int siblingCount = 1, IReadOnlyList<CitationDto>? citations = null) =>
        new(SampleIds.Message(n), conversation, parent, null, role, blocks, status, null, model, usage, null, at, siblingIndex, siblingCount, citations ?? []);

    private static DateTimeOffset At(DateTimeOffset now, int daysAgo, int hour, int minute) =>
        new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).AddDays(-daysAgo).AddHours(hour).AddMinutes(minute);

    /// <summary>The messages of a sample conversation, in tree order (the older sibling answer comes before the shown one).</summary>
    public static IReadOnlyList<MessageDto> Messages(Guid conversationId, DateTimeOffset now)
    {
        var conversation = SampleCore.Conversations.FirstOrDefault(c => c.Id == conversationId);
        if (conversation is null)
        {
            return [];
        }
        var model = SampleCore.DefaultModel.Code;
        var number = SampleCore.Conversations.ToList().IndexOf(conversation) + 1;
        var at = At(now, conversation.DaysAgo, conversation.Hour, conversation.Minute);
        if (number != 1)
        {
            var q = Message(number * 10 + 1, conversationId, null, MessageRoles.User, [new("text", conversation.Title)], at.AddMinutes(-2));
            var a = Message(number * 10 + 2, conversationId, q.Id, MessageRoles.Assistant, [new("text", ShortAnswerFor(conversation.Title))], at, model: model,
                usage: new MessageUsageDto(2_100, 310, 0, 380, 4_200, 6, false));
            return [q, a];
        }
        var m1 = Message(11, conversationId, null, MessageRoles.User, [new("text", FirstQuestion)], At(now, 0, 14, 28));
        var m2 = Message(12, conversationId, m1.Id, MessageRoles.Assistant, [new("text", FirstAnswer)], At(now, 0, 14, 29), model: model, usage: new MessageUsageDto(8_200, 96, 0, 410, 2_300, 9, false));
        var m3 = Message(13, conversationId, m2.Id, MessageRoles.User, [new("text", Question)], At(now, 0, 14, 31));
        var older = Message(14, conversationId, m3.Id, MessageRoles.Assistant, [new("text", ShortAnswer)], At(now, 0, 14, 31).AddSeconds(20), model: "aurora-flash",
            usage: new MessageUsageDto(9_000, 64, 0, 360, 2_000, 2, false), siblingIndex: 0, siblingCount: 2);
        MessageBlock[] blocks =
        [
            new("reasoning", Reasoning, Ms: 6_000),
            new("plan", Steps: Plan()),
            .. ToolBlocks(),
            new("text", Answer),
        ];
        var shown = Message(15, conversationId, m3.Id, MessageRoles.Assistant, blocks, At(now, 0, 14, 32), model: model, usage: Usage, siblingIndex: 1, siblingCount: 2,
            citations: Citations(SampleIds.Message(15)));
        return [m1, m2, m3, older, shown];
    }

    /// <summary>The leaf of the shown branch of a sample conversation.</summary>
    public static Guid LeafOf(Guid conversationId) => Messages(conversationId, DateTimeOffset.UtcNow).Last().Id;

    private static string ShortAnswerFor(string title) => title switch
    {
        "解释这段导出 SQL 为什么慢" => "这条 SQL 对 `orders` 全表扫描后再排序：`created_at` 上没有覆盖 `status` 的复合索引，建议改成 `(status, created_at)` 并去掉 `SELECT *`。",
        "竞品 B 的定价页拆解" => "竞品 B 按席位和导出额度分三档，导出额度是主要的升级触发点；免费档每月只能导出 3 次。",
        "把工单周报改成管理层摘要" => "本周工单 811 张，较上周下降 6%。导出超时仍是第一大类，异步导出方案已排入本迭代。",
        "用户访谈提纲：财务角色" => "建议围绕“对账导出”展开：频率、数据量、格式要求、出错后怎么补救，各留 3 个追问。",
        "周会纪要整理（11/12）" => "结论：异步导出本迭代上线；格式错乱并入同一版本。待办：何砚秋补充技术方案，唐书瑶整理工单样本。",
        "PRD：批量导出权限" => "建议把“批量导出”拆成独立权限点，默认只授予管理员；导出记录保留 30 天并写入审计。",
        "三家竞品的导出能力对照" => "三家都支持异步导出，只有竞品 A 提供定时导出；历史记录保留期从 7 天到 90 天不等。",
        "翻译：导出错误码文档" => "已翻译 24 个错误码，其中 `E1042`（导出行数超限）与 `E1043`（文件正在生成）的措辞建议与产品保持一致。",
        "NPS 开放题聚类" => "共聚成 6 类，占比最高的是“导出等待”（31%），其次是“权限设置复杂”（18%）。",
        "接口限流方案评审" => "建议按租户加令牌桶：默认每分钟 600 次，导出接口单独限 10 次；超限返回 429 并带 Retry-After。",
        _ => "好的，我来整理一下。",
    };
}
