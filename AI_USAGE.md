# AI usage

## Tools used

- **Cursor (Composer)** — scaffolded the .NET 8 API, EF Core / Postgres schema, Docker Compose, JWT auth, transfer flow, Testcontainers tests, and first-pass README.
- Used for speed on boilerplate and standard patterns. Money-path behaviour (concurrency, kobo, idempotency, limits) was reviewed and verified with tests.

## Example prompts

### Prompt 1

> Build NovaWallet Ledger: .NET 8 minimal APIs, Postgres, kobo-only balances, atomic transfers, Idempotency-Key, daily WAT limit, JWT, docker compose.

**Output (summary)**  
Project layout, entities, `TransferService` with SQL `UPDATE … RETURNING`, compose file, Swagger, Problem Details. Good starting structure; I kept it thin (no MediatR / generic repositories).

### Prompt 2

> Add a concurrency test: fund a wallet, run many parallel transfers, assert only the funded amount succeeds and balances are exact.

**Output (summary)**  
xUnit + Testcontainers + `WebApplicationFactory` tests. Extended to 1,000 parallel transfers (fund 50,000 kobo → expect 500 success / 500 insufficient). Also added a short load summary written to `load-summary.txt`.

### Prompt 3

> Add rate limiting on POST /transfers with AddFixedWindowLimiter.

**Output (summary)**  
60 requests/minute fixed window on the transfers policy. Looked complete as a stretch goal until I checked how partitioning works.

## When the model was wrong

### Credit path broke funding (caught by tests)

The first credit implementation used `SqlQuery<long>` with `RETURNING balance` (and treated a zero result ambiguously). Under Testcontainers this surfaced as **HTTP 500 on credit**, so transfer/concurrency tests never got a fair run.

**How I caught it:** `dotnet test` — failures all stacked on `CreditAsync`.

**Fix:** same pattern as transfers — `RETURNING balance AS "Balance"` mapped to a small row type; empty result set means wallet not found.

### Rate limiter is global, not per customer

`AddFixedWindowLimiter("transfers")` creates **one shared bucket** for the process. Sixty transfers/min total, not per `sub`. One noisy customer can 429 everyone else on that instance.

**How I caught it:** reviewing ASP.NET rate-limiting behaviour and thinking through a two-token live demo.

**Decision:** documented as a known gap in the README (partition by `sub` / gateway in prod). Stretch goal kept simple on purpose; silence would have been worse.

### What I did not trust the model on

- No read-balance-in-C#-then-write for wallets
- No process `lock` for correctness (must work with multiple API instances)
- Idempotency and daily limit must commit or roll back with the transfer in one DB transaction
