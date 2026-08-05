# Travelism — collaborative trip planner

A shared wishlist on a map, a drag-and-drop day itinerary, travel-time
feasibility checks, expense settlement and per-day weather — for two people now,
N later. First real trip: Mộc Châu, Vietnam. Nothing about the app is specific
to that trip.

The contract is [`SPEC_PROMPT.md`](SPEC_PROMPT.md). Choices it does not make are
recorded in [`DECISIONS.md`](DECISIONS.md).

**All seven milestones are complete.**

---

## Prerequisites

- .NET SDK 8.0
- Node.js 20+

## Run it

Two terminals:

```bash
# API on http://localhost:5080 (creates and migrates wego.db on first run)
dotnet run --project src/WeGo.Api

# Vite dev server on http://localhost:5173, proxying /trips and /session to the API
cd src/web && npm install && npm run dev
```

Open <http://localhost:5173>.

### Single deployable

```bash
cd src/web && npm run build      # emits into src/WeGo.Api/wwwroot
dotnet run --project src/WeGo.Api
```

The API then serves the built SPA itself, on <http://localhost:5080>. One
process, one SQLite file, no Docker.

---

## Verify

```bash
dotnet build WeGo.sln                       # warnings are errors
dotnet test  WeGo.sln
pwsh scripts/check-coverage.ps1             # enforces the §9 gates

cd src/web
npm run lint && npm run typecheck && npm run test
```

And against a real browser, which is the only thing that catches a tab bar
sitting on top of the header or a map that loaded one tile:

```bash
cd src/web && npm run build           # serves from src/WeGo.Api/wwwroot
dotnet run --project src/WeGo.Api     # in another shell
cd src/web && npm run ux -- ./ux      # screenshots + audit; non-zero on failure
```

Current state:

| Suite                | Result |
| -------------------- | ------ |
| Domain unit tests    | 465 passing |
| API integration tests | 321 passing |
| Frontend (Vitest)    | 194 passing |
| Domain line coverage | 97.77% (gate 90%) |
| Api line coverage    | 95.80% (gate 70%) |

### Adding a place

Three ways, in order of how often they work for Vietnam:

1. **Paste a Google Maps link** into the search box — Share → copy link → paste.
   Short `maps.app.goo.gl` links are expanded server-side. Bare coordinates work too.
2. **Type a name** — searches OpenStreetMap, ranked by distance from your trip.
3. **Click the map** — for anywhere that is in no database at all.

---

## Layout

```
src/
  WeGo.Domain/          entities, validation rules, pure logic — no EF, no HTTP
  WeGo.Infrastructure/  EF Core + SQLite, migrations, connection PRAGMAs
  WeGo.Api/             minimal API endpoints, DTOs, auth, error contract
  web/                  React 18 + TypeScript + Vite + Leaflet
tests/
  WeGo.Domain.Tests/    pure unit tests
  WeGo.Api.Tests/       WebApplicationFactory integration tests
```

`WeGo.Domain` references nothing, so its "no database, no HTTP" rule is enforced
by the compiler rather than by discipline.

---

## API (milestone 1)

Public:

| Method | Route | Notes |
| ------ | ----- | ----- |
| `POST` | `/trips` | Creates trip + owner; **adds** it to the session cookie |
| `POST` | `/trips/join` | `{inviteCode, displayName}`; rate limited to 10/min per IP |
| `GET`  | `/session` | Every membership the cookie holds, most recent first |
| `GET`  | `/trips/mine` | Summaries of those trips, for the switcher |
| `DELETE` | `/session/trips/{tripId}` | Forgets a trip on this device — not a deletion |

A browser plans more than one trip, so the cookie holds a **set** of
memberships (up to 20) rather than one. A single-membership payload is the
one-element case of the same format, so cookies issued before this still work.

Trip-scoped — all require a session cookie that holds a membership for the trip
in the route:

| Method | Route |
| ------ | ----- |
| `GET` | `/trips/{tripId}` |
| `PATCH` | `/trips/{tripId}` |
| `DELETE` | `/trips/{tripId}` → 405, out of scope in v1 |
| `GET` | `/trips/{tripId}/members` |
| `GET` | `/trips/{tripId}/places?includeDeleted=` |
| `POST` | `/trips/{tripId}/places` |
| `GET` | `/trips/{tripId}/places/search?q=` → place-name lookup |
| `POST` | `/trips/{tripId}/places/resolve-link` → coordinates from a pasted map link |
| `GET` | `/trips/{tripId}/places/{placeId}` |
| `PATCH` | `/trips/{tripId}/places/{placeId}` |
| `DELETE` | `/trips/{tripId}/places/{placeId}?force=` |

Every error is RFC 7807 with a stable `code`:

```json
{
  "type": "https://httpstatuses.io/409",
  "title": "Conflict",
  "status": 409,
  "detail": "“Thác Dải Yếm” is scheduled on 2 day(s). Re-send with ?force=true …",
  "code": "PLACE_IN_USE",
  "dates": ["2026-03-02", "2026-03-05"]
}
```

---

## Configuration

`src/WeGo.Api/appsettings.json`, overridable by environment variables
(`Auth__SigningKey`, `Database__ConnectionString`, …):

| Key | Default | Purpose |
| --- | ------- | ------- |
| `Database:ConnectionString` | `Data Source=wego.db` | SQLite file |
| `Database:BusyTimeoutMs` | `5000` | Per-connection `PRAGMA busy_timeout` |
| `Database:EnableWal` | `true` | WAL, so readers do not block the writer |
| `Auth:SigningKey` | *(empty)* | Base64 HMAC key — **set this in production** |
| `RateLimits:JoinPerMinute` | `10` | Join attempts per IP per minute |
| `Geocoding:UserAgent` | `Travelism-TripPlanner/0.1 …` | Nominatim requires an identifying UA — **put a real contact in it before deploying** |
| `Geocoding:MinIntervalMs` | `1000` | Minimum gap between upstream lookups |
| `Geocoding:CacheMinutes` | `30` | How long identical searches are reused |

With `Auth:SigningKey` unset, a key is generated on first run and cached in
`.wego-signing-key` next to the app. That is fine for local development and not
fine for a deployment — see D20.

---

## Milestones — all complete

1. **Trip/Member/auth + Place CRUD + map** — cookie auth, IDOR sweep, soft delete
2. **Place state machine + likes + wishlist** — §4 matrix enumerated in tests
3. **Itinerary + drag-and-drop + suggestions** — optimistic move with rollback
4. **Feasibility + OSRM + cache + route line** — haversine fallback, source surfaced
5. **Expenses + balance + settlement** — integer minor units throughout
6. **SignalR sync + snapshot + reconnect** — broadcast only after commit
7. **Weather + activity feed + PWA** — trip-timezone "today", stale flagged

Each was built and then walked against
[`ADVERSARIAL_REVIEWER.md`](ADVERSARIAL_REVIEWER.md) before the next began.
Defects that pass caught, and the fixes, are recorded in the commit messages
and in [`DECISIONS.md`](DECISIONS.md).

## Things worth knowing

- **Money is never a float.** Amounts are `long` minor units end to end; the
  only conversion to a decimal is `formatMoney`, on the way to the screen.
- **Calendar dates never shift.** `DateOnly`/`TimeOnly` end to end, no
  `DateTime` anywhere, and "today" is resolved in the trip's timezone (D36).
- **Two spec conflicts were arbitrated**, both flagged for review: the
  Equal-split remainder rule (D34) and the trip-span reading (D7).
- **Place search has real limits.** OpenStreetMap does not contain every place
  in Vietnam; pasting a Google Maps link (D32) and clicking the map (D28) are
  the answers to that, not better queries.
