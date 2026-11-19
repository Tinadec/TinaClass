using TinaClass.Contracts.Sample;

namespace TinaClass.Contracts.Agents;

public sealed record SampleServer(Guid Id, string Name, string Kind, string Endpoint, string Status);

public sealed record SampleTool(Guid Id, Guid ServerId, string Name, string Description, string Risk);

public sealed record SampleAgent(Guid Id, string Name, string Description, string Instruction, string ModelCode, int MaxSteps, long MaxCost, string? Schedule, string Icon,
    int Runs, int SuccessPercent, Guid[] ToolIds, string[] Approvals);

public sealed record SampleStep(string Kind, string Title, string Detail, string State, int AtMs);

/// <summary>The sample agents, tool servers (one of them failing) and run 842 of the ticket triage agent that waits for approval to send an email.</summary>
public static class AgentsSample
{
    public static IReadOnlyList<SampleServer> Servers { get; } =
    [
        new(SampleIds.ToolServer(1), "内置工具", "builtin", "builtin://tools", "ok"),
        new(SampleIds.ToolServer(2), "工单系统 MCP", "mcp", "https://tickets.qiyun.example/mcp", "ok"),
        new(SampleIds.ToolServer(3), "本地文件 MCP", "mcp", "stdio://local-files", "ok"),
        new(SampleIds.ToolServer(4), "日历 MCP", "mcp", "https://calendar.qiyun.example/mcp", "error"),
    ];

    public static IReadOnlyList<SampleTool> Tools { get; } =
    [
        new(SampleIds.Tool(1), SampleIds.ToolServer(1), "知识库检索", "在授权的知识库里检索片段", ToolRisks.Read),
        new(SampleIds.Tool(2), SampleIds.ToolServer(1), "联网搜索", "搜索公开网页", ToolRisks.Read),
        new(SampleIds.Tool(3), SampleIds.ToolServer(1), "只读 SQL 查询", "对数据仓库执行只读查询", ToolRisks.Read),
        new(SampleIds.Tool(4), SampleIds.ToolServer(2), "查询账户套餐", "CRM 里的套餐与席位，只读", ToolRisks.Read),
        new(SampleIds.Tool(5), SampleIds.ToolServer(2), "创建或更新工单", "在工单系统里新建或修改工单", ToolRisks.Write),
        new(SampleIds.Tool(6), SampleIds.ToolServer(2), "发送邮件", "以客服名义对外发送邮件", ToolRisks.External),
        new(SampleIds.Tool(7), SampleIds.ToolServer(2), "查询工单", "按编号或条件查询工单", ToolRisks.Read),
        new(SampleIds.Tool(8), SampleIds.ToolServer(2), "分配工单", "指派处理人", ToolRisks.Write),
        new(SampleIds.Tool(9), SampleIds.ToolServer(2), "关闭工单", "关闭并记录结论", ToolRisks.Write),
        new(SampleIds.Tool(10), SampleIds.ToolServer(2), "工单评论", "给工单加内部评论", ToolRisks.Write),
        new(SampleIds.Tool(11), SampleIds.ToolServer(3), "读取文件", "读取共享目录里的文件", ToolRisks.Read),
        new(SampleIds.Tool(12), SampleIds.ToolServer(3), "列出目录", "列出共享目录", ToolRisks.Read),
        new(SampleIds.Tool(13), SampleIds.ToolServer(3), "写入文件", "写入共享目录", ToolRisks.Write),
        new(SampleIds.Tool(14), SampleIds.ToolServer(3), "搜索文件", "按文件名搜索", ToolRisks.Read),
        new(SampleIds.Tool(15), SampleIds.ToolServer(4), "查询日程", "读取日历事件", ToolRisks.Read),
        new(SampleIds.Tool(16), SampleIds.ToolServer(4), "创建日程", "在日历里新建事件", ToolRisks.Write),
    ];

    private static Guid[] T(params int[] n) => [.. n.Select(SampleIds.Tool)];

    public static IReadOnlyList<SampleAgent> Agents { get; } =
    [
        new(SampleIds.Agent(1), "需求分析师", "把零散的用户反馈整理成带证据的需求条目，自动关联调研与工单。", "你是产品需求分析师。把用户反馈整理成需求条目，每条都要带出处。", "aurora-pro", 20, 200, null, "bulb", 126, 97,
            T(1, 3, 7, 4), [ToolApprovals.Auto, ToolApprovals.Auto, ToolApprovals.Auto, ToolApprovals.Auto]),
        new(SampleIds.Agent(2), "工单分诊员", "给新工单分类、定级并起草回复；对外发送前需要人工批准。",
            "你是客服团队的分诊助手。收到新工单后：先判断类别（缺陷、咨询、需求）并给出 P0–P3 定级；缺陷类先检索「客服工单知识」里的已知问题，再起草一封不超过 150 字的回复。{{客户套餐}} 为企业版时优先级上调一级。涉及退款或赔付时不要承诺，转人工。",
            "aurora-flash", 20, 50, null, "ticket", 842, 94, T(1, 4, 5, 6), [ToolApprovals.Auto, ToolApprovals.Auto, ToolApprovals.Ask, ToolApprovals.Always]),
        new(SampleIds.Agent(3), "周报助手", "每周五 17:00 汇总项目进展和风险，生成部门周报草稿。", "汇总本周工单与进展，生成周报草稿。", "aurora-flash", 12, 30, "每周五 17:00", "calendar", 12, 100,
            T(1, 7, 15), [ToolApprovals.Auto, ToolApprovals.Auto, ToolApprovals.Auto]),
        new(SampleIds.Agent(4), "SQL 数据助手", "把业务问题翻译成只读 SQL，执行后用人话解释结果。", "把问题翻译成只读 SQL，执行并解释。", "vega-reasoner", 10, 300, null, "database", 58, 91,
            T(3, 1), [ToolApprovals.Auto, ToolApprovals.Auto]),
        new(SampleIds.Agent(5), "竞品雷达", "每天扫描竞品的更新日志与定价页，有变化时提醒。", "扫描竞品更新日志与定价页，有变化时提醒。", "aurora-pro", 15, 200, "每天 08:30", "globe", 31, 96,
            T(2, 1, 11), [ToolApprovals.Auto, ToolApprovals.Auto, ToolApprovals.Auto]),
        new(SampleIds.Agent(6), "会议纪要官", "录音转写后提炼结论与待办，全程在本机运行。", "提炼会议结论与待办。", "orion-local-8b", 8, 0, null, "mic", 44, 98,
            T(11, 12), [ToolApprovals.Auto, ToolApprovals.Auto]),
    ];

    public static IReadOnlyList<SampleStep> Run842 { get; } =
    [
        new("think", "收到工单 #48213", "“导出 12 万行卡在 80%，已经等了十分钟” · 远帆物流", "done", 0),
        new("think", "判断类别", "像是已知的大文件导出超时，先查知识库确认", "done", 2_000),
        new("tool", "检索知识库 客服工单知识", "4 个片段 · 0.6s", "done", 3_000),
        new("tool", "查询账户套餐", "企业版 · 席位 120 · 0.9s", "done", 5_000),
        new("think", "定级：缺陷 · P1", "企业版客户，优先级上调一级", "done", 7_000),
        new("reply", "起草回复", "132 字，已附临时解决办法", "done", 14_000),
        new("approval", "等待批准：发送邮件", "对外发送需要你确认", "waiting", 18_000),
    ];

    public static IReadOnlyList<KeyValuePair<string, string>> ApprovalArguments { get; } =
    [
        new("收件人", "ops@yuanfan-logistics.example"),
        new("主题", "关于导出卡在 80% 的处理办法（工单 #48213）"),
        new("正文", "您好，已确认是大文件导出超时的已知问题。临时办法：按月份拆分导出，异步导出将在下个版本上线。"),
        new("风险", "对外发送，需要批准"),
    ];
}
