# AI usage

## Tools used

- **Cursor (Composer)** — scaffolded the .NET 8 API, EF Core / Postgres schema, Docker Compose, JWT auth, transfer flow, Testcontainers tests, and first-pass README.
- Used for speed on boilerplate and standard patterns. Money-path behaviour (concurrency, kobo, idempotency, limits) was reviewed and verified with tests, not taken on trust.

## Example prompts

### Prompt 1 — scaffold, with the invariant stated up front

> Build the transfer endpoint so balance can never go negative under concurrent requests. Don't read the balance in C# and then write it back — I want a single SQL statement that checks and updates atomically. Explain why a naive read-then-write would fail under load before you write the code.

**Why this prompt, not "build a transfer endpoint":** asking for the invariant and the failure mode first forces the model to design around the constraint instead of bolting a check onto whatever it defaults to. It came back with the `UPDATE ... WHERE balance >= @amt RETURNING balance` pattern in [TransferService.cs:263-276](src/NovaWallet.Api/Services/TransferService.cs#L263-L276) and correctly explained why a `SELECT` then `UPDATE` in application code leaves a gap two concurrent requests can both pass.

### Prompt 2 — idempotency, with the race named explicitly

> Add Idempotency-Key handling for POST /transfers. If two requests with the same key arrive at the same instant, exactly one must process and the other must return the identical result. Don't check for the key's existence and then insert it — that's a check-then-act race. Use a unique constraint as the only source of truth for "has this key been claimed."

**Output:** the `IdempotencyKey` composite primary key on `(customer_id, key)` and the insert-and-catch-the-unique-violation pattern in [TransferService.cs:164-188](src/NovaWallet.Api/Services/TransferService.cs#L164-L188), instead of the check-then-insert shape it would default to if I'd just said "add idempotency."

### Prompt 3 — the test, with the assertion shape specified

> Write a concurrency test: fund a wallet for exactly half of N parallel transfer requests, then assert exactly N/2 succeed, N/2 return 422, and the final source/destination balances are exact — not just "no exceptions thrown." Run it against a real Postgres via Testcontainers, not the EF in-memory provider — money correctness can't be verified against a database that doesn't actually take locks.

**Output:** [TransferTests.cs:9-59](tests/NovaWallet.Tests/TransferTests.cs#L9-L59) — 1,000 parallel transfers against a 50,000-kobo balance, asserting `success == 500`, `rejected == 500`, `sourceBal == 0`, `destBal == funded`. Specifying the exact numbers as assertions (not "check it doesn't crash") is what makes this test catch a regression instead of just exercising the code path.

## Where the AI's output was actually wrong

### Credit path returned 500 on a zero-row result

The first credit implementation used `SqlQuery<long>` against `UPDATE ... RETURNING balance` but treated an empty result set ambiguously — it didn't distinguish "wallet not found" from a real value, and threw an unhandled exception. Under Testcontainers this surfaced as **HTTP 500 on `/wallets/{id}/credit`**, which meant every downstream transfer test that depended on funding a wallet first was failing for the wrong reason.

**How I caught it:** `dotnet test` — every failure traced back to `CreditAsync`, not to the transfer logic itself.

**Fix:** matched the same pattern already used for debits — map to a small row type, treat zero rows as `404 wallet_not_found` explicitly. See [WalletService.cs:130-138](src/NovaWallet.Api/Services/WalletService.cs#L130-L138).

## Where I went back and caught things myself, after the AI-assisted first pass

I re-read my own first pass critically before submitting — not just accepting that it built and tests were green — and found three more gaps:

**Rate limiter was a single global bucket, not per customer.** The first prompt ("add rate limiting on POST /transfers") got a working-looking `AddFixedWindowLimiter("transfers")` — 60 requests/minute, but shared across every caller on the instance. I caught it by mentally running a two-customer scenario: one noisy customer would 429 everyone else. Re-prompted specifically for partitioning by the JWT `sub` claim, which is what produced `AddPolicy` + `RateLimitPartition.GetFixedWindowLimiter` in [Program.cs:35-53](src/NovaWallet.Api/Program.cs#L35-L53) — and moving `UseRateLimiter()` to *after* `UseAuthentication()`/`UseAuthorization()` in the pipeline, since the partition key reads `httpContext.User`, which isn't populated yet any earlier in the pipeline.

**Migrations had no protection against multiple replicas.** The original `await db.Database.MigrateAsync()` would let two API instances booting at once both attempt the same schema migration concurrently. Fixed with a Postgres advisory lock — single-writer at startup, see [Program.cs:95-108](src/NovaWallet.Api/Program.cs#L95-L108).

**Credit endpoint had no idempotency protection at all**, while transfers did — an inconsistency that would let a retried NIP callback double-credit a wallet. Fixed using the same insert-and-catch-unique-violation pattern as transfers, see [WalletService.cs:107-176](src/NovaWallet.Api/Services/WalletService.cs#L107-L176).

## What I did not trust the model on, without independent verification

- No read-balance-in-C#-then-write for wallets, anywhere — verified by grepping for it, not by asking the model to confirm its own work.
- No process-local `lock`/mutex for correctness — this has to hold across multiple API instances, so any concurrency control that isn't in the database doesn't count.
- Idempotency and the daily limit must commit or roll back atomically with the transfer, in one database transaction — verified by a test that forces a mid-transfer failure and checks the daily-limit row rolled back with it, not just that the transfer itself failed.
