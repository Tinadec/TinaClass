using TinaClass.Contracts.Sample;

namespace TinaClass.Contracts.Team;

/// <summary>A few audit entries and a pending invitation of the sample workspace.</summary>
public static class TeamSample
{
    public static IReadOnlyList<(Guid Actor, string Action, string TargetKind, string Detail, int DaysAgo, int Hour)> Audit { get; } =
    [
        (SampleIds.Owner, "settings.updated", "workspace", "月预算设为 ¥2,000.00", 12, 10),
        (SampleIds.Admin, "provider.created", "provider", "添加服务商 Halo 网关", 9, 15),
        (SampleIds.Owner, "provider.key_changed", "provider", "替换 Northlight Cloud 的密钥", 6, 9),
        (SampleIds.Admin, "member.invited", "member", "邀请 xu.mingzhe@qiyun.example 为成员", 2, 16),
        (SampleIds.Owner, "agents.approval_approved", "approval", "批准发送邮件（工单 #48190）", 1, 11),
    ];

    public const string InvitedEmail = "xu.mingzhe@qiyun.example";
}
