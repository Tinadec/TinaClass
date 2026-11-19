using TinaClass.Api.Common;
using TinaClass.Api.Common.Persistence;
using TinaClass.Api.Features.Auth;
using TinaClass.Api.Features.Chat;
using TinaClass.Api.Features.Models;
using TinaClass.Api.Features.Agents;
using TinaClass.Api.Features.Knowledge;
using TinaClass.Api.Features.Prompts;
using TinaClass.Api.Features.Team;
using TinaClass.Api.Features.Workflows;
using TinaClass.Api.Features.Usage;

// One Add…/Map… pair per feature, here and nowhere else. A feature that is left out takes its two lines with it.
var builder = WebApplication.CreateBuilder(args);
builder.AddCommon();
builder.AddAuthFeature();
builder.Services.AddModelsFeature(builder.Configuration);
builder.Services.AddChatFeature(builder.Configuration);
builder.Services.AddAgentsFeature(builder.Configuration);
builder.Services.AddKnowledgeFeature();
builder.Services.AddPromptsFeature();
builder.Services.AddTeamFeature(builder.Configuration);
builder.Services.AddWorkflowsFeature(builder.Configuration);
builder.Services.AddUsageFeature();

var app = builder.Build();
app.UseCommon();
app.MapAuthFeature();
app.MapModelsFeature();
app.MapChatFeature();
app.MapAgentsFeature();
app.MapKnowledgeFeature();
app.MapPromptsFeature();
app.MapTeamFeature();
app.MapWorkflowsFeature();
app.MapUsageFeature();

await app.Services.PrepareDatabaseAsync();
await app.RunAsync();

/// <summary>The entry point, public for the tests' WebApplicationFactory.</summary>
public partial class Program;
