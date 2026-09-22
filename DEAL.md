# NovaWallet Interview Prep — Continuation Spec

Paste this into a new chat along with: the take-home PDF, the submitted `.pptx`, and the code
(`fbn.zip`, or just the repo link). This file exists so nothing found in the previous session gets
lost.

## Context

- **Candidate name:** Chukwu Emmanuel on the deck and repo. FirstBank's original invite email was
addressed to **Oluwaseyi O Olaoye** — unresolved mismatch, flagged once, not re-litigated since.
- **Role:** Backend Engineer, .NET/C#, FirstBank Digital Factory (fictional NovaPay scenario).
- **Take-home:** NovaWallet Ledger Service — a wallet ledger that must survive concurrent transfers
without ever going negative or double-spending.
- **Case study deck:** already submitted as PowerPoint (10 slides). Locked — do not suggest
reopening it unless the person raises it.
- **Live assessment:** Tuesday, 22 September 2026, Microsoft Teams, time to be confirmed
separately. Bring the repo up on screen, walk the panel through the design, be ready to modify
code live.
- **Repo:** github.com/skillzo/fbn. Code is still editable before the 22nd.
- **Grading criteria (from the brief):** correctness & concurrency safety, code quality &
architecture, test rigor, security awareness, AI fluency & judgment, communication.



## Deck as submitted (10 slides, final)

1. Title — NovaWallet Ledger Service
2. Problem & Scope
3. Architecture (right-side card now shows the real load-test line, not prose)
4. Data Model
5. Transfer Flow: Core Design (has the 500/500 concurrency stat card)
6. Why Pessimistic Locking
7. Idempotency & Daily Limit (What I did / Why / Result structure)
8. Security & Test Coverage (still paragraph-card style — a terminal-snippet redesign was
  suggested but never applied before submission)
9. AI Usage (deliberately bare — four pill tags: Tools, Prompts, Mistakes Caught, Improvements —
  candidate talks through this live, nothing scripted on the slide)
10. Thank You (navy, mirrors the title slide)



## Code, as it actually stands (verified by reading the repo, not assumed)

Stack: .NET 8 minimal API, single project (`NovaWallet.Api`), no repository/CQRS layers —
deliberate simplicity call. Postgres via EF Core for schema, raw parameterized SQL
(`db.Database.SqlQuery<T>`) for every money-path mutation. Tests: xUnit + Testcontainers +
`WebApplicationFactory`, 12 tests, real Postgres container per run. `docker-compose.yml`,
`README.md`, `AI_USAGE.md` are all present and filled in.

### Mechanisms implemented (know these cold)

- **Money type:** `long` kobo everywhere. No float/decimal in the money path.
- **Debit:** `UPDATE wallets SET balance = balance - @amt WHERE id=@id AND balance >= @amt RETURNING balance`. Zero rows returned → `insufficient_funds` (422). This conditional UPDATE
*is* the lock — a row-level write lock held to commit, not an app-level lock, so it holds across
multiple API instances too.
- **Deadlock avoidance:** wallets are locked in `Guid.CompareTo` order (lower GUID first) on every
transfer, so A→B and B→A always take locks in the same global order.
- **Idempotency:** `IdempotencyKey` row inserted with a `(customer_id, key)` composite PK.
Insert-and-catch on unique-violation, never check-then-insert. The loser rolls back, then polls
(`WaitForCachedResponseAsync`, 60 × 50ms = 3s max) for the winner's cached response. Same key +
different request hash → 422. Response is written in the same transaction as the transfer, so it
commits or rolls back with it.
- **Daily limit:** one atomic upsert into `daily_limits`, cap enforced inside the `WHERE` clause of
the `ON CONFLICT DO UPDATE`. `amountKobo > limit` is checked up front so a first-ever oversized
transfer can't slip through the `INSERT` branch (which the `WHERE` doesn't gate). WAT = fixed
UTC+1, no DST in Nigeria, so `AddHours(1)` is correct year-round.
- **Retry:** the whole `RunTransferAsync` (including re-claiming the idempotency key) retries up to
3× on deadlock/serialization failure only, via a recursive `IsTransient` check that unwraps inner
exceptions — so a deadlock surfacing through `DbUpdateException` during the idempotency insert
still gets caught by the outer retry loop, not just the inner unique-violation catch.
- **Audit:** append-only `audit_log`, Postgres trigger blocks `UPDATE`/`DELETE` at the engine level.
- **Outbox:** `TransferCompleted` row written in the same transaction as the transfer. Background
`OutboxPublisher` polls with `FOR UPDATE SKIP LOCKED`.
- **Auth:** JWT bearer, global `FallbackPolicy = RequireAuthenticatedUser()` (confirmed — nothing
is anonymous by accident), `SystemOnly` policy (role claim) gates the credit endpoint. Ownership
checks for transfer source and wallet reads happen via plain `AsNoTracking` queries *before* the
transaction opens — that's fine, because they're authorization checks; the actual money-safety
guarantee happens later, under the lock, regardless of what that early read saw.
- **Errors:** RFC 7807 Problem Details, `code` + `traceId` (`HttpContext.TraceIdentifier`).
Unhandled exceptions are logged and returned as a generic 500 with no leaked detail.
- **Rate limiting:** fixed window, 60/min, on `POST /transfers` only.



### Known gaps (real, current, sitting in the files — own these before they're found)

1. **Credit endpoint has no idempotency.** No session/reference id, no dedupe. A retried NIP
  webhook would double-credit. Not mentioned in the README's limitations section — only the rate
   limiter is. Strongest unwritten AI_USAGE-style story; have it ready anyway.
2. **Rate limiter is one global bucket**, not partitioned by customer `sub`. Documented in the
  README and AI_USAGE.md already. Rehearse actually demonstrating it live: two tokens, hammer
   `/transfers`, show customer B getting 429'd by customer A's traffic.
3. **No settlement/system wallet.** Credit conjures balance directly rather than debiting a
  contra-account — this is not real double-entry bookkeeping. Know what a production version
   would need.
4. **Secrets committed in** `docker-compose.yml`**, plaintext:** the JWT signing key and DB password.
  High-probability question since "security awareness" is graded and this file is the first thing
   opened on screen.
5. **Statement pagination is OFFSET (**`Skip`**/**`Take`**), not keyset.** Fine at this scale, degrades at
  large page numbers. Know the keyset alternative in one sentence.
6. **Migrations run in-process on startup** (`db.Database.MigrateAsync()`), no advisory lock. Fine
  for one instance; would race across replicas.
7. **No explicit max transfer amount field or check** — but the daily-limit check
  (`amountKobo > limit` throws first) already acts as a hard ceiling as a side effect, so this is
   covered even though it looks missing at first glance.
8. `Currency` **column exists but isn't enforced** beyond the `"NGN"` default — no CHECK constraint.
9. **No test for:** self-transfer rejection, transfer to a nonexistent wallet, a single transfer
  above the daily limit outright (only cumulative breach is tested). All three are implemented in
   code, none are tested — the most likely "write this live" asks.
10. **Single unified** `/health` (Postgres reachability only), not split live/ready.



## The prep plan (6 phases — this is the actual work to do)

**Phase 1 — Defend the deck, slide by slide.** For each of the 10 slides, know the one follow-up
question that breaks a shallow answer (see the gaps list above for where these bite hardest: slide
8 → docker-compose secrets, slide 9 → talk cold about tools/prompts/the bug caught).

**Phase 2 — Defend the code, decision by decision.** Six specific design choices to be able to
explain the *why*, not just the *what*: GUID lock ordering, pre-transaction ownership checks,
full-transaction retry including the idempotency claim, the implicit max-amount ceiling via the
daily limit, the recursive transient-exception check, and the single unified health check.

**Phase 3 — Say the gaps before they're found.** The 10-item list above. Volunteering beats being
caught, especially since security and communication are both graded criteria.

**Phase 4 — Live-coding readiness.** Write, for real, ahead of time: a self-transfer rejection
test, a transfer-to-nonexistent-wallet test, a single-transfer-over-daily-limit test. These are the
three most likely live-coding asks because they're implemented but untested.

**Phase 5 — Curveballs (adjacent scenarios not in the brief).** Reversal/refund endpoint design
talk-through. Multi-currency (the `Currency` gap above). Scaling to 3 instances behind a load
balancer (the lock already works across instances; the rate limiter and in-process migration
don't). Real NIP integration instead of the mock (signature verification, retries, maps onto the
credit-idempotency gap).

**Phase 6 — Rapid-fire mock Q&A.** Run once phases 1–5 are solid. Timed, no warm-up, mixed across
all of the above.

## Where we left off

Phase 1 had not started yet. Next step in the new chat: pick up at Phase 1, slide by slide.

## Working notes for whoever continues this

- Ground every answer in the actual code/deck above — this candidate does not want generic
interview-prep filler.
- Communication style: terse, direct, fragment-style is fine, no hand-holding, no sugarcoating.
- Written prose (emails, cover letters) should be human-sounding, no em dashes.

