# TinaClass

An AI assistant workbench for the desktop (Windows, macOS, Linux), built with [TKCode.Arc](https://github.com/AIDotNet/TKCode.Arc) on
Avalonia: streaming chat with citations and tool calls, knowledge bases, agents that ask before they do something risky, prompts with test
cases, usage and budgets, and a team. It builds and passes its tests as it is, on sample data, with a built-in Echo model that needs no key.
It comes with an ASP.NET Core API (sign-in, API keys, roles, a database per feature, the model gateway) that the app can sign in to.

## Requirements

- .NET 10 SDK or newer
- Docker, for the deployment files in `deploy/`

## Run it

```bash
dotnet run --project src/TinaClass.App
```

The app starts on its sample data, offline: a workspace of five people with conversations, documents, agents, prompts and a month of usage.
Press `Ctrl+K` (`Cmd+K` on macOS) for the command palette, `Ctrl+B` for the sidebar.

To use the API instead, start it (in Development it creates a database with the sample workspace) and point the app at it:

```bash
dotnet run --project src/TinaClass.Api      # http://localhost:5080
```

Then set `"dataSource": "api"` in `src/TinaClass.App/app.json` (the address is `apiBaseUrl` there) and start the app: it asks for a
sign-in first. The sample people and their local development password are listed in [the API's README](src/TinaClass.Api/README.md). That
password is public: it works in Development only, and the API refuses to start with sample data anywhere else.

## Test

```bash
dotnet build TinaClass.slnx
dotnet run --project tests/TinaClass.App.Tests
dotnet run --project tests/TinaClass.Api.Tests
```

The test projects use xUnit v3; `dotnet run` runs them on any SDK. The app's tests run on Avalonia's headless platform.
The API's tests start the API in memory on SQLite, whichever database you chose.

## What is inside

| Area | |
|---|---|
| Chat | Conversations in folders (rename, pin, move, delete from the sidebar's context menu), a streaming answer with a "thinking" step, a plan, tool calls, source cards and numbered citations, tables, generation stats, a context-window inspector, regenerate and edit |
| Home | A greeting, the input with a gradient glow, starter prompts |
| Models | Providers and models: price, context size, capabilities, whether a key is set (a key is never shown again); `IModelProvider` is the seam, Echo is the default |
| Knowledge | Documents with a state each (ready, parsing, queued, failed), a retrieval test with scores, the properties of a document |
| Agents | Agents with tools and a risk per tool, tool servers, runs with a trace of steps, and approvals: a risky call waits until a person holds the button to confirm |
| Workflows | A node canvas (`WorkflowCanvas`) of agents, tools and approvals; a run colors its nodes as it goes |
| Prompts | Prompts with variables, versions and test cases that run against the model |
| Usage | Spend per day, model and member, the monthly budget with warnings at 80 % and 100 % |
| Team | Members, roles, invitations, a shared conversation |
| API | `src/TinaClass.Api`: sign-in with refresh tokens, API keys, roles and permissions, one database per feature (SQLite, PostgreSQL or SQL Server), the model gateway; see [its README](src/TinaClass.Api/README.md) |
| Sign-in | With `"dataSource": "api"` the app shows a sign-in screen first and keeps the session (the refresh token goes to the system's keychain only if asked) |
| Everywhere | Light and dark, English, 简体中文, 繁體中文, motion that follows Full / Reduced / Off and the system setting |

Read [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for how it is organized.

## Provider keys

A provider's key is runtime configuration, never part of the source or of an option: save it on the Models page (only its last four
characters are shown again). Without a key the chat says so and Echo stays one click away.
With the API the key is stored encrypted by the API; it can also be given as `Models:Keys:<provider name>` (an environment variable or a
user secret).

## Theme and brand

`src/TinaClass.App/arc.theme.json` holds the appearance, the accent (`accent`, or a named `palette`), the corner scale (`corners`: a number,
1 is Arc's default), `density` (`spacious`, `comfortable` or `compact`: table rows and figure cards) and design token overrides;
`src/TinaClass.App/app.json` the product name, the logo mark (`spark`, `ring` or `grid`), the currency (`CNY` or `USD`) and the language
(`system` follows the operating system). Both are plain data files next to the executable, read once at start.
`app.json` also says where the app's data comes from: `dataSource` (`sample` or `api`) and the API's address, `apiBaseUrl`. The API reads
its own side of the same options from `appsettings.json` (`App:Name`, `App:Currency`, `Models:DefaultProvider`, `Models:DefaultModelCode`).

## Preview pictures

```bash
TINACLASS_SCREENSHOTS=$PWD/preview dotnet run --project tests/TinaClass.App.Tests    # headless: the scenes, light and dark
TINACLASS_CAPTURE=$PWD/capture dotnet run --project src/TinaClass.App                # the real window walks every page
TINACLASS_CAPTURE=$PWD/capture TINACLASS_CAPTURE_PART=motion dotnet run --project src/TinaClass.App   # frames in the middle of the animations
```
