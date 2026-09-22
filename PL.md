Went through the actual repo (not the take-home PDF from memory) and the deck you submitted. Here's the plan, in the order we'll run it.

## Phase 1 — Defend the deck (slide by slide)

For each slide, know the one follow-up question that breaks a shallow answer:

| Slide | Likely follow-up |
|---|---|
| Problem & Scope | "Why kobo as `long` and not `decimal`?" — have the float-drift answer crisp, not rehearsed-sounding |
| Architecture | "Why no repository layer / CQRS?" — you kept it thin on purpose, say why that was the right call for this scope |
| Data Model | "Why no `ledger_entries` / double-entry table?" — this is the biggest scope simplification in the whole design, own it before they spot it |
| Transfer Flow | "Walk me through what happens on attempt 3 of the retry loop if it still deadlocks" — trace it live, don't summarize |
| Why Pessimistic Locking | "What if two API instances run this, not just threads?" — the lock is a DB row lock, not in-process, so it already holds; say that explicitly |
| Idempotency & Daily Limit | "What if the original request is still running when the retry lands?" — you have a real answer (bounded 3s poll, then 409), most candidates don't |
| Security & Test Coverage | "Where's the JWT key stored?" — it's in `docker-compose.yml`, plaintext, committed. Don't let this be a surprise |
| AI Usage | This is bare on purpose. Have the tool, the 2–3 prompts, and the bug story ready to say cold, unscripted |

## Phase 2 — Defend the code (design decisions, not features)

Six decisions you made that a sharp panelist will poke at. You need the "why," not just the "what":

1. **GUID comparison for lock ordering** — why comparing `Guid`s (not IDs or timestamps) still guarantees a consistent global order between any two wallets.
2. **Pre-checks outside the transaction** — ownership/existence checks run before `BeginTransactionAsync`. Why that's fine: they're authorization checks, not the money-safety check. The balance guarantee happens later, under the lock, regardless of what the early read saw.
3. **`RunTransferAsync` fully retried on deadlock** — including re-claiming the idempotency key. Why re-claiming is safe to repeat.
4. **No explicit max transfer amount** — walk through why the daily-limit check (`amountKobo > limit` throws first) already acts as a hard ceiling, so it's covered even though it looks missing.
5. **Recursive `IsTransient`** — why a `DbUpdateException` wrapping a Postgres deadlock still gets retried by the outer loop even though the inner catch only matches unique-violations.
6. **`AddNpgSql` health check** — single `/health`, not split live/ready. Be ready to say why that's an acceptable simplification here and what you'd split in a real orchestrated deployment.

## Phase 3 — Gaps to own before they're found

These are real, current, and sitting in the exact files you'll share your screen on. Say them unprompted if the conversation gets near them; volunteering beats being caught.

- **Credit endpoint has no idempotency.** Transfer does. A retried NIP webhook would double-credit. This is the strongest AI_USAGE-style story you *didn't* write down. Have it ready anyway.
- **Rate limiter is one global bucket**, not per customer. You already documented this, good. Rehearse actually demonstrating it: two tokens, hammer `/transfers`, watch customer B get 429'd by customer A's traffic.
- **No settlement/system wallet.** Credit conjures balance rather than debiting a contra-account. Real production ledger needs one; say what it would look like.
- **Secrets committed in `docker-compose.yml`.** JWT key and DB password, plaintext. "Convenience for a one-command reviewable demo; production pulls these from a secrets manager at deploy time."
- **Statement pagination is OFFSET, not keyset.** Fine at this scale, degrades at large page numbers. Know the keyset alternative in one sentence.
- **Migrations run in-process on startup**, no advisory lock. Fine for one instance. If asked "what breaks with 3 replicas," this is the answer.

## Phase 4 — Live-coding readiness

"Be ready to modify code live" is in the brief. The three most likely asks, because they're implemented but *untested*:

1. Write a test asserting **self-transfer is rejected** (`from == to`).
2. Write a test asserting **transfer to a nonexistent wallet** returns 404.
3. Write a test asserting a **single transfer above the daily limit** is rejected outright (not just cumulative breach).

We should write these for real, now, so you're not improvising xUnit syntax live.

## Phase 5 — Curveballs (adjacent scenarios, not in the brief)

- "Add a reversal/refund endpoint" — talk through it, don't need to build it.
- "How would this change for multi-currency?" — `Currency` column exists but isn't enforced; know the gap.
- "This needs to scale to 3 instances behind a load balancer" — what changes, what doesn't (the lock already works, the rate limiter and in-process migration don't).
- "Real NIP integration instead of the mock" — signature verification, retries, and why that maps onto the credit-idempotency gap above.

## Phase 6 — Rapid-fire mock round

Once 1–5 are solid, I fire quick-succession questions across all of it, no warm-up, timed like the real thing.

---

Say **go** and we start with Phase 1.