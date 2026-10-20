# Approach

How each task was approached, the decisions behind it and what was rejected. The
[README](README.md) says how to run it; this document says why it is built the way it is.
Requirement IDs (FL-, BR-, AU-, DK-) refer to `ASSIGNMENT_CANDIDATE.md`.

**Working method.** Each task was a separate phase on its own branch: read the requirement,
inspect the existing code, implement inside the existing clean architecture (Domain ←
Application ← Infrastructure/Presentation), test, verify against the real SQL Server (and
later Docker), then commit. Every phase was reviewed before the next started. Claude (AI) did
most of the implementation work under that review - see *AI usage* in the README.

---

## Task 1 - Defect and server-side filtering

### DEFECT-117

**Symptom.** Changing Status sent no request; the next search keystroke "brought it back"; clearing
the search made it stale again.

**Root cause.** `useTicketList`'s fetch effect listed only `search`, `page` and `pageSize` as
dependencies (with the exhaustive-deps lint rule silenced on the grounds that the filters object
"changes every render" - it does not; it is state). Other filter changes never re-ran the effect,
and the next search fetch read whatever filters had accumulated in state.

**Fix.** The hook now holds two pieces of state: `query` (what is sent to the API) and
`searchText` (what is in the box). The fetch effect depends on the whole `query`, so *any* change
fetches immediately - including Task 2's `slaStatus`, which needed no wiring. Only `searchText` is
debounced (300 ms) into `query`. Two problems the original also had were fixed with it:

- **Out-of-order responses:** each fetch's effect cleanup marks it stale, so a slow response
  cannot overwrite a newer one (React's documented pattern, no `AbortController` needed).
- **Loading state** is derived (`latest request answered?`) instead of set inside the effect,
  which also satisfies React 19's `set-state-in-effect` lint rule.

*Rejected:* adding the missing dependencies to the old effect (still debounces every filter
change); a remount key or forced reload (hides the symptom; the assignment counts these as not
fixed).

**Order.** The regression tests were written first and run against the original hook: 11 of 13
failed, each because a request was missing or a stale response won.

### Server-side filtering (FL-1..6)

`TicketQueries.ApplyFilters` adds one `Where` per supplied filter to the EF query *before*
`CountAsync`, sort, `Skip` and `Take`, so the database filters, counts and pages and
`totalCount`/`totalPages` describe the filtered set. Chained `Where`s are AND (FL-4); a new filter
is one more `if` (FL-6 - `slaStatus` was added exactly that way).

**Search** (FL-3) is trimmed, ignored when blank, and matches title, reference or customer name
with `EF.Functions.Like` and `%`, `_`, `[` escaped. *Rejected:* `string.Contains`, which becomes
`instr()` on SQLite - case-sensitive - while SQL Server's default collation is not: the tests and
production would have disagreed. This was checked by swapping it in (6 search tests failed) and
back. The SQL was checked in the EF command log against SQL Server: one parameterised
`WHERE ... LIKE @p ESCAPE N'\'` in both the `COUNT(*)` and the `OFFSET/FETCH` query.

---

## Task 2 - Triage, escalation and SLA

The assignment says the challenge is *where the rules are placed*. They are small, framework-free
units in `SupportDesk.Domain/Triage`, each unit-tested on its own and **shared by create and
escalate**:

| Unit | Rule |
| --- | --- |
| `PriorityRule` | BR-1: requested or Medium; Critical when the category's `ForcesCriticalPriority` flag is set (flag, never name) |
| `SlaPolicy` + `SlaSettings` | BR-2/3/6: base window per priority, Premium multiplier, minimum window, all from configuration |
| `AgentSelector` + `AgentWorkload` | BR-4/5: eligible = active, strictly below the limit, specialised where required; fewest open; ties to lowest id |
| `TicketTriage` | create: combines the three |
| `TicketEscalator` | BR-7: one level up, the new window from `SlaPolicy`, the owner from `AgentSelector.KeepOrReassign` |
| `SlaEvaluator` | BR-8: the status, a pure function of dates, window length, "now" and the threshold |

Handlers only load data (customer tier, category flags, agent workloads), call the unit and save.

**Triage on create.** No eligible agent is a normal outcome: 201, unassigned, with the reason.
The response is the existing ticket DTO plus a `triage` object (reasons for priority, window and
owner) omitted from the JSON elsewhere, so `GET /tickets/{id}` and the existing client were
unchanged. Workloads come from one query with a correlated `COUNT` of open tickets - not a load of
tickets.

**Decisions.** Ties go to the lowest agent id (stable, deterministic). The 1 h floor applies to
the Premium-reduced window, as BR-3 words it. Configuration is bound with the options pattern and
validated *at startup*, naming the bad key. On escalation the current owner keeps the ticket while
still eligible, with this ticket left out of their count - an agent at 6/6 is not taken off a
ticket merely for holding it.

**Escalation and history.** `Ticket.Escalate` raises the priority exactly one level, restarts the
window from the escalation time and creates the immutable `TicketEscalation` record itself, so the
record cannot disagree with the change. The handler saves both in one `SaveChanges` (one
transaction; tested by forcing a foreign-key failure - neither is saved). 409s name the reason
(`already Critical`, `is Resolved`). The table has `Restrict` foreign keys (an audit trail is not
deleted with its ticket), a `ToPriority = FromPriority + 1` check, and one index,
`(TicketId, EscalatedAtUtc DESC)`, which serves the only read - one ticket's history, newest
first - as a seek with no sort, and covers the foreign key.

**SLA status and the SQL filter.** "25% of the window remaining" needs the window's *length*, and
after an escalation the window no longer starts at `CreatedAtUtc`. So `Tickets.SlaWindowSeconds`
is stored next to the due date (set together, enforced by a check constraint; backfilled exactly
as `DueAtUtc - CreatedAtUtc`). The status itself is never stored. *Rejected:* storing the at-risk
moment (a threshold change would not reach existing tickets) and joining the latest escalation per
row (a correlated subquery on every list read). The filter is BR-8 as plain comparisons plus one
`DATEADD` on the "now" parameter (`due <= now + lead`), translatable on SQL Server and SQLite;
the lead is `(int)(window × fraction)` in both C# and SQL so both truncate alike. Two details
found by testing: "now" is truncated to the millisecond (EF sends it at `datetime2(3)` scale, so
otherwise a row's label and the filter could disagree within a millisecond of a due time), and
history is ordered on columns before projection (EF cannot sort on a record constructor).

**Frontend.** One `SlaIndicator` for list and detail: the API's status as text plus a symbol per
state, announced as "SLA: At risk" - the web app never recalculates SLA. The escalation panel
validates with the API's limits before any request, guards against double submission with a ref
(a disabled button does not stop a form submit), shows 400/404/409 as the API wrote them, and on
success uses the ticket from the response and refetches only the history.

---

## Task 3 - Authentication

- **Credentials (AU-1):** a nullable `PasswordHash` on `Agents`. Agents are the only users, 1:1;
  a Users table would add a join and a second identity for roles the assignment rules out. Every
  read path projects explicitly, so no response carries the hash (tested).
- **Hashing (AU-2):** ASP.NET Core Identity's `PasswordHasher` (PBKDF2-HMAC-SHA512, 100k
  iterations, per-password salt, versioned format) behind an `IPasswordHasher` abstraction.
  Unknown emails are checked against a dummy hash so every failure costs the same.
- **Login (AU-4):** one message - `Email or password is incorrect.` - for unknown email, wrong
  password and inactive agent. **Token:** HS256, 60 min, `sub`/`email`/`name`/`jti`; validated
  for signature (HS256 only, so `alg: none` fails), issuer, audience and lifetime (30 s skew).
- **Central authorization (AU-6):** a fallback policy requires a signed-in user on every endpoint;
  only login and `/health` are anonymous. New endpoints are protected by default. 401s are
  ProblemDetails saying when the session expired.
- **Key (AU-7):** only from user-secrets or `Jwt__SigningKey`; validated at startup (≥ 32 bytes).
- **Actor (AU-8):** the escalate request carries only `reason`; the controller passes the token's
  `email` claim. A body `escalatedBy` is ignored (tested with Sara's token and Priya's name).
- **Frontend:** a small session store read through `useSyncExternalStore` is the single source of
  the token, so the API client and React never disagree (considered and rejected: a React context
  that registers the token in an effect - a child's first fetch could go out before it ran). A 401 on a
  call that carried a token ends the session; so does reaching `expiresAtUtc`. **Storage:**
  `sessionStorage` - survives reload, not the tab; readable by XSS like any script-visible storage
  (mitigated by React escaping and the short lifetime). *Rejected:* `localStorage` (outlives the
  tab), memory only (every reload signs out). *Production:* an httpOnly cookie plus CSRF protection.

---

## Task 4 - Docker

- **Image (DK-1/3):** restore → publish → runtime. The restore stage copies only `global.json`,
  the `Directory.*.props`, `.editorconfig` and the four project files, so `dotnet restore` is
  cached until a dependency changes (verified: CACHED after a `.cs` change; rebuild 23 s vs 951 s
  cold). Runtime is `aspnet:10.0` with the published output only: HTTP 8080, the image's
  non-root `app` user (uid 1654), no SDK, source or certificates. 455 MB vs 2.16 GB for the SDK stage.
- **Nothing baked in (DK-2/4):** `.dockerignore` keeps `.env`, the web app, tests and
  `appsettings.Development.json` (local connection string) out. The SLA section moved to
  `appsettings.json` because every environment needs it. Environment, connection string, signing
  key and seed password arrive from compose / `.env` at run time.
- **Compose (DK-5):** the API waits for SQL Server's healthcheck, reaches it by service name and
  is published on 5080. `sqlserver` was left byte-for-byte unchanged so the running database and
  volume were not recreated.
- **Health (DK-7):** `/health` opens a real connection through the DbContext; 200/503 with check
  names and statuses only. Measured with SQL Server stopped: 20 s to answer, because SqlClient
  ignores cancellation until its own connect timeout; fixed with `Task.Run(...).WaitAsync(token)`
  so the 5 s check timeout holds (measured 5.0 s).
- **Migrations (DK-6):** Development runs `Migrate()` and the seeds on every start; all three are
  idempotent. A real deployment would run migrations as a release step (migration bundle or
  reviewed idempotent script), not on start: instances race, the app would need DDL rights, and a
  failed migration would take the service down mid-rollout.
- **Verification without risk:** `down -v && up --build` was run as a separate compose project
  (own volume, containers, ports) because this machine's working database volume had to be kept.

---

## Design patterns used (and why they fit)

1. **Domain services / policy objects** (`SlaPolicy`, `PriorityRule`, `AgentSelector`,
   `TicketEscalator`). The rules span aggregates (ticket, category, customer, agents) so they do
   not belong in one entity, and both create and escalate must apply them identically. Pure
   functions over plain inputs make every boundary testable without mocks.
2. **Options pattern with validation at startup** (`SlaSettings`, `JwtOptions`). Configuration
   errors stop the process with the key named, instead of surfacing as a 500 on the first ticket
   or login.
3. **CQRS-style handlers with read-side projections** (already in the codebase, extended
   consistently). Commands load aggregates and go through their methods; queries project straight
   to DTOs with `AsNoTracking`, which is what keeps credentials out of responses and lists free of
   N+1 queries.

Interfaces were added only at real seams: the password hasher, the token issuer and the current
user (each has a framework-bound implementation and a test double or test-time replacement).

---

## Assumptions and limitations

- Assignment races are not handled: two simultaneous creates or escalations can both pick an agent
  at their last slot, or escalate from the same starting state (needs optimistic concurrency or a
  serialisable check).
- Tokens are not revocable: a deactivated agent's token works until it expires; only `/me`
  re-checks the agent. No refresh tokens, lockout or rate limiting (not wanted).
- The escalation actor is stored as the agent's email in a 100-character column; a longer email
  could not escalate. Search with `%term%` cannot use an index (full-text search at scale).
- `db/schema.sql` and `db/seed.sql` are reference scripts and were not updated; the migrations are
  the source of truth. Non-ASCII case-insensitive search follows the database collation.
- After a 409 the detail page keeps showing the ticket as it was loaded (it does not refetch).

---

## Testing and verification

| Layer | What | Where |
| --- | --- | --- |
| Domain | every rule and boundary (SLA ±1 ms, thresholds, floor, ties, limits) | `UnitTests/Domain` |
| Application | handlers with mocked repositories: status codes, saving once, refusing | `UnitTests/Application` |
| Database | EF translation on in-memory SQLite: every filter, paging counts, SLA filter = evaluator for every status, workloads, atomic escalation, seeding | `UnitTests/Infrastructure` |
| HTTP | real pipeline via `WebApplicationFactory`: login, 401s, expired/forged/unsigned tokens, every endpoint protected, actor spoofing, `/health` up and down | `ApiTests` |
| Architecture | layer dependencies; EF model matches migrations | `ArchitectureTests` |
| Web | Vitest + RTL: request per filter, debounce, stale responses, SLA states, escalation validation/409/double submit, triage, login, routing, token, 401, expiry | `apps/web/src/**/*.test.ts(x)` |

**Honest order.** Task 1's tests were written first and shown failing on the original code. For
Tasks 2-4 most tests were written alongside or after the implementation. To show they still
detect defects, the critical rules were then deliberately broken one at a time (capacity `<` vs
`<=`, tie order, SLA and breach boundaries in C# and SQL, history order, keep-owner adjustment,
fallback policy, lifetime/issuer/audience validation, inactive login, actor from token, route
guard, token attachment, 401 handling, expiry, double-submit guard, anonymous `/health`); every
mutation was caught. One was not at first - the double-submit ref guard - and a test was added.

**Live checks** (scripts against the running API, SQL Server and the browser, not committed):
filters against independent SQL counts; escalation rules and history rows; SLA filter against an
independent T-SQL evaluation; login, token validation and actor recording; the web app end to end;
and every Docker check listed in the README.

**Deliberately not automated:** browser end-to-end tests (done manually), load and concurrency
tests, SQL Server-specific behaviour beyond the live checks (the automated database tests use
SQLite), and visual layout.

---

## Requirement traceability

| Req. | Where | Automated evidence | Live evidence |
| --- | --- | --- | --- |
| DEFECT-117 | `useTicketList.ts` | `useTicketList.test.ts` (11 failed on original) | browser: status, search, clear |
| FL-1..6 | `TicketQueries.ApplyFilters` | `TicketQueriesFilteringTests` | 20 filter checks vs SQL counts; EF SQL log |
| BR-1 | `PriorityRule` | `PriorityRuleTests`, handler tests | create checks (Security/Outage forced Critical) |
| BR-2/3/6 | `SlaPolicy`, `SlaSettings`, `appsettings.json` | `SlaPolicyTests` (windows, floor, config) | create/escalate windows; bad config stops startup |
| BR-4/5 | `AgentSelector`, `AgentRepository.GetWorkloadsAsync` | `AgentSelectorTests`, `AgentRepositoryWorkloadTests` | assignment and "nobody eligible" checks |
| BR-7 | `Ticket.Escalate`, `TicketEscalator`, handler | `TicketEscalationTests`, `TicketEscalatorTests`, handler/validator tests | 19 escalation checks |
| BR-8 | `SlaEvaluator`, `TicketQueries.WhereSlaStatus` | `SlaEvaluatorTests`, `TicketQueriesSlaFilterTests` | filter = T-SQL = labels for all statuses |
| 2.2 history/persistence | `TicketEscalation`, migration, `GetEscalationsAsync` | `EscalationPersistenceTests` | schema in system catalogs |
| 2.3 frontend | `SlaIndicator`, `TicketFilters`, `EscalationPanel/History`, `TriageResult` | component and page tests | browser checks |
| AU-1..5, 8 | `Agent`, `IdentityPasswordHasher`, `LoginCommandHandler`, `AuthController`, `TicketsController` | `IdentityPasswordHasherTests`, `LoginCommandHandlerTests`, `AuthenticationApiTests` | 24 live API checks |
| AU-6, 7 | `AuthenticationServiceCollectionExtensions`, `JwtOptions` | `AuthenticationApiTests` (every endpoint 401) | short key stops startup |
| AU-9..13 | `LoginPage`, `RequireAuth`, `sessionStore`, `client.ts`, `App.tsx` | `authFlow.test.tsx`, `client.test.ts` | browser: redirect, return, tamper, expiry, sign-out |
| DK-1..8 | `Dockerfile`, `.dockerignore`, `docker-compose.yml`, `Health/` | `HealthApiTests` | README *How each check was verified* |
