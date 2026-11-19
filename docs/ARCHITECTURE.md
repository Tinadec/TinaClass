# TinaClass architecture

TinaClass is an AI assistant workbench: an Avalonia desktop app built with TKCode.Arc. This file explains how it is put together, so that
you can extend it.
It comes with an ASP.NET Core API in the same solution; the app talks to it through the shared contracts.

## Solution

```
TinaClass.slnx
src/
  TinaClass.Contracts/   DTOs shared by the API and the app; ContractsJsonContext (source-generated JSON); ApiRoutes; ErrorCodes; Permissions; Money
  TinaClass.Api/         ASP.NET Core minimal API, EF Core (SQLite, PostgreSQL or SQL Server), JWT sign-in, API keys, the model gateway, background jobs
  TinaClass.App/         Avalonia 12 + TKCode.Arc desktop app (Windows, macOS, Linux), MVVM, DI
    app.json           the product name, version, logo mark, currency and language, and where its data comes from (read once at start)
    arc.theme.json     appearance, accent or palette, corners, density and design token overrides (read once at start by Common/Theme/ThemeFile)
    Common/            shell and layouts, navigation, settings, theme, localization, shared parts, capture tooling, styles
    Common/Api/        the API client, the session, the token store and the sign-in screen
    Features/          one folder per feature
tests/
  TinaClass.App.Tests/   xUnit v3 + Avalonia.Headless: pages render and behave against the sample services; the preview pictures
  TinaClass.Api.Tests/   xUnit v3 + WebApplicationFactory against SQLite in memory
deploy/                Dockerfile for the API, Docker Compose files, .env.example
tools/add-migration.sh one migration per database for a feature, and the drift check
.config/               the dotnet-ef tool, pinned
```

Target framework `net10.0` (LTS). Package versions are central in `Directory.Packages.props`; TKCode.Arc 1.1.0 requires
Avalonia [12.1.5, 13.0.0), so every Avalonia package stays on one version, 12.1.5. Warnings are errors.

`Directory.Packages.props` also pins, without referencing them, the Arc packages the app does not use (all 1.1.0):
`TKCode.Arc.AdvancedCharts` (`DataGrid`, other charts; call `UseArcAdvancedCharts()` in `AppSetup`), `.Dock`, `.Terminal`, `.Mobile`, `.Pdf`.
A template that needs one adds `<PackageReference Include="TKCode.Arc.Dock" />` to `TinaClass.App.csproj` (no version there).
The app uses `TKCode.Arc`, `.Blocks`, `.Special` (the AI controls), `.Icons.Lucide`, `.Themes.Corners`<!--#if (featureWorkflows) --> and `.Workflow` (the canvas)<!--#endif -->.

## Features

Every feature is a folder with the same name in each project, registered in exactly one place per project. A feature that is switched off
in Arc Gallery is removed with its folders, its tests and its text.

| Feature | Folder | Option |
|---|---|---|
| Chat: conversations, streaming answers, citations, tools, the inspector | `Chat/` | always |
| Home: greeting, composer, starter prompts, recent conversations | `Home/` | always |
| Models: providers and models, keys, the default | `Models/` | always |
| Knowledge: bases, documents, indexing, retrieval test | `Knowledge/` | `featureKnowledge` |
| Agents: agents, tools, tool servers, runs, approvals | `Agents/` | `featureAgents` |
| Workflows: a node canvas, runs, a run trace | `Flow/` in the app, `Workflows/` in the contracts and the API | `featureWorkflows` (needs `featureAgents`) |
| Prompts: variables, versions, test cases | `Prompts/` | `featurePrompts` |
| Usage: spend, budget, warnings | `Usage/` | `featureUsage` |
| Team: members, roles, invitations, sharing | `Team/` | `featureTeam` |
| Auth: sign-in, refresh tokens, API keys, roles | `Auth/` in the contracts and the API, `Common/Api/` in the app | `backend` |

In the app a feature folder holds its page, view model, service interface, sample service, text catalog and `Api/` (the service over the
API). The contracts and the API have the same folder (`TinaClass.Contracts/<Feature>/`, `TinaClass.Api/Features/<Feature>/` with `Data/`
and its migrations), and each test project too (`tests/TinaClass.App.Tests/Features/<Feature>/`, `tests/TinaClass.Api.Tests/<Feature>/`).

Cross-cutting pieces live in `Common/`: the shell and the layouts (`Shell/`), the page list and navigation (`Navigation/`), the settings
file (`Settings/`), the theme (`Theme/`), the text catalog and languages (`Localization/`), the app's own parts and motion (`Ui/`), the
capture tooling (`Capture/`) and the shared styles (`Styles/Panels.axaml`).

Features are kept apart so that each can be added or removed as a unit:

- A feature's code lives **only** in its folders, plus the few lines that register it, each inside a template block: the service
  registration in `Common/Services/AppServices.cs`, the page entry in `Common/Navigation/AppPages.cs` and the text catalog in `AppSetup.cs`.
  The API's part is one `Add…Feature` / `Map…Feature` pair in `Program.cs`, the feature's permissions in
  `Contracts/Common/Permissions.cs` and `Api/Common/Security/Roles.cs`, its routes in `ApiRoutes.cs` and its DTOs in `ContractsJsonContext.cs`.
- Features never call each other's internals. A call from one feature to another goes through an interface in `Common/`
  (`IApprovalReview`, `ISpendInfo`, `IBadgeSource`, `INavigator`) or in the API's `Common/Seams/` (`IModelGateway`, `IKnowledgeRetriever`,
  `IToolCatalog`, `IPromptResolver`, `IUsageRecorder`, `IBudgetGuard`, `IApprovalSink`) with a default that does nothing; the feature
  that provides the real one replaces it. A feature that is left out therefore leaves no dangling reference.

## Layouts

The option `layout` picks the shell. Each layout is its own pair of files and its own tests; the manifest removes the other two.
`ShellBase` holds what they share (the page host, the command palette, the preferences menu, the sign-in cover); a layout arranges the
sidebar, top bar and page.

| Layout | Files | Look |
|---|---|---|
| `studio` (default, design scheme B) | `Common/Shell/StudioShell.*` | conversations in the `Sidebar` on the left, the page in the middle, the inspector on the right (it folds at narrow widths) |
| `focus` (scheme A) | `Common/Shell/FocusShell.*` | one centered column, a pill to change page, no side panels |
| `spaces` (scheme C) | `Common/Shell/SpacesShell.*` | an icon rail and a panel per space; a space is a folder of conversations |

`ShellLayout.Create()` returns the shell whose template block survives.

## App

- **Use TKCode.Arc components first.** A view is built from TKCode.Arc, TKCode.Arc.Blocks, TKCode.Arc.Special, TKCode.Arc.Icons.Lucide and
  TKCode.Arc.Themes.Corners controls wherever one fits. Colors, fonts, radii, sizes and spacing are Arc design tokens
  (`{arc:ArcDesignTokenResource ...}`), never literals; only Thickness values (margins, paddings) are written out, because
  Arc has no Thickness tokens. UI is hand-rolled only where Arc 1.1.0 has no equivalent, with a comment naming the Arc
  component that was checked and why it does not fit. The list is under "UI components" below.
- MVVM with CommunityToolkit.Mvvm (source-generated), Microsoft.Extensions.DependencyInjection with every registration
  written out (trimming-safe), compiled bindings. View models are singletons, so a page keeps its state when you leave and come back.
- Every page talks to a service interface in its feature folder (`IChatService`, `IKnowledgeService`, ...); the sample
  implementations are in memory and deterministic (`Common/Sample/SampleClock.cs` is the clock of the sample, pinned in tests).
  Replace them with your backend; the pages do not change.
  `app.json` `"dataSource": "api"` swaps in the API implementations (`Features/<Feature>/Api/`, over `Common/Api/ApiClient`); `"sample"`
  keeps the sample data, offline. With the API the app shows a sign-in screen until someone has signed in (a call that needs a user waits
  for it), keeps the access token in memory and, only for "keep me signed in", the refresh token in the system's secret store (Keychain,
  Credential Manager, Secret Service). A refresh that is refused signs out. `Common/Api/ApiSettings.cs` reads the two keys; `AppInfo`
  ignores them.
- **Chat**: `ChatHub` owns the conversation list (rename, pin, move to a folder, delete) and the open conversation; `ChatViewModel` shows
  one conversation through the `AiChat` block. An answer is a stream of events (`message.start`, `reasoning.delta`, `plan`,
  `tool.start`, `tool.result`, `tool.approval`, `citation`, `text.delta`, `usage`, `message.done`; the names are in `StreamEvents` and are fixed:
  the app, the API and the adapters all speak them). `SampleChatService` is the Echo of the app: it streams a scripted answer piece by piece so
  that the thinking step, the plan, the tool calls, the sources and the text arrive the way a real model's would. Messages form a tree
  (`parent_id`), which regenerate, edit and parallel answers rely on.
- **Models**: the API's `IModelProvider` is the seam to a model service (`Protocol` says which; `EchoProvider` is the default, needs no key and no
  network; `OpenAiCompatibleProvider` serves `openai-compatible`, `messages` and `local`). `IModelGateway` picks the adapter for a model and
  fills in the provider's key; chat, prompts, agents and workflows all call the gateway. A key is saved encrypted (Data Protection), never
  returned, and never part of an option; `Models:DefaultProvider` and `Models:DefaultModelCode` (template options) only say which kind of
  provider to create at the first start.
- Money: amounts are integer minor units; `Money.Configure("CNY" | "USD")` (from `app.json`<!--#if (backend) --> and the API's `App:Currency`<!--#endif -->) sets the symbol.
- Theme: `arc.theme.json` loaded once at start through `ThemeFile`; name, logo, currency and language in `app.json`
  (`Common/AppInfo.cs`). What a person chooses in the app (appearance, language, motion, the folded sidebar, the window's
  place) is kept in `settings.json` under the user's application data folder (`Common/Settings/UserSettings.cs`).
- Motion: every visible change animates through Arc's motion system and follows its Full / Reduced / Off levels
  (`Common/Theme/MotionPreference.cs`: the preferences menu's Motion choice, or the operating system's reduce-motion
  setting). Page changes use `ArcContentSwap`, the sidebar springs, menus open on Arc's menu surface, the theme switch
  crossfades (`Common/Theme/ThemeTransition.cs`), alerts collapse, figures roll, a figure that changes fades (`Motion.FadeOn`) and the
  composer glows while the model thinks. At Reduced the changes fade without travel; at Off they jump.
- Text in `Common/Localization/*.xlf` and, per feature, `Features/<Feature>/Localization/*.xlf` (en-US, zh-CN, zh-TW); the language follows
  the system unless `app.json` sets one. To add a string, add a member to the enum next to the .xlf files and a unit with the same id to
  each of them; the build reports a missing one.
- Capture tooling (`Common/Capture/CaptureRun.cs`): `TINACLASS_CAPTURE=<folder>` walks every page of the real window in light
  and dark; `TINACLASS_CAPTURE_PART=motion` takes frames in the middle of the animations; `menus` captures the preferences menu.

## API

- Minimal API endpoint groups per feature under `/api/v1` (`ApiRoutes`), JSON through `ContractsJsonContext`, errors as problem
  details with a `code` (`ErrorCodes`). Every endpoint declares who may call it: anonymous (sign-in only), any signed-in user, or a
  permission from `Permissions`; roles map to permission sets (`Common/Security/Roles.cs`: Owner, Admin, Member, Viewer).
- Layers: `Contracts` (DTOs, routes, error codes, permissions), `Api/Common` (http, persistence, security, auditing, events, email, seams) and
  `Api/Features/<Feature>/`. A feature registers itself with `Add<Feature>Feature` / `Map<Feature>Feature` in `Program.cs`.
- Streaming: a chat answer is Server-Sent Events (`id:` is the event's number, for `Last-Event-ID`); an agent run has its own event stream.
- Sign-in: email and password (PBKDF2), a JWT access token (15 minutes) and a rotating refresh token stored as a SHA-256 hash, one
  session per device. A used refresh token ends its session. API keys (`ak_live_…`, stored hashed) act for the user who made them,
  within the scopes they were given. Five wrong passwords block sign-in for 15 minutes; every failure looks alike; `/auth/*` is rate
  limited per address.
- Secrets: the signing key, connection strings, provider keys and passwords come from the environment or a secret store, never from source.
  Outside Development the API refuses to start without `Jwt:SigningKey` (32 or more characters) or with `Seed:SampleData` on.
- Tools that can do harm wait for a person: a tool with risk `write` or `external` asks for approval by default, one call at a time; only an
  owner relaxes that in the settings (it is not an installation option).
- Database: one DbContext per feature with a table prefix (`auth_`, `mdl_`, `chat_`, ...). Its model (`Features/<Feature>/Data/<Feature>DbContext.cs`)
  is the same on every database; `Data/Sqlite`, `Data/Postgres` and `Data/SqlServer` derive from it and own the migrations, which are
  applied at start. To change a model, edit it and run `tools/add-migration.sh <Feature> <Name>` (one migration per database), then
  `tools/add-migration.sh --check`. Rules for the shared model, so that it stays valid on all three databases:
  - every string has a maximum length (an index key on SQL Server must fit its limit);
  - concurrency uses the app-level `Version` integer (`IVersioned`, raised by `FeatureDbContext`), not `rowversion` or `xmin`;
  - no provider-specific column types or raw SQL in the shared model; what one database needs goes in its own subclass;
  - times are stored in UTC (`TimeProvider.GetUtcNow()`); SQLite keeps them as ticks (its subclass says so);
  - compare text the way every database does: the API normalizes what it looks up (emails are lower-cased) instead of relying on the collation.
- Tests run the whole API in memory on SQLite. The migrations of PostgreSQL and SQL Server are generated and checked for drift, not run:
  start the API against a real server (the Compose files in `deploy/`) before you ship.

## UI components

Arc 1.1.0 controls used, and what is still composed by hand (each with a comment in the source). **Hand-rolled** rows say which Arc
component was checked and why it does not fit.

| Part of the UI | Built with | Notes |
|---|---|---|
| Window, title bar, caption buttons | `ArcWindow`, `TitleBar` | the page's actions sit in the title bar (`PageAction`) |
| Sidebar (rail and expanded, conversations in folders, the spend card) | `Sidebar`, `SidebarSection`, `SidebarItem` (Blocks) | width springs; labels fade with `ArcDoubleTransition`; the page glides with the sidebar's edge |
| Conversation context menu (rename, pin, move, delete) | `MenuSurface` + `MenuEntry`, `Dialog` | `Common/Shell/ConversationMenu.cs`. In the Studio sidebar the row is found by its label: Arc 1.1.0 keeps `SidebarRow` internal and `SidebarItem` is not a control, so there is no public hit test |
| Preferences menu, appearance menu, submenus | `MenuSurface` + `MenuEntry` (account variant), `DropdownMenu` | radio rows for appearance, language and motion |
| Command palette `Ctrl+K` | `CommandPaletteDialog` | commands and conversations; shortcuts are shown with `KeyCap` |
| Page change | `ArcContentSwap` (Lift) inside `Common/Navigation/PageHost.cs` | |
| Chat conversation | `AiChat` (Special) | the block draws the messages, the composer and the streaming reveal |
| Chat plan, generation stats, context window | `AgentPlan`, `GenerationStats`, `ContextWindow` (Special) in `Tabs` | in the inspector |
| Home composer | `AiComposer` (Special) | the gradient glow while the model thinks |
| Headline figures | `MetricCard` (value rolls in) | density via `MetricCard` tokens (`CardPadding`) set from `density` in `arc.theme.json` |
| Budget | `UsageMeter` | its caption and badge are English-only in 1.1.0, so two template-part styles in `UsagePage.axaml` hide them and the page says the same in a localized line |
| Spend per day | `BarChart` | |
| Tables (documents, members, models, invitations) | `Table` + `DataTableColumn` (typed delegates, no reflection), `Avatar`, `Badge`, `Progress` | `Table` tokens `RowHeight` and `HeaderHeight` follow `density`; `CompactBreakpoint` is a token in `arc.theme.json` |
| Knowledge page | `FileDropzone`, `SearchField`, `Table`, `Progress`, `Badge` | the `KnowledgeBase` block is not used: it owns its own document model and layout, and this page needs the app's states (ready, parsing, queued, failed) and a retrieval test next to the table |
| Agents page | `AgentPlan`, `Badge`, `Button`, `Alert` | `AgentTimeline` is not used: it is a playable multi-agent timeline with scrubbing and handoffs; a run here is a short list of steps |
| Approval dialog | `Dialog` + `HoldToConfirm` | `Features/Agents/ApprovalReview.cs`; the arguments are a `DescriptionList` |
| Provider keys | `ProviderKeys` (Blocks), `Switch`, `Table` | a key is typed once and never shown again |
| Prompts page | `PromptStudio` (Special), `SearchField`, `Badge` | |
| Workflow canvas | `WorkflowCanvas`, `WorkflowMinimap`, `WorkflowZoomControls` (Workflow) | node states color the nodes as a run goes |
| Errors | `Alert` (collapses itself; `Motion.HidesWhenClosed` drops it from layout afterwards), `Toast` | |
| Counts, status | `Badge` | |
| Sign-in screen | `Input`, `PasswordField`, `Checkbox`, `Button`, `Alert`; shown and hidden with `ArcContentSwap` (`Common/Api/SignInOverlay.cs`) | **hand-rolled layout**: Arc 1.1.0's `LoginSplit`, `LoginCentered` and `SignIn` run an email-code, passkey or provider flow that this API (email and password) does not have |
| Sign out | `MenuEntry` at the end of the preferences menu (added in code, only with the API) | |
| Panels and their header bars (`Border.panel`, `Border.bar`, `Border.panel-head`) | Arc tokens only | **hand-rolled**: `Card` is a media/quick-look card with fixed padding; `PageHeader` (Blocks) is a document header that owns the scroll area and condenses on scroll |
| Logo tile in the title bar and the sidebar header | `Border` + `LucideIcon`, Arc tokens only | **hand-rolled**: Arc has no logo control, and `Avatar` shows initials or a photo rather than the app's mark; the shape is the `logoMark` option (`spark`, `ring`, `grid`) |
| Compact spend card in the sidebar | `Border` + `Progress` + `TextBlock` | **hand-rolled**: `UsageMeter` is a full-width meter with a caption and a badge, too large for the sidebar's foot |
| `KeyCap` (`Common/Ui/KeyCap.cs`) | `Border` + `TextBlock` in Geist Mono | **hand-rolled**: Arc has `ShortcutKeyCap` only as a struct that formats text, and `Badge` is a proportional text pill |
| `DescriptionList` (`Common/Ui/DescriptionList.cs`): a read-only list of label and value rows | `ItemsControl` | **hand-rolled**: `PropertyGrid` edits the properties of an object and groups them; this only displays pairs (approval arguments, run details, a search hit's facts) |
| `RunTrace` (`Common/Ui/RunTrace.cs`): a horizontal strip of a run's steps, each as wide as it took | `Grid` of `StatusDot` bars and `TextBlock` | **hand-rolled**: `AgentTimeline` is a playable timeline with scrubbing and handoffs and `Timeline` is a vertical list of events |
| Plain row lists (`ListBox` styles in `Styles/Panels.axaml`): the bases, prompts and agents lists; the Spaces rail and its conversation rows (`SpacesShell`) | `ListBox` with its own template and `ListBoxItem` styles, Arc tokens only | **hand-rolled**: Arc has no selectable list of rows (`Sidebar` is navigation and its items are tree rows with no room for a time and a pin; `TreeView` adds an expand gesture history has no use for; `ListBox` has no template without a base theme). A row is muted on hover and raised and outlined when selected |
| `StatusDot` (`Common/Ui/StatusDot.cs`): an 8 px dot for a state (a provider that answers, a run that waits) | `Border` | **hand-rolled**: `Badge` is a text pill with no 8 px dot form. The running dot pulses at Full motion only |
| `ComposerGlow` (`Common/Ui/ComposerGlow.cs`): the looping gradient outline of the chat composer while an answer is made | `AiChat` / `AiComposer` (Arc), a conic gradient on the composer's `PART_Surface` | **hand-rolled**: `AiComposer` and `AiChat` only set `:generating` / `:busy` and draw no glow and Arc has no gradient token. Turns once in 3 s at Full, still at Reduced, none at Off |

## Not covered by tests or checks

The motion at each level (Full, Reduced, Off) is not asserted by the headless tests: look at it with `TINACLASS_CAPTURE_PART=motion` in a real
window (and `TINACLASS_CAPTURE_MOTION=Reduced` or `Off`). The preview pictures are rendered headless, in zh-CN.
PostgreSQL and SQL Server are built and their migrations checked for drift, not run.
