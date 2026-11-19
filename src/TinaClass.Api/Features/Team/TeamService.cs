using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TinaClass.Api.Common.Auditing;
using TinaClass.Api.Common.Email;
using TinaClass.Api.Common.Events;
using TinaClass.Api.Common.Http;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Common.Seams;
using TinaClass.Api.Features.Auth;
using TinaClass.Api.Features.Team.Data;
using TinaClass.Contracts;
using TinaClass.Contracts.Common;
using TinaClass.Contracts.Team;

namespace TinaClass.Api.Features.Team;

/// <summary><c>Team:InviteDays</c> (how long an invitation lasts, 7) and <c>Team:InviteUrl</c> (the link in the email; the token is appended).</summary>
public sealed class TeamOptions
{
    public int InviteDays { get; set; } = 7;

    public string InviteUrl { get; set; } = "tinaclass://accept-invite?token=";
}

public static class TeamAuditActions
{
    public const string MemberInvited = "member.invited";
    public const string MemberJoined = "member.joined";
    public const string MemberRoleChanged = "member.role_changed";
    public const string MemberDisabled = "member.disabled";
    public const string MemberEnabled = "member.enabled";
    public const string InviteRevoked = "member.invite_revoked";
}

/// <summary>The audit seam's implementation: every feature's <see cref="IAuditLog"/> record becomes a row of <c>team_audit</c>.</summary>
internal sealed class TeamAuditLog(TeamDbContext db, IActorContext actors, TimeProvider time) : IAuditLog
{
    public async Task RecordAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        var actor = await actors.GetAsync(cancellationToken);
        db.Audit.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            ActorId = actor?.Id,
            ActorName = Cut(record.ActorName ?? actor?.Name ?? "anonymous", 120),
            Action = Cut(record.Action, 60),
            TargetKind = Cut(record.ResourceType, 40),
            TargetId = Cut(record.ResourceId ?? "", 80),
            Detail = Cut(record.ResourceName ?? "", 500),
            At = time.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string Cut(string text, int length) => text.Length <= length ? text : text[..length];
}

/// <summary>
/// Members (accounts, through <see cref="IUserDirectory"/>), invitations and the audit log (design 4.8). Only the owner may make an owner or change one;
/// the last active owner can be neither demoted nor disabled (409 <c>last_owner</c>).
/// </summary>
public sealed class TeamService(TeamDbContext db, IActorContext actors, IUserDirectory users, IAuditLog audit, IEventPublisher events, IEmailSender email, IOptions<TeamOptions> options, TimeProvider time)
{
    public async Task<IReadOnlyList<MemberDto>> MembersAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var members = (await users.ListAsync(cancellationToken)).Select(u => new MemberDto(u.Id, u.Name, u.Email, u.RoleId, u.Disabled ? "disabled" : "active", u.CreatedAt)).ToList();
        var invites = await db.Invites.AsNoTracking().Where(i => i.AcceptedAt == null && i.ExpiresAt > now).OrderBy(i => i.CreatedAt).ThenBy(i => i.Id).ToListAsync(cancellationToken);
        members.AddRange(invites.Select(i => new MemberDto(i.Id, i.Email.Split('@')[0], i.Email, i.RoleId, "invited", null)));
        return members;
    }

    public async Task<MemberDto> UpdateAsync(Guid id, UpdateMemberRequest request, CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        var invite = await db.Invites.SingleOrDefaultAsync(i => i.Id == id && i.AcceptedAt == null, cancellationToken);
        if (invite is not null)
        {
            if (request.RoleId is not null)
            {
                RequireRole(actor, request.RoleId);
                invite.RoleId = request.RoleId;
                await db.SaveChangesAsync(cancellationToken);
            }
            return new MemberDto(invite.Id, invite.Email.Split('@')[0], invite.Email, invite.RoleId, "invited", null);
        }

        var user = await users.FindAsync(id, cancellationToken) ?? throw ApiException.NotFound("The member");
        if (user.RoleId == Roles.Owner && actor.RoleId != Roles.Owner)
        {
            throw ApiException.Forbidden("Only an owner can change an owner.");
        }
        var roleId = user.RoleId;
        var disabled = user.Disabled;
        if (request.RoleId is not null && request.RoleId != user.RoleId)
        {
            RequireRole(actor, request.RoleId);
            if (user.RoleId == Roles.Owner)
            {
                await EnsureAnotherOwnerAsync(user.Id, cancellationToken);
            }
            await users.ChangeRoleAsync(user.Id, request.RoleId, cancellationToken);
            roleId = request.RoleId;
            await audit.RecordAsync(new AuditRecord(TeamAuditActions.MemberRoleChanged, "member", user.Id.ToString(), $"{user.Email}: {user.RoleId} -> {request.RoleId}"), cancellationToken);
        }
        if (request.Disabled is { } wanted && wanted != user.Disabled)
        {
            if (wanted)
            {
                await DisableAsync(actor, user, cancellationToken);
            }
            else
            {
                await users.SetDisabledAsync(user.Id, false, cancellationToken);
                await audit.RecordAsync(new AuditRecord(TeamAuditActions.MemberEnabled, "member", user.Id.ToString(), user.Email), cancellationToken);
            }
            disabled = wanted;
        }
        return new MemberDto(user.Id, user.Name, user.Email, roleId, disabled ? "disabled" : "active", user.CreatedAt);
    }

    /// <summary>Removing a person turns the account off (their conversations and runs stay); removing an invitation revokes it.</summary>
    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        var invite = await db.Invites.SingleOrDefaultAsync(i => i.Id == id && i.AcceptedAt == null, cancellationToken);
        if (invite is not null)
        {
            db.Invites.Remove(invite);
            await db.SaveChangesAsync(cancellationToken);
            await audit.RecordAsync(new AuditRecord(TeamAuditActions.InviteRevoked, "member", invite.Id.ToString(), invite.Email), cancellationToken);
            return;
        }
        var user = await users.FindAsync(id, cancellationToken) ?? throw ApiException.NotFound("The member");
        if (user.RoleId == Roles.Owner && actor.RoleId != Roles.Owner)
        {
            throw ApiException.Forbidden("Only an owner can remove an owner.");
        }
        if (!user.Disabled)
        {
            await DisableAsync(actor, user, cancellationToken);
        }
    }

    private async Task DisableAsync(Actor actor, DirectoryUser user, CancellationToken cancellationToken)
    {
        if (actor.Id == user.Id)
        {
            throw ApiException.Forbidden("You cannot turn off your own account.");
        }
        if (user.RoleId == Roles.Owner)
        {
            await EnsureAnotherOwnerAsync(user.Id, cancellationToken);
        }
        await users.SetDisabledAsync(user.Id, true, cancellationToken);
        await audit.RecordAsync(new AuditRecord(TeamAuditActions.MemberDisabled, "member", user.Id.ToString(), user.Email), cancellationToken);
    }

    private async Task EnsureAnotherOwnerAsync(Guid owner, CancellationToken cancellationToken)
    {
        var others = (await users.ListAsync(cancellationToken)).Count(u => u.RoleId == Roles.Owner && !u.Disabled && u.Id != owner);
        if (others == 0)
        {
            throw ApiException.Conflict(ErrorCodes.LastOwner, "The workspace needs at least one active owner.");
        }
    }

    private static void RequireRole(Actor actor, string roleId)
    {
        if (!Roles.Exists(roleId))
        {
            throw ApiException.Invalid("roleId", "Unknown role.");
        }
        if (roleId == Roles.Owner && actor.RoleId != Roles.Owner)
        {
            throw ApiException.Forbidden("Only an owner can make an owner.");
        }
    }

    public async Task<InviteDto> InviteAsync(InviteRequest request, CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        var validator = new Validator().Text("email", request.Email, 254);
        if (!string.IsNullOrWhiteSpace(request.Email) && !Passwords.IsEmail(request.Email.Trim()))
        {
            validator.Add("email", "Not a valid email address.");
        }
        validator.ThrowIfInvalid();
        RequireRole(actor, request.RoleId);
        var address = request.Email.Trim();
        var normalized = Passwords.Normalize(address);
        if (await users.FindByEmailAsync(address, cancellationToken) is not null)
        {
            throw ApiException.Conflict(ErrorCodes.Conflict, "A person with this email is already in the workspace.");
        }
        // One live invitation per address: a new one replaces the old link.
        await db.Invites.Where(i => i.NormalizedEmail == normalized && i.AcceptedAt == null).ExecuteDeleteAsync(cancellationToken);
        var now = time.GetUtcNow();
        var token = Secrets.Token();
        var invite = new Invite
        {
            Id = Guid.NewGuid(),
            Email = address,
            NormalizedEmail = normalized,
            RoleId = request.RoleId,
            TokenHash = Secrets.Hash(token),
            InvitedBy = actor.Id ?? Guid.Empty,
            CreatedAt = now,
            ExpiresAt = now.AddDays(Math.Max(options.Value.InviteDays, 1)),
        };
        db.Invites.Add(invite);
        await db.SaveChangesAsync(cancellationToken);
        await email.SendAsync(new EmailMessage(address, "You are invited to TinaClass", $"{actor.Name} invited you to a TinaClass workspace as {request.RoleId}. Open {options.Value.InviteUrl}{token} to accept; the link works until {invite.ExpiresAt:yyyy-MM-dd}."), cancellationToken);
        await audit.RecordAsync(new AuditRecord(TeamAuditActions.MemberInvited, "member", invite.Id.ToString(), $"{address} ({request.RoleId})"), cancellationToken);
        var shown = new InviteDto(invite.Id, address, invite.RoleId, invite.ExpiresAt, null);
        await events.PublishAsync(TeamEvents.MemberInvited, shown, ContractsJsonContext.Default.InviteDto, cancellationToken);
        return shown with { Token = token };
    }

    /// <summary>The invitee's side: anonymous, the token is the credential. A used, expired or unknown token is the same 404.</summary>
    public async Task<MemberDto> AcceptAsync(AcceptInviteRequest request, CancellationToken cancellationToken)
    {
        new Validator().Text("token", request.Token, 200).Text("name", request.Name, 120).Text("password", request.Password, 200).ThrowIfInvalid();
        var hash = Secrets.Hash(request.Token.Trim());
        var now = time.GetUtcNow();
        var invite = await db.Invites.SingleOrDefaultAsync(i => i.TokenHash == hash && i.AcceptedAt == null && i.ExpiresAt > now, cancellationToken) ?? throw ApiException.NotFound("The invitation");
        var user = await users.CreateAsync(invite.Email, request.Name, request.Password, invite.RoleId, cancellationToken);
        // Claim it once: a second accept with the same token matches no row.
        var claimed = await db.Invites.Where(i => i.Id == invite.Id && i.AcceptedAt == null).ExecuteUpdateAsync(s => s.SetProperty(i => i.AcceptedAt, now), cancellationToken);
        if (claimed == 0)
        {
            throw ApiException.NotFound("The invitation");
        }
        await audit.RecordAsync(new AuditRecord(TeamAuditActions.MemberJoined, "member", user.Id.ToString(), user.Email, user.Name), cancellationToken);
        var dto = new MemberDto(user.Id, user.Name, user.Email, user.RoleId, "active", user.CreatedAt);
        await events.PublishAsync(TeamEvents.MemberJoined, dto, ContractsJsonContext.Default.MemberDto, cancellationToken);
        return dto;
    }

    public async Task<Paged<AuditEntryDto>> AuditAsync(string? action, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var paging = PageRequest.Of(page, pageSize);
        var query = db.Audit.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(action))
        {
            var prefix = action.Trim();
            query = query.Where(a => a.Action.StartsWith(prefix));
        }
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(a => a.At).ThenBy(a => a.Id).Skip(paging.Skip).Take(paging.PageSize)
                              .Select(a => new AuditEntryDto(a.Id, a.ActorName, a.Action, a.TargetKind, a.TargetId, a.Detail, a.At)).ToListAsync(cancellationToken);
        return paging.Wrap(rows, total);
    }
}
