# BUILD SPEC — Collaborative Trip Planner ("WeGo")

You are the implementing engineer for this project. Follow this spec exactly. Where the spec is silent, choose the simplest option consistent with the rules below and record the decision in `DECISIONS.md`.

---

## 1. Product summary

A lightweight web app for 2 (later N) people to collaboratively plan a trip: shared wishlist of places on an interactive map, drag-and-drop day itinerary with time-of-day suggestions, route/travel-time feasibility checks, expense tracking with settlement, weather per day. First real trip: Mộc Châu, Vietnam. Design everything to be trip-agnostic.

## 2. Tech stack (fixed — do not substitute)

- **Backend**: ASP.NET Core 8 Minimal API, C# 12, EF Core 8 + SQLite (WAL mode), SignalR for server→client sync, `HttpClient` typed clients for OSRM and Open-Meteo.
- **Frontend**: React 18 + TypeScript (strict) + Vite. Leaflet + OpenStreetMap tiles. `dnd-kit` for drag-and-drop. TanStack Query for server state. No Redux.
- **Testing**: xUnit + FluentAssertions (backend), `WebApplicationFactory` integration tests, Vitest + Testing Library (frontend).
- **Single deployable**: backend serves the built frontend (`wwwroot`). One SQLite file. No Docker required to run locally (`dotnet run` + `npm run dev` must both work).

## 3. Domain model

All entities have `Id` (GUID), `CreatedAt`, `UpdatedAt`, `UpdatedByMemberId`. All timestamps stored as UTC; all *calendar dates* (trip days, itinerary dates) stored as `DateOnly` interpreted in the trip's timezone.

- **Trip**: Name, Destination, StartDate (DateOnly), EndDate (DateOnly), TimeZoneId (IANA, default `Asia/Bangkok`), Currency (ISO 4217, default `VND`), BudgetAmount (nullable), Status (`Planning | Ongoing | Completed`), InviteCode (8 chars, cryptographically random, unique).
- **Member**: TripId, DisplayName (1–40 chars), Role (`Owner | Editor`). Owner = trip creator. Max 10 members per trip (v1 uses 2; enforce 10 as hard cap).
- **Place**: TripId, Name (1–120), Lat (−90..90), Lng (−180..180), Category (`Food | Sight | Photo | Rest | Other`), TimeSlots (flags: `Morning | Noon | Afternoon | Evening`; at least one), EstimatedDurationMinutes (5–1440), EstimatedCost (≥0, nullable), OpenHoursText (free text ≤200, nullable), Status (`Idea | Shortlist | Confirmed | Visited | Skipped`), SkipReason (nullable), IsDeleted (soft delete), LikedByMemberIds (join table `PlaceLike`).
- **ItineraryItem**: TripId, PlaceId, Date (DateOnly), StartTime (TimeOnly, nullable), Note (≤500, nullable), ActualCost (nullable, ≥0).
- **Expense**: TripId, Title (1–120), Amount (>0), Currency (must equal trip currency in v1), PaidByMemberId, Date (DateOnly), Category (`Transport | Lodging | Food | Tickets | Other`), SplitType (`Equal | Custom`), Shares: list of (MemberId, ShareAmount).
- **TravelTimeCache**: FromPlaceId, ToPlaceId, Mode (`driving` only in v1), Minutes, Meters, Source (`osrm | haversine`), FetchedAt. Unique on (From, To, Mode).
- **ActivityLog**: TripId, MemberId, Action (enum), EntityType, EntityId, SummaryText, At. Append-only.

## 4. State machine — Place.Status

Allowed transitions only:

```
Idea → Shortlist          (any member likes it)
Shortlist → Idea          (all likes removed)
Shortlist → Confirmed     (automatic: ALL current trip members have liked it)
                          (manual: any member force-confirms; log as ForceConfirmed)
Confirmed → Shortlist     (any member un-confirms)
Confirmed → Visited       (only when Trip.Status == Ongoing or Completed)
Confirmed → Skipped       (only when Trip.Status == Ongoing or Completed; SkipReason optional)
Visited ↔ Skipped         (correction allowed)
```

Any other transition → HTTP 409 with ProblemDetails code `INVALID_STATUS_TRANSITION`. Likes are idempotent (liking twice = no-op). When a member is added to the trip, existing `Confirmed` places stay confirmed (do NOT retro-demote).

## 5. Core business rules

### 5.1 Suggestions (`GET /trips/{id}/suggestions?date=`)
Return places where: Status == Confirmed, not soft-deleted, and NOT already scheduled on that date. Group by TimeSlot (a place with multiple slots appears in each matching group). Within each group, order: (a) places whose Category differs from categories already scheduled in that slot on that date first, (b) then by EstimatedCost ascending, nulls last. If `date` is outside the trip range → 422 `DATE_OUT_OF_RANGE`.

### 5.2 Feasibility (`GET /trips/{id}/itinerary/feasibility?date=`)
For the given date, take items ordered by StartTime; **items with null StartTime are excluded from pairing but reported as `info: UNSCHEDULED_TIME`**. For each consecutive pair (A, B):
- `gap = B.StartTime − (A.StartTime + A.Place.EstimatedDurationMinutes)`
- `travel = TravelTime(A.Place, B.Place)` (see 5.4)
- `gap < 0` → error `OVERLAP`
- `0 ≤ gap < travel` → error `INSUFFICIENT_TRAVEL_TIME` (include gap, travel, source)
- `gap > travel + 90min` → info `IDLE_GAP`
Additionally, per item: if item's StartTime does not fall in any of its Place.TimeSlots (Morning 05:00–10:59, Noon 11:00–13:59, Afternoon 14:00–17:59, Evening 18:00–23:59; times 00:00–04:59 match Evening) → warning `TIMESLOT_MISMATCH`. Feasibility NEVER blocks writes; it is a pure read endpoint returning `{ items: [{itineraryItemId, level: error|warning|info, code, data}] }`.

### 5.3 Expenses & settlement
- `Equal`: server computes shares = Amount / memberCount, rounded to whole VND (currency exponent 0); distribute the rounding remainder (±1 per unit) to the payer's share so that Σshares == Amount exactly.
- `Custom`: client sends shares; server validates ΣShareAmount == Amount exactly (integer math, no floats — store money as `long` minor units) else 422 `SHARES_SUM_MISMATCH`. Every ShareAmount ≥ 0. PaidByMemberId and every share MemberId must belong to the trip.
- Balance (`GET /trips/{id}/balance`): per member, `paid − owed`. Also return minimal settlement transfers (greedy: largest debtor pays largest creditor) — for 2 members this collapses to one line.
- Money rule project-wide: **all amounts are `long` minor units; no `double`/`decimal` arithmetic on money in hot paths; formatting only at the edge.**

### 5.4 Travel time
- Primary: OSRM public server (`router.project-osrm.org`), driving profile, 3s timeout, 1 retry.
- On success: cache in TravelTimeCache; cache is invalidated when either place's Lat/Lng changes.
- On failure/timeout: fallback = haversine distance × 1.35 road factor ÷ 32 km/h average speed; mark `source: haversine`. Feasibility responses must surface the source so the UI can show "ước tính".
- Never call OSRM inside a DB transaction. Batch: feasibility for a day makes at most (n−1) lookups, cache-first.

### 5.5 Weather (`GET /trips/{id}/weather`)
Proxy Open-Meteo daily forecast for trip coordinates (centroid of confirmed places; if none, first place; if none, 20.85, 104.65 fallback is NOT allowed — return 204). Cache per trip for 3 hours. If upstream fails and stale cache exists → serve stale with `stale: true`. If no cache → 502 `WEATHER_UNAVAILABLE`.

### 5.6 Deletion rules
- Place soft-delete only. Deleting a place with itinerary items requires `?force=true`; without it → 409 `PLACE_IN_USE` listing affected dates. With force: hard-delete the itinerary items, soft-delete the place, one ActivityLog entry summarizing both.
- ItineraryItem and Expense: hard delete, logged.
- Trip delete: out of scope v1 (return 405).

### 5.7 Auth & authorization
- `POST /trips` → creates trip + Owner member; response sets an HttpOnly, SameSite=Lax cookie containing a signed token {memberId, tripId}.
- `POST /trips/join` with {inviteCode, displayName} → creates Member, same cookie. Rate limit: 10 join attempts per IP per minute (429 on excess). Invalid code → 404 (same response shape as not-found trip; do not leak existence).
- Every trip-scoped endpoint: token's tripId must equal route tripId AND memberId must exist in that trip, else 403. **Test cross-trip access explicitly (IDOR).**
- DisplayName unique within a trip (case-insensitive) → 409 `NAME_TAKEN`.

### 5.8 Realtime sync
- One SignalR group per trip (`trip:{id}`); joining requires the same cookie auth.
- Direction: server→client only. After every successful mutation, broadcast `{event, entityType, entityId, payload, byMemberId, at}`. Events: `PlaceChanged`, `PlaceDeleted`, `ItineraryChanged`, `ExpenseChanged`, `TripChanged`, `MemberJoined`.
- Broadcast happens AFTER SaveChanges commits (no broadcast on failed tx).
- Concurrency model: last-write-wins. No rowversion. Client reconciles via broadcast payloads; on reconnect, client refetches the whole trip snapshot (`GET /trips/{id}/snapshot` returns everything in one call).

## 6. Constraints & validation (server-side, always)

- Trip: EndDate ≥ StartDate; span ≤ 60 days; Name 1–80.
- Changing Trip dates while itinerary items fall outside the new range → 409 `ITEMS_OUT_OF_RANGE` listing the item ids; client must move/delete them first (no silent cascade).
- ItineraryItem.Date must lie within trip range → 422.
- Same Place may appear multiple times in the trip but at most once per date → 409 `DUPLICATE_PLACE_ON_DATE`.
- All string inputs trimmed; reject strings that are whitespace-only where min length ≥ 1.
- Coordinates outside bounds → 422. Reject (0,0) as `SUSPICIOUS_COORDINATES` (almost always a client bug).
- All list endpoints exclude soft-deleted rows by default; `?includeDeleted=true` allowed only on places, marked in payload.
- Error contract: RFC 7807 ProblemDetails everywhere, with stable machine-readable `code` in extensions. No stack traces to clients.

## 7. Known edge cases — implement and test each

1. Feasibility on a day where 0 or 1 items have StartTime → no pair checks, no crash.
2. Two itinerary items with identical StartTime → treat as OVERLAP (gap < 0 rule with duration > 0), deterministic ordering by CreatedAt for pairing.
3. Item scheduled 23:30 with 90-minute duration (crosses midnight) → duration clamps at 23:59 for gap math in v1; emit info `CROSSES_MIDNIGHT`.
4. Place coordinates edited after itinerary scheduled → invalidate its TravelTimeCache rows (both directions) in the same transaction.
5. OSRM returns 200 but no route (unroutable pair) → treat as fallback haversine, source `haversine`.
6. Expense Equal split with memberCount that doesn't divide Amount (e.g., 100,001 VND / 2) → payer absorbs remainder; Σ == Amount asserted in a unit test.
7. Member joins mid-planning → existing Equal expenses are NOT recomputed (shares are frozen at creation); document this in the UI copy.
8. Two clients drag the same itinerary item concurrently → LWW; both receive the final broadcast; no 500s, no duplicate rows.
9. SQLite `SQLITE_BUSY` under concurrent writes → connection string uses `BusyTimeout=5000`; WAL enabled at startup via `PRAGMA`; integration test hammers 20 parallel writes without failure.
10. Trip timezone vs server timezone: an itinerary date must never shift when serialized (use DateOnly/TimeOnly end-to-end; never `DateTime` for calendar concepts). Test with server TZ set to UTC and to `Pacific/Auckland`.
11. Invite code collision on generation → regenerate (loop with cap 5, then 500).
12. Weather requested for a Completed trip in the past → return 204 (no forecast), not an upstream call.
13. Deleted place still referenced by TravelTimeCache → cache rows are deleted with the place (soft-delete of place still hard-deletes its cache rows).
14. JSON payload with unknown fields → ignored (do not 400); missing required fields → 422 with per-field errors.
15. Frontend: dnd drop onto a full/invalid target (different trip day column while feasibility panel open) must roll back optimistic state on API failure.

## 8. Code rules

- `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
- Structure: `Domain/` (entities, state machine, pure logic — no EF, no HTTP), `Infrastructure/` (EF, OSRM/weather clients), `Api/` (endpoint definitions, DTOs, validation), `Realtime/` (hub + broadcaster). Domain logic must be callable and testable without a database.
- Endpoints are thin: parse → validate → call service → map to DTO. No LINQ-to-entities in endpoint bodies.
- DTOs are separate from entities; never serialize EF entities.
- Feasibility, settlement, suggestion-ordering, and state-machine logic are **pure functions** over in-memory inputs.
- No background jobs, no MediatR, no AutoMapper, no repositories-over-EF. Keep it boring.
- Frontend: strict TS, no `any` (lint error), API types in one generated/hand-maintained `api-types.ts` mirroring DTOs, all server state via TanStack Query, business rules never re-implemented client-side except optimistic-UI mirrors clearly marked `// mirror of server rule`.
- Every mutation endpoint writes ActivityLog in the same transaction.

## 9. Test rules

- **Unit (must, pure)**: state-machine transition matrix (every pair, allowed + rejected); feasibility scenarios incl. edge cases 1,2,3,5; Equal-split rounding property test (∀ amount, memberCount ≤ 10: Σshares == amount, |share_i − share_j| ≤ 1); settlement correctness; suggestion ordering.
- **Integration (must)**: auth/IDOR (member of trip A hits trip B → 403 for every trip-scoped route — write this as a single parameterized test over the route table); join rate limiting; place force-delete cascade; trip date change conflict; SQLite concurrency (case 9); snapshot endpoint completeness; SignalR broadcast received after mutation (TestServer + HubConnection).
- **Frontend (must)**: settlement display, feasibility badge rendering, optimistic drag rollback. Skip trivial render tests.
- Coverage gates: Domain ≥ 90% line, Api ≥ 70%. CI = `dotnet build` (warnings as errors) + `dotnet test` + `npm run lint` + `npm run test` + `tsc --noEmit`. All green before any milestone is "done".
- Tests assert behavior, not implementation: no asserting that a mock was called as the sole assertion.

## 10. Milestones (each independently shippable)

1. Trip/Member/auth + Place CRUD + map display.
2. Place state machine + likes + wishlist UI.
3. Itinerary CRUD + dnd + suggestions endpoint.
4. Feasibility + OSRM + cache + route polyline on map.
5. Expenses + balance + settlement.
6. SignalR sync + snapshot + reconnect.
7. Weather + polish (ActivityLog UI, PWA manifest).

After completing each milestone, STOP and hand the diff to the Adversarial Reviewer (separate prompt). Do not start the next milestone until all Blocker/Major findings are resolved.
