using TinaClass.Contracts.Sample;

namespace TinaClass.Contracts.Prompts;

public sealed record SamplePrompt(Guid Id, string Name, string Command, string System, string User, int Version, int Uses, Guid OwnerId);

/// <summary>Seven sample prompts; <c>/review</c> is at v7 with three test cases that all pass.</summary>
public static class PromptsSample
{
    public static IReadOnlyList<SamplePrompt> Prompts { get; } =
    [
        new(SampleIds.Prompt(1), "需求评审清单", "/review", "你是资深产品评审人，按清单逐项检查需求文档，每项限制为三条意见。", "请评审下面的需求文档（读者：{{读者角色}}）：\n{{文档}}", 7, 312, SampleIds.Owner),
        new(SampleIds.Prompt(2), "PRD 润色", "/polish", "你是 PRD 编辑，保持原意，让表达更精确、结构更清晰。", "润色下面的 PRD 段落：\n{{文本}}", 3, 188, SampleIds.Admin),
        new(SampleIds.Prompt(3), "用户访谈提纲", "/interview", "你是用户研究员，写开放式、不带引导的访谈问题。", "为{{角色}}写一份{{时长}}分钟的访谈提纲，主题：{{主题}}", 5, 64, SampleIds.Zhou),
        new(SampleIds.Prompt(4), "SQL 解释器", "/sql", "你是数据库专家，解释查询做了什么、哪里慢、怎么改。", "解释这段 SQL：\n{{sql}}", 2, 141, SampleIds.He),
        new(SampleIds.Prompt(5), "周报生成", "/weekly", "你是项目经理助理，生成简明的部门周报。", "根据{{进展}}和{{风险}}生成本周周报。", 9, 52, SampleIds.Tang),
        new(SampleIds.Prompt(6), "竞品对比表", "/compare", "你是竞品分析师，用表格对比，每个结论要有出处。", "对比{{产品}}与{{对手}}在{{维度}}上的差异。", 4, 37, SampleIds.Owner),
        new(SampleIds.Prompt(7), "错误日志诊断", "/diagnose", "你是后端工程师，根据日志定位最可能的原因并给出验证步骤。", "诊断这段日志：\n{{日志}}", 1, 9, SampleIds.He),
    ];

    public static IReadOnlyList<(string Note, int Number, Guid By, int DaysAgo)> ReviewVersions { get; } =
    [
        ("补充权限模型检查", 7, SampleIds.Owner, 2),
        ("每项限制为三条", 6, SampleIds.Owner, 11),
        ("增加读者角色变量", 5, SampleIds.Admin, 24),
    ];

    public static IReadOnlyList<(string Name, string Input, string Expect)> ReviewCases { get; } =
    [
        ("批量导出权限", "{\"文档\":\"批量导出默认对所有成员开放\",\"读者角色\":\"安全负责人\"}", "权限"),
        ("导出邮件通知", "{\"文档\":\"导出完成后发邮件通知\",\"读者角色\":\"客服\"}", "通知"),
        ("历史记录保留", "{\"文档\":\"导出记录保留 24 小时\",\"读者角色\":\"运营\"}", "保留"),
    ];
}
