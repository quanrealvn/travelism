# WeGo — collaborative trip planner

A shared wishlist on a map, a drag-and-drop day itinerary, travel-time
feasibility checks, expense settlement and per-day weather — for two people now,
N later. First real trip: Mộc Châu, Vietnam. Nothing about the app is specific
to that trip.

The contract is [`SPEC_PROMPT.md`](SPEC_PROMPT.md). Choices it does not make are
recorded in [`DECISIONS.md`](DECISIONS.md).

**Milestone 1 is complete**: trips, members, cookie auth, place CRUD, map.

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

Current state:

| Suite                | Result |
| -------------------- | ------ |
| Domain unit tests    | 144 passing |
| API integration tests | 114 passing |
| Frontend (Vitest)    | 40 passing |
| Domain line coverage | 97.98% (gate 90%) |
| Api line coverage    | 92.52% (gate 70%) |

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
| `POST` | `/trips` | Creates trip + owner, sets the session cookie |
| `POST` | `/trips/join` | `{inviteCode, displayName}`; rate limited to 10/min per IP |
| `GET`  | `/session` | Who the current cookie belongs to |

Trip-scoped — all require a session cookie whose `tripId` matches the route:

| Method | Route |
| ------ | ----- |
| `GET` | `/trips/{tripId}` |
| `PATCH` | `/trips/{tripId}` |
| `DELETE` | `/trips/{tripId}` → 405, out of scope in v1 |
| `GET` | `/trips/{tripId}/members` |
| `GET` | `/trips/{tripId}/places?includeDeleted=` |
| `POST` | `/trips/{tripId}/places` |
| `GET` | `/trips/{tripId}/places/search?q=` → place-name lookup |
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
| `Geocoding:UserAgent` | `WeGo-TripPlanner/0.1 …` | Nominatim requires an identifying UA — **put a real contact in it before deploying** |
| `Geocoding:MinIntervalMs` | `1000` | Minimum gap between upstream lookups |
| `Geocoding:CacheMinutes` | `30` | How long identical searches are reused |

With `Auth:SigningKey` unset, a key is generated on first run and cached in
`.wego-signing-key` next to the app. That is fine for local development and not
fine for a deployment — see D20.

---

## Milestones

1. **Trip/Member/auth + Place CRUD + map** ← done
2. Place state machine + likes + wishlist UI
3. Itinerary CRUD + dnd + suggestions
4. Feasibility + OSRM + cache + route polyline
5. Expenses + balance + settlement
6. SignalR sync + snapshot + reconnect
7. Weather + polish

Each milestone stops for adversarial review
([`ADVERSARIAL_REVIEWER.md`](ADVERSARIAL_REVIEWER.md)) before the next begins.
