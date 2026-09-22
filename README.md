# NovaWallet Ledger Service

Wallet ledger for the NovaPay take-home. Amounts are **kobo** only (`long`). Stack: .NET 8, PostgreSQL 16, JWT, Docker.

## Run

Copy env defaults, then start:

```bash
cp .env.example .env
docker compose up --build
```

- Swagger: http://localhost:8080/swagger
- Health: http://localhost:8080/health (also `/health/live`, `/health/ready`)

Local API (Development uses `appsettings.Development.json`):

```bash
dotnet run --project src/NovaWallet.Api
```

Secrets (`JWT_KEY`, DB password) live in `.env` (gitignored). See `.env.example`.

## Test

Docker required (Testcontainers):

```bash
dotnet test
```

Concurrency load summary (optional):

```bash
dotnet test --filter Concurrent_transfers -v q
cat load-summary.txt
```

## Auth (dev)

```bash
curl -s -X POST http://localhost:8080/dev/token \
  -H 'Content-Type: application/json' \
  -d '{"sub":"customer-1","role":"customer"}'
```

Use `role: "system"` for `POST /wallets/{id}/credit`. Credit and transfer both require an `Idempotency-Key` header.

## API

| Endpoint | Notes |
|----------|--------|
| `POST /wallets` | One wallet per JWT `sub` |
| `GET /wallets/{id}/balance` | Owner only (404 otherwise) |
| `POST /wallets/{id}/credit` | System role; `Idempotency-Key` required (NIP reference) |
| `POST /transfers` | `Idempotency-Key` required |
| `GET /wallets/{id}/transactions` | Paginated statement, newest first |

## Design notes

### Balance updates

Balances change only via SQL `UPDATE … RETURNING` inside a DB transaction — never read-modify-write in C#. Debits use `WHERE balance >= amount` so concurrent transfers cannot overdraw. Wallet rows are updated in **id order** to reduce deadlocks. Retries on deadlock / serialization failure.

### Idempotency

Transfers and credits are keyed by `(scope, Idempotency-Key)` with a body hash. Same key + same body returns the stored **201**. Same key + different body → **422**. On failure the key rolls back so the client can retry. Credit scope is `system:{actor}` so inbound NIP retries do not double-credit.

### Daily limit

Outbound total per wallet per **WAT** calendar day (`UTC + 1 hour` → date). Default ₦500,000 (`Transfers:DailyLimitKobo`). Enforced with a conditional SQL upsert.

### Audit

Balance mutations append to `audit_log` (separate from `transactions`). A Postgres trigger blocks `UPDATE`/`DELETE`.

### Stretch

- **Outbox:** `TransferCompleted` in the same transaction as the transfer; worker uses `FOR UPDATE SKIP LOCKED`.
- **Rate limit:** `POST /transfers` fixed window (**60/min per JWT `sub`**).

## Assumptions / known tradeoffs

- NGN only (`CHECK (currency = 'NGN')`); amounts in kobo (`long`)
- One wallet per customer
- Inbound NIP mocked as system-role credit (no settlement/contra wallet — balance is credited directly; full double-entry is out of scope)
- Statement uses `OFFSET`/`LIMIT` pagination — fine for this size; keyset on `(created_at, id)` would be better at very high pages
- Dev JWT issuer for local/demo auth
- Errors use Problem Details (`code`, `traceId`)
- Migrations take a Postgres advisory lock on startup (safe if multiple API replicas boot together)
