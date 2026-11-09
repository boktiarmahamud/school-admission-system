# Assumptions & Design Decisions

The assignment leaves several behaviours unspecified. This document records
the decisions made and the reasoning behind them.

## 1. Identity and access

| Topic | Decision |
|---|---|
| Student identity | The 17-digit birth registration number, sent in the `X-Student-Id` header. |
| Validation | Must be exactly 17 digits (0-9 only). Otherwise the API returns `400 Bad Request`. |
| Student records | A student record is created automatically the first time a valid ID is used. |
| Login | None. There is no authentication or password. |
| Admin access | No authentication; admin endpoints and UI are open. This is a **demo limitation** and is not production-secure. |

## 2. Reservation lifecycle

Statuses: `HELD`, `CONFIRMED`, `EXPIRED`, `CANCELLED`.

- A new reservation starts as `HELD` with an `ExpiresAt` timestamp.
- `HELD` -> `CONFIRMED` when the parent confirms before expiry.
- `HELD` -> `CANCELLED` when the parent cancels an active hold.
- `HELD` -> `EXPIRED` when the time runs out without confirmation.
- `CONFIRMED`, `CANCELLED` and `EXPIRED` are terminal states.
- Records are never deleted, so the full admission history is preserved.

## 3. Hold duration

- A hold lasts **10 minutes**.
- The value is configurable (`HoldDurationMinutes` setting / environment
  variable) and is defined in one place only.

## 4. Seat counting

- Held = holds with status `HELD` and `ExpiresAt` in the future
- Confirmed = holds with status `CONFIRMED`
- Available = TotalSeats - Held - Confirmed
- `EXPIRED` and `CANCELLED` holds never consume seats.
- A `HELD` hold whose `ExpiresAt` has passed is treated as expired **even if
  its stored status has not been updated yet**.
- Invariant that must always hold: Confirmed + Active Held <= TotalSeats.

## 5. Expiration strategy

- Expiration is **lazy**: expired holds are excluded when counting seats, and
  their stored status is updated to `EXPIRED` when they are next accessed.
- No background job is used. This keeps the system simple and correct, because
  counting never depends on the stored status of an expired hold.

## 6. Student rules

| Question | Decision |
|---|---|
| Multiple classes at once? | No. A student may have **one active hold at a time**. |
| Applying again after cancel or expiry? | Allowed. |
| Applying after a confirmed admission? | Rejected. A confirmed student is already admitted. |
| Cancelling a confirmed admission? | Not allowed. |
| Duplicate request for same class while holding? | Rejected with `409 Conflict`. |

## 7. Payment

- Payment is **simulated**. Clicking "Confirm / Pay" counts as successful
  payment. No payment gateway is used.

## 8. Concurrency

- Seat reservation runs inside a PostgreSQL transaction that locks the class
  row (`SELECT ... FOR UPDATE`), re-checks availability, then inserts the hold.
- Locking is done in the database, not in application memory, so it stays
  correct even with multiple backend instances.

## 9. API behaviour decisions

| Situation | Response |
|---|---|
| Missing or invalid `X-Student-Id` | `400 Bad Request` |
| Invalid class data (empty name, seats <= 0) | `400 Bad Request` |
| Class or hold not found | `404 Not Found` |
| Hold belongs to a different student | `404 Not Found` (does not reveal that the hold exists) |
| No seats available | `409 Conflict` |
| Student already has an active hold or confirmed admission | `409 Conflict` |
| Confirm after expiry | `409 Conflict` |
| Confirm a hold that is not `HELD` | `409 Conflict`, except the repeat case below |
| Confirm an already-`CONFIRMED` hold | `200 OK` with the existing state (idempotent, no duplicate admission) |
| Cancel an expired, cancelled or confirmed hold | `409 Conflict` |

Response fields required by the assignment are never renamed or removed;
extra fields may be added.

## 10. Other assumptions

- Class and shift are combined in one record (e.g. "Class 6 - Morning Shift").
- The class `Name` is not required to be unique.
- Timestamps are stored in UTC.
- The frontend refreshes data after each action; no WebSockets are used.
- All configuration (DB connection, API URL, hold duration) comes from
  environment variables, with no hardcoded values.

## 11. Known limitations

- No authentication or authorization; anyone can use admin endpoints or act
  as any student ID.
- Lazy expiration means expired `HELD` rows may remain stored until accessed.
- Simulated payment only.
