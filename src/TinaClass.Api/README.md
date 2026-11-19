# TinaClass API

ASP.NET Core minimal API for the TinaClass desktop app, the AI workbench: sign-in with refresh tokens and API keys, model providers and
models, streaming chat with citations and tool calls, and (each one optional) knowledge bases, agents with approvals, workflows,
prompt templates, usage and budgets, and the team with an audit log. Read `docs/ARCHITECTURE.md` first for how the features are organized.

## Run it locally

```bash
dotnet run --project src/TinaClass.Api          # http://localhost:5080, SQLite file tinaclass.dev.db next to the project
```

In Development an empty database is filled with the sample workspace of the design (`appsettings.Development.json`): five people, their
conversations, providers and models, and every optional feature that is included. The people come from `SampleCore.People`:

| Account | Role | Password |
|---|---|---|
| `shen.zhiyuan@qiyun.example` | Owner: every permission, including provider keys | `local-dev-only-change-me` |
| `lu.jiahe@qiyun.example` | Admin: every permission except provider keys | same |
| `he.yanqiu@qiyun.example`, `zhou.yining@qiyun.example`, `tang.shuyao@qiyun.example` | Member: use and edit, no administration | same |

That password is public and exists for local development only: change `Seed:Password` (or turn `Seed:SampleData` off) and never
deploy with it. Outside Development the API refuses to start with `Seed:SampleData` on, and it ignores `appsettings.Development.json`
(the container image deletes it).

Without `Jwt:SigningKey` Development makes up a key at start (a warning says so): tokens stop working when the API restarts. Any other
environment refuses to start without one.

## Configuration

Settings come from `appsettings.json`, then the environment (`Section__Key`, e.g. `Jwt__SigningKey`), then user secrets in
Development. **Secrets (the signing key, connection strings with a password, the first administrator's password, the SMTP password)
belong in the environment, a secret store or `deploy/.env`: never in a file that is committed.**

| Setting | Default | |
|---|---|---|
| `App:Name` | `TinaClass` | Product name in emails (template option `productTitle`) |
| `App:Currency` | `CNY` | `CNY` or `USD`: the currency of the price tables and the budget (template option `currency`) |
| `Database:Provider` | see `appsettings.json` | `Sqlite`, `Postgres` or `SqlServer`: only the providers included in this edition work |
| `Database:MigrateOnStart` | `true` | Apply the migrations of every feature when the API starts; turn off to run them from a pipeline |
| `ConnectionStrings:Sqlite` / `:Postgres` / `:SqlServer` | `Data Source=tinaclass.db` / – / – | The connection string of the provider in use (`ConnectionStrings__Postgres`, ...) |
| `Jwt:SigningKey` | – | **Required outside Development**, 32+ random characters (`openssl rand -base64 48`) |
| `Jwt:Issuer` / `Jwt:Audience` | `tinaclass-api` / `tinaclass-app` | Checked on every token |
| `Jwt:AccessTokenMinutes` | 15 | Refresh tokens rotate; a session lasts 12 hours, or 30 days with "keep me signed in" |
| `Bootstrap:AdminEmail` / `AdminPassword` / `AdminName` | – | The first administrator, created at start when there are no accounts (password 10 to 128 characters). Remove them afterwards |
| `Auth:SignInRequestsPerMinute` | 20 | Per address, on `/auth/*` (429 beyond it) |
| `Jobs:SessionCleanupMinutes` | 60 | How often old sessions are deleted; 0 turns the job off |
| `Jobs:ChatPurgeMinutes` / `Chat:PurgeAfterDays` | 360 / 30 | How often, and after how many days, deleted conversations are removed for good; 0 minutes turns the job off |
| `Models:DefaultProvider` / `Models:DefaultModelCode` | `echo` / `echo-1` | Template options `defaultProvider` and `defaultModelCode`: any kind but `echo` makes the API create a provider "Default provider" of that kind with a model of that code, as the default, at its first start. It has no key: set it on the Models page or in `Models:Keys:Default provider`; until then the chat says so, and Echo is one click away |
| `Models:Echo:DelayMs` / `ThinkMs` | 14 / 350 | The pace of the built-in Echo model (a pause between pieces of text, and its "thinking" time); 0 for tests |
| `Models:Keys:<provider name>` | – | A provider key given as configuration (a user secret, or an environment variable when the name has no spaces); a key saved from the app is stored encrypted and wins |
| `Knowledge:IndexerPollMs` | 600 | How often the background indexer looks for uploaded documents; 0 turns it off |
| `Agents:StepDelayMs` | 700 | The pause between the steps of an agent run, so that a person can watch it; 0 for tests |
| `Workflows:NodeDelayMs` | 600 | The pause between the nodes of a workflow run; 0 for tests |
| `Team:InviteDays` / `Team:InviteUrl` | 7 / `tinaclass://accept-invite?token=` | How long an invitation lasts, and the link in the invitation email (the token is appended; give the address your app opens) |
| `Email:Mode` | `Log` | `Log` or `Smtp` (`Email:SmtpHost`, `SmtpPort`, `SmtpSsl`, `SmtpUser`, `SmtpPassword`, `From`). `Log` writes the text of a message only in Development |
| `DataProtection:KeysPath` | – | Where the keys that encrypt secrets read back later (a TOTP secret, a webhook signing secret) live; a volume in containers |
| `ForwardedHeaders:Enabled` | `false` | Behind a reverse proxy only: take the client address from `X-Forwarded-For`. Leave it off when clients can reach the API directly, or they can choose their own address |
| `Seed:SampleData` / `Seed:Password` | `false` / – | Development only: fill an empty database with the sample workspace (`Seed:Password` is the password of its five people) |

## API

All paths are under `/api/v1` (`TinaClass.Contracts.Common.ApiRoutes`); requests and responses are the records in `TinaClass.Contracts`,
errors are problem details with a `code` (`ErrorCodes`). Send `Authorization: Bearer <access token>` (or an API key `ak_live_…`), and
optionally `X-Client-Device: <device name>` so that the session shows it.

| Area | Endpoints | Needs |
|---|---|---|
| Auth (anonymous, rate limited) | `POST /auth/login`, `/auth/refresh`, `/auth/logout` | |
| Account | `GET /me` | any signed-in user or key |
| API keys | `GET/POST /me/api-keys`, `DELETE /me/api-keys/{id}` | `api_keys.manage`, signed in as a person |
| Models | `GET /models` (the enabled ones; `?all=true` for managers), `PATCH /models/{id}` | `models.use` / `models.manage` |
| Providers | `GET/POST /providers`, `PATCH/DELETE /providers/{id}`, `POST /providers/{id}/test`, `/sync` | `models.manage` |
| Provider keys | `PUT/DELETE /providers/{id}/key` (the key is never returned, only its last four characters) | `keys.manage` (owner) |
| Chat | `GET/POST /conversations`, `GET/PATCH/DELETE /conversations/{id}`, `GET /conversations/{id}/messages`, `POST /conversations/{id}/messages` (server-sent events) | `chat.use` |
| Chat, per message | `POST /messages/{id}/regenerate` (SSE), `/stop`, `/feedback`, `GET /messages/{id}/stream` (reconnect, `Last-Event-ID`) | `chat.use` |
| Chat, organizing | `GET/POST /folders`, `PATCH/DELETE /folders/{id}`, `GET/POST /spaces`, `GET /search`, `GET /citations/{id}` | `chat.use` |
| Chat, sharing | `POST /conversations/{id}/share`, `DELETE /shares/{id}` (`chat.share`); `GET /shared/{token}` (anonymous, rate limited, read only) | |
| Knowledge | `GET /knowledge`, `POST /knowledge/{id}/search`, `GET /knowledge/{id}/documents` | `knowledge.read` |
| Knowledge, write | `POST /knowledge/{id}/documents`, `DELETE /documents/{id}`, `POST /documents/{id}/reindex` | `knowledge.write` |
| Knowledge, manage | `POST /knowledge`, `PATCH/DELETE /knowledge/{id}` | `knowledge.manage` |
| Agents | `GET /agents`, `GET /agents/{id}`, `POST /agents/{id}/runs`, `GET /runs`, `GET /runs/{id}`, `POST /runs/{id}/cancel`, `GET /runs/{id}/events` (SSE), `GET /tools` | `agents.use` |
| Agents, write | `POST /agents`, `PATCH/DELETE /agents/{id}`, `POST /agents/{id}/publish` | `agents.write` |
| Approvals | `GET /approvals?state=`, `POST /approvals/{id}/approve`, `/reject` | `agents.approve` |
| Tools | `GET/POST /tool-servers`, `DELETE /tool-servers/{id}`, `POST /tool-servers/{id}/refresh`, `PATCH /tools/{id}` | `tools.manage` |
| Workflows | `GET /flows`, `GET /flows/{id}`, `GET /flows/{id}/versions`, `GET/POST /flows/{id}/runs`, `GET /flow-runs/{id}` | `workflows.run` |
| Workflows, write | `POST /flows`, `PUT /flows/{id}/draft`, `POST /flows/{id}/publish`, `DELETE /flows/{id}` | `workflows.write` |
| Prompts | `GET /prompts`, `GET /prompts/{id}`, `/versions`, `/cases` | `prompts.read` |
| Prompts, write | `POST /prompts`, `PATCH/DELETE /prompts/{id}`, `/publish`, `/run`, `POST/PUT/DELETE /prompts/{id}/cases`, `POST /prompts/{id}/cases/run` | `prompts.write` |
| Usage | `GET /usage/summary`, `/usage/records`, `/usage/budget` (a person with only `usage.read` sees their own use) | `usage.read` |
| Budget | `PUT /usage/budget` (every change is audited) | `usage.manage` |
| Team | `GET /members`, `PATCH/DELETE /members/{id}`, `POST /invites`, `GET /audit` | `team.members.manage`, `team.audit.read` |
| Invitations | `POST /invites/accept` (anonymous, rate limited: the token in the link is the credential) | |
| Health | `GET /healthz` (the process), `GET /readyz` (the databases) | |

Roles are in `Common/Security/Roles.cs`. Sign-in answers every failure alike (`invalid_credentials`, 401): an unknown email, a wrong
password, a disabled account and a temporary lock after 5 wrong passwords look the same. A refresh token works once: using an old one
ends its session.

**Models and keys.** The `Echo` provider and model always exist and answer offline: a provider whose address ends in `.example`
is served by Echo too, which is how the sample workspace works without a network. Provider keys are runtime configuration (saved from
the app by the owner, encrypted with ASP.NET Data Protection, or set as `Models:Keys:<provider name>`); no manifest option, setting file or
log carries one. The `IModelProvider` interface (`Features/Models/Providers.cs`) is the seam for a real provider: one adapter per
protocol (OpenAI-compatible, Messages, local runtime).

**Seams.** Features call each other only through the interfaces in `Common/Seams/Seams.cs`; each has a harmless default that the
feature providing it replaces: the knowledge retriever, the tool catalog, the prompt resolver, the usage recorder and budget guard, the
approval sink and observers, the user directory, the audit log (the Team feature stores it) and the event publisher.

**Tools are simulated.** An agent run calls `IToolExecutor`; the default answers with a sample result and calls nothing outside. Replace it
to call a real MCP server: the approval rules around it stay (a tool that reaches outside, or that writes when the agent says "ask", never
runs without a person's approval, and a rejected one is skipped).

**Knowledge search** keeps its vectors in the process (a hashed bag-of-words embedding, nothing to install); the stored chunks are
what a real vector store would index. Replace `IKnowledgeRetriever` and the indexing step when you use one.

**What is not here.** Two-step verification (TOTP), password reset by email, webhooks and single sign-on are not included; they are in
the Atlas Admin template (`src/Atlas.Api/Features/Auth`), which shares this architecture: copy a feature folder, its contracts, its
migrations and the registration lines. `Common/Email` and the `IAuditLog` and `IEventPublisher` seams are already here for them.

## Database and migrations

Each feature has its own DbContext, tables (a prefix per feature: `auth_`, `mdl_`, `chat_`, `kb_`, `agt_`, `wf_`, `pmt_`, `use_`, `team_`) and migrations history (`__<feature>_migrations`).
Every DbContext has a subclass per database with its own migrations, applied at start:

- SQLite: `Features/<Feature>/Data/Sqlite/Migrations` (always there: the tests run on SQLite)

After changing a feature's model, add a migration for every database at once, then check that none was forgotten:

```bash
tools/add-migration.sh <Feature> <MigrationName>      # e.g. tools/add-migration.sh Chat AddPinned
tools/add-migration.sh --check                        # fails when a model changed without a migration
```

The script runs `dotnet ef migrations add` for each provider folder and rolls everything back if one fails. The `dotnet-ef` tool is
pinned in `.config/dotnet-tools.json` (`dotnet tool restore`). Rules for the shared model (maximum lengths, versions, no provider
types) are in `docs/ARCHITECTURE.md`.

## Tests

```bash
dotnet run --project tests/TinaClass.Api.Tests      # xUnit v3; `dotnet test` works too where the SDK allows it
```

Each test class starts the API in memory on its own in-memory SQLite database; time can be moved, emails are captured and background
jobs are off. Tests of a database other than SQLite are not included: run the API against a real PostgreSQL or SQL Server (the Compose
files in `deploy/`) before you ship.

## Deploy

`deploy/Dockerfile` builds the API image (non-root user, port 8080, data and keys in `/data`). Copy `deploy/.env.example` to
`deploy/.env` and fill it in first (the signing key is required), then from the repository root:

```bash
docker compose -f deploy/compose.sqlite.yaml up --build -d          # the API with SQLite in a volume
```

The API listens on `127.0.0.1:5080` only; the database is not published. Put a TLS-terminating reverse proxy in front of the API for
other machines, and turn `ForwardedHeaders:Enabled` on only then.
