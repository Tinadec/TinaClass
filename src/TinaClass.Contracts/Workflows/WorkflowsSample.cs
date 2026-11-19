using TinaClass.Contracts.Sample;

namespace TinaClass.Contracts.Workflows;

/// <summary>The sample workflow "工单自动分诊" (v12): eight nodes and seven edges, run 842 waiting at the confirmation before the email is sent.</summary>
public static class WorkflowsSample
{
    public static FlowGraphDto Graph { get; } = new(
    [
        new("n1", NodeKinds.Trigger, "新工单", "触发器 · 工单系统", 30, 250),
        new("n2", NodeKinds.Model, "分类与定级", "Aurora Flash", 310, 250),
        new("n3", NodeKinds.Branch, "是否缺陷？", "条件分支", 590, 250),
        new("n4", NodeKinds.Retrieve, "检索知识库", "客服工单知识 · top 4", 870, 110),
        new("n5", NodeKinds.Model, "起草回复", "Aurora Flash", 1150, 110),
        new("n6", NodeKinds.Approval, "发送前确认", "人工审批", 1150, 270),
        new("n7", NodeKinds.Send, "发送邮件", "需要批准", 1150, 430),
        new("n8", NodeKinds.Tool, "归档到需求池", "本次未走到", 870, 410),
    ],
    [
        new("n1", "n2", null), new("n2", "n3", null), new("n3", "n4", "是"), new("n3", "n8", "否"), new("n4", "n5", null), new("n5", "n6", null), new("n6", "n7", null),
    ]);

    public static Guid FlowId => SampleIds.Flow(1);

    /// <summary>The state of each node in run 842: key, state, start, duration (ms), output.</summary>
    public static IReadOnlyList<FlowNodeRunDto> Run842 { get; } =
    [
        new("n1", NodeStates.Succeeded, 0, 100, "工单 #48213"),
        new("n2", NodeStates.Succeeded, 100, 1_200, "缺陷 · P1"),
        new("n3", NodeStates.Succeeded, 1_300, 50, "是"),
        new("n4", NodeStates.Succeeded, 1_350, 600, "4 个片段"),
        new("n5", NodeStates.Succeeded, 1_950, 6_800, "132 字"),
        new("n6", NodeStates.Waiting, 8_750, 0, null),
        new("n7", NodeStates.Pending, 0, 0, null),
        new("n8", NodeStates.Skipped, 0, 0, null),
    ];
}
