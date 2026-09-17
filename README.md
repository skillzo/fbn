# NovaWallet Ledger Service

Wallet ledger for the NovaPay take-home. Amounts are **kobo** only (`long`). Stack: .NET 8, PostgreSQL 16, JWT, Docker.

## Run

```bash
docker compose up --build
```

- Swagger: http://localhost:8080/swagger
- Health: http://localhost:8080/health

Local API (Postgres on `localhost:5432`):

```bash
dotnet run --project src/NovaWallet.Api
```

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

Use `role: "system"` for `POST /wallets/{id}/credit`.

## API

| Endpoint | Notes |
|----------|--------|
| `POST /wallets` | One wallet per JWT `sub` |
| `GET /wallets/{id}/balance` | Owner only (404 otherwise) |
| `POST /wallets/{id}/credit` | System role only (mock inbound NIP) |
| `POST /transfers` | Requires `Idempotency-Key` header |
| `GET /wallets/{id}/transactions` | Paginated statement, newest first |

## Design notes

### Balance updates

Balances change only via SQL `UPDATE … RETURNING` inside a DB transaction — never read-modify-write in C#. Debits use `WHERE balance >= amount` so concurrent transfers cannot overdraw. Wallet rows are updated in **id order** to reduce deadlocks. Retries on deadlock / serialization failure.

### Idempotency

Keyed by `(customer_id, Idempotency-Key)`. Body hashed (`from|to|amount`). Same key + same body returns the stored **201**. Same key + different body → **422**. Key is claimed in the transfer transaction; on failure the key rolls back so the client can retry.

### Daily limit

Outbound total per wallet per **WAT** calendar day (`UTC + 1 hour` → date). Default ₦500,000 (`Transfers:DailyLimitKobo`). Enforced with a conditional SQL upsert, not app-level read/write.

### Audit

Balance mutations append to `audit_log` (separate from `transactions`). A Postgres trigger blocks `UPDATE`/`DELETE` so the trail stays append-only.

### Stretch (optional)

- **Outbox:** `TransferCompleted` written in the same transaction as the transfer; background worker polls with `FOR UPDATE SKIP LOCKED` and logs via `IEventPublisher`.
- **Rate limit:** `POST /transfers` fixed window (60/min). **Limitation:** one shared bucket per process, not per customer `sub`.

## Assumptions

- NGN only; amounts in kobo
- One wallet per customer
- Inbound NIP mocked as system-role credit
- Dev JWT issuer (`POST /dev/token`) for local/demo auth — not a production IdP
- Errors use Problem Details (`code`, `traceId`)
