# Northwind Support Desk

A support ticket system: raise, triage, filter, escalate and resolve tickets. Agents sign in.
**.NET 10 Web API** (clean architecture, EF Core) + **React 19 / TypeScript** + **SQL Server 2022**,
in one repository. The assignment is in [`ASSIGNMENT_CANDIDATE.md`](ASSIGNMENT_CANDIDATE.md); the
reasoning behind every decision, with a requirement-by-requirement checklist, is in
**[`APPROACH.md`](APPROACH.md)**.

## What was implemented

| Task | Summary |
| --- | --- |
| 1 | DEFECT-117 fixed at its cause, with regression tests. Every list filter applied in SQL before counting and paging. |
| 2 | Automatic triage on create (priority, SLA due date, agent), escalation with an immutable history, derived SLA status (incl. At risk) filterable in SQL, and the UI for all of it. |
| 3 | Agents sign in with email and password (hashed); JWT on every API call, enforced centrally; login page, protected routes, sign-out, expiry handling; escalation actor from the token. |
| 4 | Multi-stage Docker image (non-root, nothing baked in), compose with SQL Server, migrations and seeding on start, anonymous `/health`. |

## Run it

**Prerequisites:** Docker Desktop, .NET SDK 10, Node.js 22 (20.19+ works). Windows 11 with CMD,
PowerShell or Git Bash, macOS and Linux all work; shell differences are shown where they exist.

### Option A - backend in Docker (one command after setup)

Create `.env` (git-ignored) with a JWT signing key:

```bash
# Git Bash / WSL / macOS / Linux
cp .env.example .env
sed -i "s|^Jwt__SigningKey=.*|Jwt__SigningKey=$(openssl rand -base64 48)|" .env
```

```powershell
# Windows PowerShell 5.1 or 7
Copy-Item .env.example .env
$b = New-Object byte[] 48; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
(Get-Content .env) -replace '^Jwt__SigningKey=.*', "Jwt__SigningKey=$([Convert]::ToBase64String($b))" | Set-Content .env -Encoding ascii
```

```bat
:: Windows CMD: generate a key, then paste it after Jwt__SigningKey= in .env
copy .env.example .env
powershell -NoProfile -Command "$b=New-Object byte[] 48;[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b);[Convert]::ToBase64String($b)"
notepad .env
```

Then:

```bash
docker compose up --build -d     # SQL Server + API; first build downloads the .NET SDK image
docker compose ps                # sqlserver "healthy", api "Up"
docker compose logs api          # migrations applied, demo data seeded
npm install
npm run web                      # see below for pointing it at the container
```

| | |
| --- | --- |
| API | http://localhost:5080 · Swagger http://localhost:5080/swagger · health http://localhost:5080/health |
| Web app against the container | Bash: `VITE_API_PROXY_TARGET=http://localhost:5080 npm run web` · CMD: `set VITE_API_PROXY_TARGET=http://localhost:5080&& npm run web` (no space before `&&`) · PowerShell: `$env:VITE_API_PROXY_TARGET='http://localhost:5080'; npm run web` |
| Web app | http://localhost:5173 |
| Stop / stop and wipe the database | `docker compose down` / `docker compose down -v` |

In PowerShell use `curl.exe` (plain `curl` is `Invoke-WebRequest`) or `Invoke-RestMethod`.

### Option B - API on the host

```bash
npm install
npm run db:up                    # SQL Server only, in Docker
dotnet user-secrets set Jwt:SigningKey "$(openssl rand -base64 48)" --project apps/api/src/SupportDesk.Presentation
npm run api                      # https://localhost:7043, Swagger at /swagger
npm run web                      # http://localhost:5173 (proxies /api to the API)
```

PowerShell: generate `$b` as in Option A, then
`dotnet user-secrets set Jwt:SigningKey ([Convert]::ToBase64String($b)) --project apps/api/src/SupportDesk.Presentation`.
CMD: replace the user-secrets line with
`for /f %k in ('powershell -NoProfile -Command "$b=New-Object byte[] 48;[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b);[Convert]::ToBase64String($b)"') do dotnet user-secrets set Jwt:SigningKey %k --project apps\api\src\SupportDesk.Presentation`.
The key lives in your user profile, never in the repository.

### Development login

Any seeded agent with the password **`Local-Dev-Only-1!`** - a fictional local fixture
(`Seed:DevelopmentAgentPassword`), hashed per agent when seeded. For example
`sara.lindqvist@northwind-support.example` or `priya.nair@northwind-support.example`.
`yuki.tanaka@northwind-support.example` is inactive and is always refused, by design.

### Configuration

| Setting | Where | Notes |
| --- | --- | --- |
| `Jwt__SigningKey` | `.env` (Docker) or user-secrets (host) | **Required**, ≥ 32 bytes. Missing: the API stops at startup and says how to set it. |
| `ConnectionStrings__Default` | compose builds it (server `sqlserver`); `appsettings.Development.json` on the host | |
| `ASPNETCORE_ENVIRONMENT` | `.env`, default `Development` | Development = migrate + seed on start, Swagger |
| `MSSQL_SA_PASSWORD`, `Seed__DevelopmentAgentPassword` | `.env`, local defaults | |
| `Sla:*`, `Jwt:Issuer/Audience/AccessTokenMinutes` | `appsettings.json` | overridable with `Sla__...` / `Jwt__...` variables |

**Migrations and seeding.** In Development the API applies pending EF migrations and seeds on
every start, idempotently: only missing migrations run, demo data goes only into a database with
no tickets, and credentials only to agents without one. Empty volume → all migrations, 40 tickets,
6 agents; existing database → nothing changes. In production migrations would be a separate
release step, not part of startup ([why](APPROACH.md#task-4---docker)).

## Tests

```bash
npm test          # dotnet test (3 projects), then Vitest - no database, Docker or secret needed
```

`UnitTests` (domain rules, handlers, EF queries on in-memory SQLite), `ArchitectureTests` (layer
rules, model vs migrations), `ApiTests` (the real HTTP pipeline via `WebApplicationFactory`:
authentication, authorization, `/health`), and Vitest + React Testing Library for the web app.
Last full run: **196 unit, 4 architecture, 30 API, 83 web - all passing**; `dotnet format`, ESLint,
`tsc` and the production build clean. Not automated: browser end-to-end flows (checked manually),
load and concurrency, visual layout. Method and coverage: [APPROACH.md](APPROACH.md#testing-and-verification).

## API

Every endpoint needs `Authorization: Bearer <token>` except login, `/health` and Swagger.

| Endpoint | |
| --- | --- |
| `POST /api/auth/login` · `GET /api/auth/me` | sign in (token, expiry, agent) · the signed-in agent |
| `GET /api/tickets` | paged; `search`, `status`, `priority`, `categoryId`, `customerId`, `assignedAgentId`, `unassignedOnly`, `slaStatus`, `sortBy`, `sortDirection` - all in SQL, AND-combined |
| `POST /api/tickets` | create; returns the triaged ticket and `triage` reasons (201) |
| `GET /api/tickets/{id}` · `PATCH .../status` · `PATCH .../assignment` | one ticket · change status · reassign |
| `POST /api/tickets/{id}/escalate` · `GET .../escalations` | escalate (`{ reason }`; 200/400/404/409) · history, newest first |
| `GET /api/customers` · `/agents` · `/categories` | reference data |
| `GET /health` | `200 Healthy` / `503 Unhealthy` - database reachability, no details |

Errors are ProblemDetails: 400 validation, 401 not signed in, 404, 409 business rule.

## The defect (DEFECT-117)

**Root cause:** the ticket list's fetch effect depended only on `search`, `page` and `pageSize`
(the lint warning was silenced), so changing any other filter never re-ran it and the next search
fetched with the stale accumulated filters. **Fix:** the hook keeps the query sent to the API
separate from the search box text; any query change fetches immediately and only the text is
debounced (300 ms); stale responses are discarded. **Verify:** `useTicketList.test.ts` asserts the
request sent for every filter; 11 of its tests fail on the original hook. Detail:
[APPROACH.md](APPROACH.md#defect-117).

## Design decisions (summary)

| Chosen | Rejected, and why |
| --- | --- |
| Business rules as small domain units shared by create and escalate (`SlaPolicy`, `PriorityRule`, `AgentSelector`) | rules in handlers or controllers - duplicated between create and escalate, hard to test |
| `EF.Functions.Like` with escaping for search | `Contains` - case-sensitive on SQLite, insensitive on SQL Server: tests would lie |
| `SlaWindowSeconds` stored next to the due date; status always derived | storing the at-risk moment (threshold changes would not apply); joining the latest escalation per row |
| One index `(TicketId, EscalatedAtUtc DESC)` on history | per-column indexes - the only read is one ticket newest first |
| `PasswordHash` column on `Agents`, Identity `PasswordHasher` | a Users table (a second identity for roles that are out of scope); a home-made hash |
| Fallback authorization policy | `[Authorize]` per controller - a new endpoint would be open by default |
| Token in `sessionStorage` | `localStorage` (outlives the tab); memory only (reload signs out). Production: httpOnly cookie |
| Migrations on start in Development only | on start in production (races, DDL rights, failed rollouts) |

Patterns used and why they fit: domain services/policy objects, the options pattern with startup
validation, CQRS-style handlers with projections - see [APPROACH.md](APPROACH.md#design-patterns-used-and-why-they-fit).

## Database

Two migrations were added. `AddTicketEscalationsAndSlaWindow`: the `TicketEscalations` table
(restrict foreign keys, a one-level check, the index above) and `Tickets.SlaWindowSeconds` with a
check constraint and an exact backfill. `AddAgentPasswordHash`: `Agents.PasswordHash nvarchar(200) NULL`.

## Docker verification

| Check | How | Result |
| --- | --- | --- |
| `down -v && up --build` on an empty volume | run as a separate compose project so the working database was kept | healthy in 34 s; 3 migrations, 40 tickets seeded; `/health` 200; login and `/api/tickets` work |
| Restore layer cached | change one `.cs` file, rebuild | `RUN dotnet restore` CACHED; rebuild 23 s (cold 951 s) |
| Image size | `docker images` | runtime 455 MB vs 2.16 GB for the SDK stage |
| Not root | `docker compose exec api whoami` | `app` (uid 1654) |
| Nothing baked in | `docker image inspect`, `ls /app` | no key, connection string or environment; no `appsettings.Development.json`, SDK or source |
| Health when the database is down | stop SQL Server | 503 in 5.0 s, back to 200 when it returns |
| DK-8 | web app with `VITE_API_PROXY_TARGET=http://localhost:5080` | signed in and listed tickets from the container |

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| API exits: `Jwt:SigningKey must be at least 32 bytes` | set the key (Run it, above), then `docker compose up -d api` |
| API cannot reach the database | wait for `sqlserver` to be `healthy`; `MSSQL_SA_PASSWORD` must match the one the volume was created with |
| SQL Server exits with *PAL initialization error 101* (empty `.system/instance_id` in the volume) | its first start was interrupted: `docker compose down -v` wipes it, or `docker compose -p <new-name> up -d` keeps it and uses a fresh one |
| `docker` fails with *dockerDesktopLinuxEngine ... cannot find the file* | start Docker Desktop and wait for "Engine running" |
| Port 5080 / 1433 in use | change the left side of `ports:` in `docker-compose.yml` |
| Web app shows "Unable to load tickets" | API not running, or `VITE_API_PROXY_TARGET` points at the wrong port |

## Known limitations

Concurrent assignment and escalation are not race-safe. Tokens cannot be revoked before they
expire (only `/me` re-checks the agent); no refresh tokens, lockout or rate limiting (out of
scope). The token in `sessionStorage` is readable by XSS. `%term%` search cannot use an index.
The API container has no Docker healthcheck (no curl in the runtime image) and serves plain HTTP;
compose runs it in Development. `db/*.sql` reference scripts were not updated. Optional SLA summary
report (2.5) not built. More in [APPROACH.md](APPROACH.md#assumptions-and-limitations).

## AI usage

**Tools and purpose.** Claude (Anthropic), in Claude Code, did most of the work under my
direction, phase by phase: analysing the repository and requirements, planning, writing and
refactoring the code, debugging, writing the automated tests, the live verification scripts and
this documentation. I set the scope and rules for each phase (task order, separate commits, no
pushing or destructive Docker operations without approval), reviewed each phase's report and the
decisions it raised, and approved before the next phase began.

**How generated code was verified.** Every phase ended with the full build, `dotnet format`,
all automated tests, lint and typecheck; then live checks against the running API and SQL Server
(filter counts against independent SQL queries, SLA statuses against an independent T-SQL
evaluation, login and token tampering, escalation records), browser checks of the web app, and
for Task 4 the Docker checks above. Because most tests were written alongside or after the code,
critical rules were deliberately broken one at a time to confirm the tests catch each break.

**A workflow that worked:** regression tests first for the defect, shown failing on the original
code, then the fix. **Accepted as-is:** the layering of the Task 2 rules into small domain units.
**Changed or rejected:** `string.Contains` for search was replaced once a test showed SQLite and
SQL Server disagreeing on case; a React-context design for the session was rejected for a timing
gap. **Bugs found during verification (several introduced by the AI-written code):** the SQL SLA
filter and the per-row status could disagree within a millisecond of a due time; `/health` took
20 s with the database down; a double-submit guard had no test that could fail; documented Windows
commands failed on PowerShell 5.1 (fixed and re-run). A test-harness mistake once wrote an invalid
local signing key into user-secrets; it was detected and replaced.
