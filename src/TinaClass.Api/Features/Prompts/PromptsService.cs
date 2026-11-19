using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TinaClass.Api.Common.Auditing;
using TinaClass.Api.Common.Events;
using TinaClass.Api.Common.Http;
using TinaClass.Api.Common.Persistence;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Common.Seams;
using TinaClass.Api.Features.Prompts.Data;
using TinaClass.Contracts;
using TinaClass.Contracts.Chat;
using TinaClass.Contracts.Common;
using TinaClass.Contracts.Prompts;

namespace TinaClass.Api.Features.Prompts;

public static class PromptsAudit
{
    public const string Created = "prompt.created";
    public const string Updated = "prompt.updated";
    public const string Deleted = "prompt.deleted";
}

/// <summary>The text of a prompt: its <c>{{variables}}</c> and the slash command.</summary>
public static partial class PromptText
{
    [GeneratedRegex(@"\{\{\s*([^{}\r\n]{1,40}?)\s*\}\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"^/[a-z0-9][a-z0-9_-]{0,31}$")]
    private static partial Regex CommandShape();

    public static bool IsCommand(string command) => CommandShape().IsMatch(command);

    /// <summary>The variables, in order of first appearance.</summary>
    public static IReadOnlyList<string> Variables(string system, string user) =>
        [.. Placeholder().Matches(system + "\n" + user).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal)];

    public static string Fill(string text, IReadOnlyDictionary<string, string> values) =>
        Placeholder().Replace(text, m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
}

/// <summary>The prompt library, its versions and test cases (design 4.6). Visible: the workspace's prompts and your own private ones; changed: your own, or anyone's by an owner or administrator.</summary>
public sealed class PromptsService(PromptsDbContext db, IActorContext actors, IUserDirectory users, IModelCatalog models, IModelGateway gateway, IBudgetGuard budget, IUsageRecorder usage,
    IAuditLog audit, IEventPublisher events, TimeProvider time)
{
    public const int MaxCases = 20;

    private async Task<Prompt> FindAsync(Guid id, bool forWrite, bool tracking, CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        var query = tracking ? db.Prompts.AsQueryable() : db.Prompts.AsNoTracking();
        var prompt = await query.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (prompt is null || (prompt.Visibility == "private" && prompt.OwnerId != actor.Id))
        {
            throw ApiException.NotFound("The prompt");
        }
        if (forWrite && prompt.OwnerId != actor.Id && actor.RoleId is not (Roles.Owner or Roles.Admin))
        {
            throw ApiException.Forbidden("Only the owner of a prompt, or an administrator, can change it.");
        }
        return prompt;
    }

    public async Task<IReadOnlyList<PromptDto>> ListAsync(string? q, CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        var id = actor.Id;
        var query = db.Prompts.AsNoTracking().Where(p => p.Visibility != "private" || p.OwnerId == id);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim().ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(needle) || p.Command.Contains(needle));
        }
        var prompts = await query.OrderByDescending(p => p.UseCount).ThenBy(p => p.Name).ThenBy(p => p.Id).Take(200).ToListAsync(cancellationToken);
        var ids = prompts.Select(p => p.Id).ToList();
        var cases = await db.Cases.AsNoTracking().Where(c => ids.Contains(c.PromptId)).ToListAsync(cancellationToken);
        var names = await NamesAsync(cancellationToken);
        return [.. prompts.Select(p => ToDto(p, cases.Where(c => c.PromptId == p.Id).ToList(), names))];
    }

    public async Task<PromptDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var prompt = await FindAsync(id, forWrite: false, tracking: false, cancellationToken);
        return await DtoAsync(prompt, cancellationToken);
    }

    public async Task<PromptDto> CreateAsync(SavePromptRequest request, CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        var command = Validate(request);
        if (await db.Prompts.AnyAsync(p => p.Command == command, cancellationToken))
        {
            throw ApiException.Conflict(ErrorCodes.CommandTaken, $"The command {command} is used by another prompt.");
        }
        var prompt = new Prompt { Id = Guid.NewGuid(), OwnerId = actor.Id ?? Guid.Empty, Name = request.Name.Trim(), Command = command, Visibility = request.Visibility, SystemText = request.SystemText, UserText = request.UserText,
            UpdatedAt = time.GetUtcNow() };
        db.Prompts.Add(prompt);
        await SaveAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord(PromptsAudit.Created, "prompt", prompt.Id.ToString(), prompt.Name), cancellationToken);
        return await DtoAsync(prompt, cancellationToken);
    }

    public async Task<PromptDto> UpdateAsync(Guid id, SavePromptRequest request, CancellationToken cancellationToken)
    {
        var prompt = await FindAsync(id, forWrite: true, tracking: true, cancellationToken);
        var command = Validate(request);
        if (await db.Prompts.AnyAsync(p => p.Command == command && p.Id != id, cancellationToken))
        {
            throw ApiException.Conflict(ErrorCodes.CommandTaken, $"The command {command} is used by another prompt.");
        }
        db.ExpectVersion(prompt, request.Version);
        prompt.Name = request.Name.Trim();
        prompt.Command = command;
        prompt.Visibility = request.Visibility;
        prompt.SystemText = request.SystemText;
        prompt.UserText = request.UserText;
        prompt.UpdatedAt = time.GetUtcNow();
        await SaveAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord(PromptsAudit.Updated, "prompt", prompt.Id.ToString(), prompt.Name), cancellationToken);
        return await DtoAsync(prompt, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var prompt = await FindAsync(id, forWrite: true, tracking: true, cancellationToken);
        db.Prompts.Remove(prompt);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord(PromptsAudit.Deleted, "prompt", id.ToString(), prompt.Name), cancellationToken);
    }

    public async Task<IReadOnlyList<PromptVersionDto>> VersionsAsync(Guid id, CancellationToken cancellationToken)
    {
        var prompt = await FindAsync(id, forWrite: false, tracking: false, cancellationToken);
        var versions = await db.Versions.AsNoTracking().Where(v => v.PromptId == id).OrderByDescending(v => v.Number).ThenBy(v => v.Id).ToListAsync(cancellationToken);
        var names = await NamesAsync(cancellationToken);
        return [.. versions.Select(v => new PromptVersionDto(v.Id, v.Number, v.Note, v.SystemText, v.UserText, v.Number == prompt.CurrentVersion, names.GetValueOrDefault(v.CreatedBy, ""), v.CreatedAt))];
    }

    /// <summary>Publishing copies the draft into a new immutable version; the slash command and the chat use the latest published one.</summary>
    public async Task<PromptDto> PublishAsync(Guid id, PublishPromptRequest request, CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        var prompt = await FindAsync(id, forWrite: true, tracking: true, cancellationToken);
        new Validator().Text("note", request.Note, 200, required: false).ThrowIfInvalid();
        if (string.IsNullOrWhiteSpace(prompt.UserText))
        {
            throw ApiException.Invalid("userText", "Write the prompt before publishing it.");
        }
        prompt.CurrentVersion++;
        prompt.UpdatedAt = time.GetUtcNow();
        db.Versions.Add(new PromptVersion { Id = Guid.NewGuid(), PromptId = id, Number = prompt.CurrentVersion, Note = request.Note?.Trim() ?? "", SystemText = prompt.SystemText, UserText = prompt.UserText,
            CreatedBy = actor.Id ?? Guid.Empty, CreatedAt = prompt.UpdatedAt });
        await SaveAsync(cancellationToken);
        var dto = await DtoAsync(prompt, cancellationToken);
        await audit.RecordAsync(new AuditRecord(PromptsEvents.Published, "prompt", id.ToString(), prompt.Name), cancellationToken);
        await events.PublishAsync(PromptsEvents.Published, dto with { SystemText = "", UserText = "" }, ContractsJsonContext.Default.PromptDto, cancellationToken);
        return dto;
    }

    public async Task<IReadOnlyList<PromptCaseDto>> CasesAsync(Guid id, CancellationToken cancellationToken)
    {
        await FindAsync(id, forWrite: false, tracking: false, cancellationToken);
        return [.. (await db.Cases.AsNoTracking().Where(c => c.PromptId == id).OrderBy(c => c.Sort).ThenBy(c => c.Id).ToListAsync(cancellationToken)).Select(ToDto)];
    }

    public async Task<PromptCaseDto> AddCaseAsync(Guid id, SavePromptCaseRequest request, CancellationToken cancellationToken)
    {
        await FindAsync(id, forWrite: true, tracking: false, cancellationToken);
        ValidateCase(request);
        var count = await db.Cases.CountAsync(c => c.PromptId == id, cancellationToken);
        if (count >= MaxCases)
        {
            throw ApiException.Conflict(ErrorCodes.InvalidState, $"A prompt has at most {MaxCases} test cases.");
        }
        var item = new PromptCase { Id = Guid.NewGuid(), PromptId = id, Name = request.Name.Trim(), Input = request.Input, Expect = request.Expect.Trim(), Sort = count };
        db.Cases.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(item);
    }

    public async Task<PromptCaseDto> UpdateCaseAsync(Guid id, Guid caseId, SavePromptCaseRequest request, CancellationToken cancellationToken)
    {
        await FindAsync(id, forWrite: true, tracking: false, cancellationToken);
        ValidateCase(request);
        var item = await db.Cases.SingleOrDefaultAsync(c => c.Id == caseId && c.PromptId == id, cancellationToken) ?? throw ApiException.NotFound("The test case");
        item.Name = request.Name.Trim();
        item.Input = request.Input;
        item.Expect = request.Expect.Trim();
        item.LastPassed = null;
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(item);
    }

    public async Task DeleteCaseAsync(Guid id, Guid caseId, CancellationToken cancellationToken)
    {
        await FindAsync(id, forWrite: true, tracking: false, cancellationToken);
        var removed = await db.Cases.Where(c => c.Id == caseId && c.PromptId == id).ExecuteDeleteAsync(cancellationToken);
        if (removed == 0)
        {
            throw ApiException.NotFound("The test case");
        }
    }

    /// <summary>Runs the draft once with the given variables (the editor's "try it").</summary>
    public async Task<RunPromptResponse> RunAsync(Guid id, RunPromptRequest request, CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        var prompt = await FindAsync(id, forWrite: false, tracking: false, cancellationToken);
        var values = request.Variables ?? new Dictionary<string, string>();
        var missing = PromptText.Variables(prompt.SystemText, prompt.UserText).Where(v => !values.ContainsKey(v) || string.IsNullOrWhiteSpace(values[v])).ToList();
        if (missing.Count > 0)
        {
            throw ApiException.Invalid("variables", $"Give a value for {string.Join(", ", missing)}.");
        }
        var result = await CompleteAsync(prompt, values, request.ModelCode, actor.Id, cancellationToken);
        return new RunPromptResponse(result.Text, result.TokensIn, result.TokensOut, result.Cost);
    }

    /// <summary>Runs every case of the draft; a case passes when the answer contains its expected text. The outcome is kept on the cases.</summary>
    public async Task<CasesRunDto> RunCasesAsync(Guid id, CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        var prompt = await FindAsync(id, forWrite: false, tracking: false, cancellationToken);
        var cases = await db.Cases.Where(c => c.PromptId == id).OrderBy(c => c.Sort).ThenBy(c => c.Id).ToListAsync(cancellationToken);
        foreach (var item in cases)
        {
            var values = ParseInput(item.Input);
            try
            {
                var result = await CompleteAsync(prompt, values, null, actor.Id, cancellationToken);
                item.LastPassed = item.Expect.Length == 0 || result.Text.Contains(item.Expect, StringComparison.OrdinalIgnoreCase);
            }
            catch (ApiException exception) when (exception.Code == ErrorCodes.ProviderFailed)
            {
                item.LastPassed = false;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        return new CasesRunDto(cases.Count(c => c.LastPassed == true), cases.Count, [.. cases.Select(ToDto)]);
    }

    private async Task<Completion> CompleteAsync(Prompt prompt, IReadOnlyDictionary<string, string> values, string? modelCode, Guid? userId, CancellationToken cancellationToken)
    {
        await budget.EnsureAsync(userId, cancellationToken);
        List<ModelMessage> messages = [];
        if (!string.IsNullOrWhiteSpace(prompt.SystemText))
        {
            messages.Add(new ModelMessage("system", PromptText.Fill(prompt.SystemText, values)));
        }
        messages.Add(new ModelMessage(MessageRoles.User, PromptText.Fill(prompt.UserText, values)));
        var model = await models.ResolveAsync(modelCode, cancellationToken) ?? throw new ApiException(StatusCodes.Status409Conflict, ErrorCodes.NoModel, "No model is enabled.");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var result = await gateway.CompleteAsync(models, model.Code, messages, cancellationToken);
        await usage.RecordAsync(new UsageEntry(userId, model.Id, model.ProviderId, model.DisplayName, "prompt", prompt.Id, result.TokensIn, result.TokensOut, 0, 0, (int)clock.ElapsedMilliseconds, result.Cost,
            $"{{\"in\":{model.PriceIn},\"out\":{model.PriceOut},\"cache\":{model.PriceCache}}}"), cancellationToken);
        return result;
    }

    private static IReadOnlyDictionary<string, string> ParseInput(string input)
    {
        try
        {
            return JsonSerializer.Deserialize(input, PromptsJson.Default.DictionaryStringString) ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (db.ChangeTracker.Entries<Prompt>().Any(e => e.State == EntityState.Added))
        {
            // Two people took the same command at once: the unique index decides.
            throw ApiException.Conflict(ErrorCodes.CommandTaken, "That command is used by another prompt.");
        }
    }

    private static string Validate(SavePromptRequest request)
    {
        var validator = new Validator().Text("name", request.Name, 120).Text("command", request.Command, 33);
        var command = request.Command?.Trim().ToLowerInvariant() ?? "";
        if (command.Length > 0 && !PromptText.IsCommand(command))
        {
            validator.Add("command", "Start with a slash, then up to 32 letters, digits, - or _ (for example /review).");
        }
        if (request.Visibility is not ("private" or "workspace"))
        {
            validator.Add("visibility", "Use private or workspace.");
        }
        if ((request.SystemText?.Length ?? 0) > 8_000)
        {
            validator.Add("systemText", "At most 8000 characters.");
        }
        if ((request.UserText?.Length ?? 0) > 8_000)
        {
            validator.Add("userText", "At most 8000 characters.");
        }
        validator.ThrowIfInvalid();
        return command;
    }

    private static void ValidateCase(SavePromptCaseRequest request)
    {
        var validator = new Validator().Text("name", request.Name, 120).Text("expect", request.Expect, 500, required: false);
        if (request.Input is null || request.Input.Length > 4_000)
        {
            validator.Add("input", "Give the variables as a JSON object of at most 4000 characters.");
        }
        else
        {
            try
            {
                JsonSerializer.Deserialize(request.Input, PromptsJson.Default.DictionaryStringString);
            }
            catch (JsonException)
            {
                validator.Add("input", "Give the variables as a JSON object of text values.");
            }
        }
        validator.ThrowIfInvalid();
    }

    private async Task<PromptDto> DtoAsync(Prompt prompt, CancellationToken cancellationToken) =>
        ToDto(prompt, await db.Cases.AsNoTracking().Where(c => c.PromptId == prompt.Id).ToListAsync(cancellationToken), await NamesAsync(cancellationToken));

    private async Task<Dictionary<Guid, string>> NamesAsync(CancellationToken cancellationToken) => (await users.ListAsync(cancellationToken)).ToDictionary(u => u.Id, u => u.Name);

    private static PromptDto ToDto(Prompt p, IReadOnlyList<PromptCase> cases, IReadOnlyDictionary<Guid, string> names) =>
        new(p.Id, p.Name, p.Command, p.Visibility, p.CurrentVersion, p.UseCount, names.GetValueOrDefault(p.OwnerId, ""), PromptText.Variables(p.SystemText, p.UserText), p.SystemText, p.UserText,
            cases.Count(c => c.LastPassed == true), cases.Count, p.UpdatedAt, p.Version);

    private static PromptCaseDto ToDto(PromptCase c) => new(c.Id, c.Name, c.Input, c.Expect, c.LastPassed);
}

[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class PromptsJson : System.Text.Json.Serialization.JsonSerializerContext;

/// <summary>Expands a leading <c>/command text</c> into the published prompt: the text after the command fills the first variable. Everything else is left as written.</summary>
internal sealed class PromptResolver(PromptsDbContext db, IActorContext actors) : IPromptResolver
{
    public async Task<string> ResolveAsync(string text, CancellationToken cancellationToken)
    {
        if (!text.StartsWith('/'))
        {
            return text;
        }
        var split = text.IndexOfAny([' ', '\n', '\t']);
        var command = (split < 0 ? text : text[..split]).ToLowerInvariant();
        var arguments = split < 0 ? "" : text[(split + 1)..].Trim();
        var actor = await actors.GetAsync(cancellationToken);
        var id = actor?.Id;
        var prompt = await db.Prompts.SingleOrDefaultAsync(p => p.Command == command && p.CurrentVersion > 0 && (p.Visibility != "private" || p.OwnerId == id), cancellationToken);
        if (prompt is null)
        {
            return text;
        }
        var version = await db.Versions.AsNoTracking().SingleAsync(v => v.PromptId == prompt.Id && v.Number == prompt.CurrentVersion, cancellationToken);
        var variables = PromptText.Variables(version.SystemText, version.UserText);
        var values = variables.Count > 0 && arguments.Length > 0 ? new Dictionary<string, string> { [variables[0]] = arguments } : new Dictionary<string, string>();
        var user = PromptText.Fill(version.UserText, values);
        await db.Prompts.Where(p => p.Id == prompt.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.UseCount, p => p.UseCount + 1), cancellationToken);
        var system = PromptText.Fill(version.SystemText, values);
        return system.Length == 0 ? user : system + "\n\n" + user;
    }
}
