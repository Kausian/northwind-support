# Northwind Support Desk

A support ticket management system: raise tickets, search and filter them, assign them to agents,
and move them through their lifecycle.

**.NET 10 Web API** + **React 19 / TypeScript** + **SQL Server**, in one monorepo.

---

## The assignment

You are joining a team that already has this app working. Complete the tasks **in order**.
Full requirements are in **[`ASSIGNMENT_CANDIDATE.md`](ASSIGNMENT_CANDIDATE.md)**.

| # | Task | Done looks like | Effort |
| --- | --- | --- | --- |
| 1 | **Fix a defect, add server-side filtering** | Root cause fixed with a regression test; every list filter applied in the database with a correct count | ~2.5 h |
| 2 | **Ticket escalation & assignment** | Automatic triage on create, escalation with history, SLA status, UI for all of it | ~7 h |
| 3 | **Login and authentication** | Agents sign in; API rejects unauthenticated calls; login UI | ~4 h |
| 4 | **Dockerise the API** *(optional)* | `docker compose up` gives a migrated, seeded API with no baked-in secret | ~1.5 h |

### Guidance

- **Time: 2 days.** Honestly marking a task "not finished" beats rushing all of them.
- **Fork** this repo to your own GitHub account and work there. **Do not push to the original.**
- **AI tools are allowed.** You must understand and be able to defend every line you submit.
- **Commit as you go** and keep the real history. Commit the Task 1 defect fix and the filtering
  separately from each other and from the feature work. A single squashed commit is not accepted.
- **Where the spec is ambiguous**, make a decision, write it down in the README, and move on.
- **Out of scope:** registration, password reset, roles, OAuth/SSO, refresh tokens, MFA,
  notifications, CI, caching, visual redesign. Mention them under *Known limitations* instead.

### Submission

1. Update this **README** with what you built, your design decisions (chosen, rejected and why),
   the defect's root cause, dev login credentials, how to run and test it, AI usage, and known
   limitations. A reviewer must be able to run it without asking you anything.
2. Add an **APPROACH.md** explaining your approach and thought process for each task.
3. Commit both, then send us the **link to your fork**, the **dev login credentials**, and a
   **rough note of time spent** on what. (If the fork is private, add the reviewers as collaborators.)

---

## Quick start

**Prerequisites:** .NET SDK 10 (`global.json`), Node.js 22 (`.nvmrc`; 20.19+ works), Docker.

```bash
cp .env.example .env          # optional, every value has a working default
npm install                   # installs the web app
npm run db:up                 # SQL Server 2022 in Docker on localhost:1433
# once per machine: a local JWT signing key, kept outside the repository (see Task 3 below)
dotnet user-secrets set Jwt:SigningKey "$(openssl rand -base64 48)" --project apps/api/src/SupportDesk.Presentation
npm run api                   # terminal 1: migrates, seeds, then serves the API
npm run web                   # terminal 2: React dev server
```

Sign in with any seeded agent, e.g. `sara.lindqvist@northwind-support.example`, and the
development password **`Local-Dev-Only-1!`** (a local fixture; see Task 3).

| What | Where |
| --- | --- |
| Web app | http://localhost:5173 |
| API | https://localhost:7043 (also http://localhost:5043) |
| Swagger | https://localhost:7043/swagger |

In Development the API applies migrations and seeds demo data on startup. **Never commit real
credentials.**

**Visual Studio 2026:** run `npm run db:up`, open `SupportDesk.sln`, set
**SupportDesk.Presentation** as the startup project, and press F5 on the **https** profile.

## Tests

```bash
npm test                      # dotnet test, then the web tests (Vitest)
```

Both pass on a clean checkout and neither needs a database or a secret. `dotnet test` runs three
projects: `UnitTests` (domain, handlers, SQLite-backed queries), `ArchitectureTests` (layering, model
vs migrations) and `ApiTests` (the real HTTP pipeline - authentication, authorization, model
binding - via `WebApplicationFactory` on in-memory SQLite, with a test-only signing key).

## Commands

| Command | What it does |
| --- | --- |
| `npm run db:up` / `db:down` / `db:reset` | Start / stop / wipe SQL Server |
| `npm run api` / `npm run web` | Run the API / the React app |
| `npm run build` | Build both |
| `npm run lint` / `typecheck` / `format` | Web lint and types / `dotnet format` |
| `npm run ef -- migrations add <Name>` | Add an EF Core migration |

## API at a glance

| Endpoint | Description |
| --- | --- |
| `GET /api/tickets` | Paged list with `search`, `status`, `priority`, `categoryId`, `customerId`, `assignedAgentId`, `unassignedOnly`, `sortBy`, `sortDirection`. Filters are applied in the database and combined with AND. |
| `GET /api/tickets/{id}` | One ticket |
| `POST /api/tickets` | Create a ticket |
| `PATCH /api/tickets/{id}/status` · `/assignment` | Change status / assignee |
| `GET /api/customers` · `/api/agents` · `/api/categories` | Reference data |

Errors are `ProblemDetails`: `400` validation, `404` not found, `409` conflict or business rule.

**Seed data:** 5 categories, 6 customers (3 Premium), 6 agents (one inactive, one at their limit)
and 40 tickets across every status, priority and SLA state.

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| API cannot reach the database | `npm run db:up`, wait ~20 s, check `docker compose ps` |
| Port 1433 already in use | Change the port in `docker-compose.yml` and `.env` |
| Browser warns about the certificate | `dotnet dev-certs https --trust`, or use the web app, which proxies |
| Web app shows "Unable to load tickets" | The API is not running, or `VITE_API_PROXY_TARGET` points at the wrong port |
| SQL Server slow on Apple Silicon | Enable Rosetta in Docker Desktop → Settings → General |

---

## Task 1: notes

### DEFECT-117: ticket filters sometimes did not take effect

**Root cause.** The list hook's fetch effect only listed `search`, `page` and `pageSize` as
dependencies (with the lint rule silenced), so changing status, priority, category, customer,
agent, "unassigned only" or sort never re-ran it; the next search then fetched with whatever
filters happened to be in state, which is why an earlier status "appeared" late.

**Fix** (`apps/web/src/features/tickets/hooks/useTicketList.ts`). The hook now keeps two pieces
of state: `query`, the filters actually sent to the API, and `searchText`, what is typed in the
search box. Any change to `query` is fetched immediately (the effect depends on the whole query,
so a new filter such as Task 2's `slaStatus` needs no extra wiring). Only `searchText` is
debounced (300 ms) before it is copied into `query`. Each fetch's effect cleanup marks it stale,
so a slow response can no longer overwrite a newer one.

**Verify.** `npm run test --workspace apps/web`: `useTicketList.test.ts` asserts the request sent
for every filter, the search debounce, clearing the search, and an out-of-order response; 11 of
those tests fail against the original hook.

### Server-side filtering

**Where.** `TicketQueries.ApplyFilters` (`apps/api/src/SupportDesk.Infrastructure/Queries`) adds
one `Where` per supplied filter to the EF Core query *before* `CountAsync`, sorting, `Skip` and
`Take`, so the database filters, counts and pages, and `totalCount`/`totalPages` describe the
filtered set. Chained `Where` calls combine with AND. Adding a filter (e.g. Task 2's `slaStatus`)
is one more `if` block in that method.

**Decisions.**
- `status`, `priority`, `categoryId`, `customerId`, `assignedAgentId` are exact matches;
  `unassignedOnly=true` means `AssignedAgentId IS NULL`; `false` means "no filter".
- Contradictory filters (`assignedAgentId` + `unassignedOnly=true`) are honoured literally (AND)
  and return an empty page rather than an error.
- `search` is trimmed, ignored when blank, and matches title, reference or customer name as a
  substring. It uses `EF.Functions.Like` with `%`, `_` and `[` escaped, rather than
  `string.Contains`: `Contains` becomes `instr()` on SQLite, which is case-sensitive, while SQL
  Server's default collation is not; `LIKE` is case-insensitive (for ASCII) on both, so the
  tests and production agree. Escaping stops user input such as `100%` acting as a wildcard.

**How the SQL was checked.** `TicketQueriesFilteringTests` run every filter, combinations,
blank search, wildcards and paging counts through EF Core against a real (in-memory SQLite)
database. The SQL Server translation was inspected with `ToQueryString()`: all filters appear in
one parameterised `WHERE ... LIKE @pattern ESCAPE N'\'` clause, ahead of `ORDER BY`/`OFFSET`.

---

## Task 2A: automatic triage and assignment

`POST /api/tickets` now decides priority, due date and owner itself and returns them, plus a
`triage` object with a human-readable reason for each (`priority`, `slaWindow`, `assignment`).
Nobody being eligible is a normal outcome: the ticket is still created (**201**), unassigned,
and the reason says why.

**Where the rules live.** Each rule is a small unit in `SupportDesk.Domain/Triage`, with no
framework or database dependency, so it is unit-tested directly and can be reused by
escalation (Task 2B):

| Unit | Rule |
| --- | --- |
| `PriorityRule` | BR-1: requested priority, default Medium; Critical when the category's `ForcesCriticalPriority` flag is set (the flag, never the name). |
| `SlaPolicy` + `SlaSettings` | BR-2/3/6: base window per priority, Premium multiplier, minimum window; numbers from the `Sla` config section. |
| `AgentSelector` + `AgentWorkload` | BR-4/5: eligible = active, strictly below `MaxOpenTickets`, specialised when `RequiresSpecialist`; fewest open tickets wins. |
| `TicketTriage` | Combines the three for a new ticket. |

`RaiseTicketCommandHandler` only loads data (customer tier, category flags, agent workloads),
calls `TicketTriage`, raises the ticket and saves.

**Decisions.**
- *Tie-break:* lowest agent id. Ids never change, so the same data always gives the same owner.
- *Open tickets* are those not Resolved or Closed (matching `Ticket.IsOpen`); the count is a
  correlated `COUNT` in the single workload query, not a load of tickets.
- *The 1 h floor* applies to the Premium-reduced window, as BR-3 states it; a configured base
  window is used as is for Standard customers.
- *Configuration:* `Sla` is bound with the options pattern and validated **at startup**
  (`ValidateOnStart`), so a missing priority or an out-of-range multiplier stops the API with the
  offending key named, instead of failing the first ticket. The section exists only in
  `appsettings.Development.json`; any other environment must supply it (e.g. `Sla__...`
  environment variables), deliberately rather than silently falling back to defaults.
- *Response shape:* `triage` is a nullable property on the existing ticket DTO, omitted from the
  JSON when null, so `GET /api/tickets/{id}` is unchanged and the existing client keeps working.
- *No schema change:* due date and assigned agent already exist on `Tickets`; triage reasons are
  returned, not stored.

---

## Task 2B (backend): escalation, history and SLA status

### Escalation (BR-7)

`POST /api/tickets/{id}/escalate` with `{ "reason": "...", "escalatedBy": "..." }`:

| Outcome | Status |
| --- | --- |
| Escalated | **200** `{ ticket, escalation }`: the updated ticket and the history row just recorded |
| Reason missing, blank, or not 5-500 characters once trimmed; `escalatedBy` missing or over 100 | **400** ProblemDetails |
| Unknown ticket | **404** |
| Ticket is Critical / Resolved / Closed | **409**, naming which (`... is already Critical ...`, `... is Resolved ...`) |

`GET /api/tickets/{id}/escalations` returns the history newest first (ties broken by id):
before/after priority, agent and due date, reason, who and when. **404** for an unknown ticket.

**Where the rules live.** `Ticket.EnsureCanBeEscalated` and `Ticket.Escalate` (the aggregate
raises the priority exactly one level, restarts the SLA window from the escalation time and
creates the immutable `TicketEscalation` record itself, so the record cannot disagree with the
change). `TicketEscalator` (domain) reuses Task 2A's units: `SlaPolicy.WindowFor` for the new
due date (BR-2/3, Premium halving and floor included) and `AgentSelector` for the owner.

**Decisions.**
- *Keeping the owner:* the current agent keeps the ticket while still eligible (BR-4/5). Their
  open count already includes this ticket, so it is left out when checking their capacity: an
  agent at 6/6 who holds the ticket keeps it. Otherwise the ticket is reassigned by the normal
  selection, or left unassigned when nobody is eligible - the escalation still succeeds.
- *Unassigned tickets* are assigned by the normal selection when escalated.
- *Atomicity:* the ticket's change and its history row are saved by one `SaveChanges`, which EF
  Core runs in a single transaction. Tested by forcing a foreign-key failure: neither is saved.
- *Actor (until Task 3):* the handler takes `escalatedBy` as a separate argument from the
  request; the controller passes the body value today and will pass the signed-in user (AU-8).

### SLA status (BR-8)

Derived on every read, never stored: no due date → `NotApplicable`; resolved on/before due →
`Met`; resolved late, or unresolved and past due → `Breached`; unresolved with at most
`AtRiskThresholdPercent` of its window left (due moment included) → `AtRisk`; else `WithinSla`.
"Now" comes from `IClock`, taken to the millisecond (the stored precision).

**Why a new `Tickets.SlaWindowSeconds` column.** "25% of the window" needs the window's
length, and after an escalation the window starts at the escalation, not at `CreatedAtUtc`.
Storing the length next to the due date (both set together, enforced by `CK_Tickets_SlaWindow`)
keeps the status derived while letting SQL compute it. *Rejected:* storing the at-risk moment
(a threshold change would not apply to existing tickets); joining the latest escalation per
ticket (a correlated subquery on every list read). The migration backfills existing tickets
as `DueAtUtc - CreatedAtUtc`, exact because none had been escalated.

### SLA filter (`GET /api/tickets?slaStatus=...`)

One more `if` in `TicketQueries.ApplyFilters` (FL-6). `WhereSlaStatus` expresses BR-8 as plain
comparisons plus one `DATEADD` on the "now" parameter (`due <= now + lead`), so it runs in SQL
before the count and the page on SQL Server and SQLite alike, with nothing loaded into memory.
The at-risk lead is `(int)(windowSeconds * fraction)` in both C# (`SlaEvaluator.AtRiskLeadSeconds`)
and SQL (`CAST(... AS int)`), so both truncate identically. Tests assert, for every status, that
the filter returns exactly the tickets the evaluator labels with it; the same was checked live
against SQL Server and against an independent T-SQL evaluation.

### Database

Migration `AddTicketEscalationsAndSlaWindow`:
- `Tickets.SlaWindowSeconds int NULL` + backfill + `CK_Tickets_SlaWindow`.
- `TicketEscalations` (Id, TicketId FK, From/To Priority, From/To AgentId FKs, From/To DueAtUtc,
  Reason nvarchar(500), EscalatedBy nvarchar(100), EscalatedAtUtc), every FK `Restrict`
  (history is an audit trail - deleting a ticket or agent must not silently remove it);
  `CK_TicketEscalations_OneLevel` (`ToPriority = FromPriority + 1`).
- **Index** `IX_TicketEscalations_TicketId_EscalatedAtUtc (TicketId, EscalatedAtUtc DESC)`: the
  only read is one ticket's history newest first, which this serves as a seek with no sort; it
  also covers the TicketId foreign key, so no separate single-column index was added.

Not done: `db/schema.sql` and `db/seed.sql` (reference scripts) were not updated; the EF
migrations are the source of truth.

---

## Task 2 (frontend): SLA, escalation and triage in the web app

- **One SLA indicator** (`SlaIndicator`) on the list and the detail page. It shows the status the
  API computed - the web app never recalculates SLA - as a word plus a distinct symbol per state
  (● Within SLA, ▲ At risk, ✕ Breached, ✓ Met, – No SLA), announced as "SLA: At risk", with
  "No due date" instead of a blank. Labels live in one map (`features/tickets/sla.ts`) shared
  with the filter.
- **SLA filter** in the filter bar sends `slaStatus` with the API's exact values. It goes through
  the same `useTicketList` query as every other filter, so it needed no extra wiring (the
  DEFECT-117 fix at work); "Clear filters" clears it.
- **Escalation panel** on the detail page: current → next priority, a reason validated with
  the API's own limits (5-500 characters, trimmed) before any request, a pending state, a guard
  against double submission, and the API's error shown as is (400 on its field, 404, 409). On
  success the ticket is replaced by the one in the response and only the history is refetched -
  no page or ticket reload. Disabled, with the reason given, for Critical, Resolved and Closed
  tickets; a 409 is still handled if the ticket changed meanwhile.
- **Escalation actor (temporary).** Until sign-in exists the user types their name, remembered in
  `localStorage`. All of it is in one hook, `features/tickets/escalationActor.ts`, which Task 3
  replaces with the signed-in agent.
- **Escalation history** under the panel, newest first as the API returns it: priority, agent and
  due date before → after, reason, who and when; loading, error-with-retry and empty states.
- **Triage result** after raising a ticket: the priority, due date and owner the API chose (or
  "Unassigned"), each with the API's reason.

---

## Task 3: login and authentication

### Development login

| Email | Password | Notes |
| --- | --- | --- |
| `sara.lindqvist@northwind-support.example` | `Local-Dev-Only-1!` | any active seeded agent works the same way |
| `priya.nair@northwind-support.example` | `Local-Dev-Only-1!` | |
| `yuki.tanaka@northwind-support.example` | `Local-Dev-Only-1!` | inactive: always refused, by design |

The password is a **local fixture** (`Seed:DevelopmentAgentPassword` in
`appsettings.Development.json`). In Development the API gives it, hashed, to every agent that
has no credential yet, on each start: a fresh database gets credentials straight away, an existing
one gets them once, and a hash that is already set is never overwritten. Nothing seeds credentials
outside Development.

### Signing key (AU-7)

The JWT signing key is never in a committed file. Locally it lives in **user-secrets** (stored in
your user profile, outside the repository); anywhere else, set the `Jwt__SigningKey` environment
variable (listed, empty, in `.env.example`). It must be at least 32 bytes; the API refuses to
start without one and says how to set it.

```bash
dotnet user-secrets set Jwt:SigningKey "$(openssl rand -base64 48)" --project apps/api/src/SupportDesk.Presentation
```

### How it works

- **Credentials (AU-1):** a nullable `PasswordHash` column on `Agents` (migration
  `AddAgentPasswordHash`). *Chosen* because agents are the only users and the relationship is
  1:1 - a separate Users table would add a join and a second identity to keep in step, for roles
  and user types the assignment rules out. *Trade-off:* the credential sits on the domain entity,
  so every read path projects explicitly; no response contains it (tested).
- **Hashing (AU-2):** ASP.NET Core Identity's `PasswordHasher` (`IdentityPasswordHasher` in
  Infrastructure): PBKDF2-HMAC-SHA512, 100,000 iterations, random 128-bit salt per password, in a
  versioned format that records its own parameters. Unknown emails are still checked against a
  dummy hash, so every failure takes the same time.
- **Login (AU-4):** `POST /api/auth/login` returns `{ accessToken, expiresAtUtc, agent }`. An
  unknown email, a wrong password and an inactive agent all get the identical
  `401 "Email or password is incorrect."`
- **Token:** HS256 JWT with `sub` (agent id), `email`, `name`, `jti`, `iss`, `aud`, `exp`.
  **Lifetime 60 minutes** (`Jwt:AccessTokenMinutes`). Validation checks signature (HS256 only -
  `alg: none` and algorithm swaps are rejected), issuer, audience and lifetime with a 30-second
  clock skew.
- **Protection (AU-6):** one fallback authorization policy requires a signed-in user on every
  endpoint; only `/api/auth/login` is `[AllowAnonymous]`. Swagger is served by middleware ahead of
  authorization, so it stays open. (The Task 4 `/health` endpoint will be marked anonymous the same
  way.) A missing, expired or invalid token gets a `401` ProblemDetails, saying when the session
  expired.
- **Me (AU-5):** `GET /api/auth/me` re-reads the agent, so one deactivated after signing in gets 401.
- **Escalation actor (AU-8):** the escalate request has only `reason`; the controller passes the
  token's `email` claim to the handler. An `escalatedBy` added to the JSON is ignored (tested:
  Sara's token with Priya's name in the body records Sara).

### Frontend

- `/login` (AU-9): labelled email and password fields, client validation, pending state, the API's
  message on a refused sign-in. Afterwards the agent returns to the page they asked for (AU-11).
- `RequireAuth` wraps every other route: nothing protected renders, or is fetched, without a session.
- The header shows the signed-in agent and **Sign out** (AU-12).
- `api/client.ts` attaches `Authorization: Bearer <token>` to every call (AU-10). A 401 on a call
  that carried a token ends the session and returns to `/login` with "Your session has expired"
  (AU-13); the session also ends by itself at `expiresAtUtc`.
- **Where the token is kept, and the trade-off:** `sessionStorage` (`auth/sessionStore.ts`). It
  survives a reload but not closing the tab, and is not shared between tabs. Like any JavaScript-
  readable storage it can be read by an XSS bug; mitigations here are React's output escaping, no
  raw HTML, and a 60-minute token. *Rejected:* `localStorage` (outlives the tab, so a stolen token
  is useful longer); memory only (every reload signs the agent out). *Production:* an httpOnly,
  Secure, SameSite cookie set by the API, which script cannot read, plus CSRF protection.

### Production changes (not done here)

The key in a secret store and rotated (validate with old and new keys during rotation); HTTPS
only; short access tokens with refresh tokens and revocation (a deactivated agent's token stays
valid until it expires - only `/me` checks); lockout or rate limiting on login; credentials issued
by an admin flow instead of a seed; the token in an httpOnly cookie.
