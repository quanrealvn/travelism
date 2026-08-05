# DECISIONS

Choices made where `SPEC_PROMPT.md` is silent, plus the two places the spec asks
for something the fixed stack cannot express literally. Each entry says what was
decided and why, so the reviewer can arbitrate rather than guess.

Numbering is stable; entries are appended, never renumbered.

---

## Structure & tooling

### D1 — Domain / Infrastructure / Api are separate assemblies
Spec §8 names them as directories. They are separate projects instead, so
"Domain has no EF and no HTTP" is enforced by the compiler (Domain references
nothing) rather than by convention, and so the §9 per-project coverage gates
have something concrete to measure. `Realtime/` will be a folder inside
`WeGo.Api` when milestone 6 lands.

### D2 — EF Core migrations, not `EnsureCreated`
The schema grows every milestone; `EnsureCreated` cannot alter an existing
database, so an upgrade would silently mean "delete the file". Migrations are
applied at startup.

### D3 — Only milestone-1 entities exist so far
`Trip`, `Member`, `Place`, `PlaceLike`, `ItineraryItem`, `TravelTimeCache` and
`ActivityLog` are modelled now. `ItineraryItem` and `TravelTimeCache` are
included despite having no endpoints yet because the §5.6 / §7.13 delete rules
are milestone-1 scope and cannot be implemented — or tested — without them.
`Expense` is deferred to milestone 5, where it is first needed.

### D4 — Routes have no `/api` prefix
Spec §5 writes them as `/trips/{id}/...`, so that is what is served. The SPA
fallback is registered after explicit `/trips/**` and `/session/**` fallbacks
that return the JSON error contract, so a mistyped API path answers 404
ProblemDetails instead of the HTML shell.

---

## Field rules the spec does not bound

### D5 — `Trip.Destination` max length 120
Spec §3 gives no bound. 120 matches `Place.Name`.

### D6 — `Place.SkipReason` max length 300
Spec §3 gives no bound. Long enough for a real sentence, short enough to index.

### D7 — "Span ≤ 60 days" means the inclusive day count
Read as *a trip may be at most 60 days long*, so 1 March → 29 April (60 days) is
accepted and 1 March → 30 April (61 days) is rejected. The other reading
(`EndDate − StartDate ≤ 60`) differs by exactly one day. Flagged here because it
is a genuine ambiguity in the wording. `TripRulesTests` pins the current reading.

### D8 — A new place starts as `Idea`
Spec §4 defines the transitions but not the initial state. `Idea` is the only
one consistent with "any member likes it → Shortlist".

### D9 — `Trip.Currency` is frozen after creation
Spec §5.3 requires every expense to be in the trip currency. Allowing the trip
currency to change would retroactively falsify stored expense amounts, so
`PATCH /trips/{id}` ignores currency. Revisit in milestone 5 if needed.

---

## Error contract

### D10 — 401 for no/invalid session, 403 for the wrong trip
Spec §5.7 says "else 403" about a token whose `tripId` does not match the route.
That case is 403 (`FORBIDDEN`), as specified. A request with no cookie at all, or
an unverifiable one, is unauthenticated rather than forbidden and answers 401
(`UNAUTHENTICATED`). Both are covered by the parameterised route-table test.

### D11 — Enums are parsed by the domain validators, not by System.Text.Json
`JsonStringEnumConverter` throws on an unknown value, which surfaces as a
framework 400 whose body does not carry a `code`. Enum-typed fields therefore
arrive as `string` and are parsed in `EnumInput`, producing an ordinary
per-field 422. Numeric strings such as `"9"` are rejected too — `Enum.TryParse`
would accept them and store an undefined member.

### D12 — A body that cannot be parsed is 400 `MALFORMED_JSON`
Spec §6 covers missing fields (422) and unknown fields (ignored) but not
syntactically invalid JSON or a wrongly-typed field. Those cannot produce
per-field errors because binding never completes. `RouteHandlerOptions
.ThrowOnBadRequest` is forced on so the failure reaches the exception handler
and leaves as ProblemDetails rather than as the framework's bare 400.

### D13 — `SUSPICIOUS_COORDINATES` is a top-level code, not a field code
`ValidationResult.TopLevelCode` returns `SUSPICIOUS_COORDINATES` when any field
error carries that reason, and `VALIDATION_FAILED` otherwise, so the (0,0) rule
from §6 gets the distinct code the spec names while still reporting per field.

---

## Persistence

### D14 — `BusyTimeout=5000` is applied as `Default Timeout` + a PRAGMA
Spec §7.9 asks for `BusyTimeout=5000` in the connection string.
`Microsoft.Data.Sqlite` has no such keyword — passing it throws. The intent is
honoured two ways: `Default Timeout=5` on the connection string, and
`PRAGMA busy_timeout = 5000` on every connection open (`SqlitePragmaInterceptor`),
which is where the value actually takes effect. Configurable via
`Database:BusyTimeoutMs`.

### D15 — Timestamps are `DateTimeOffset` stored as ISO-8601 UTC text
SQLite has no native `DateTimeOffset`, and EF's default mapping **cannot be used
in `ORDER BY`** — the provider throws `NotSupportedException`. Every instant is
therefore converted to a fixed-width round-trip UTC string, which sorts
lexicographically in true chronological order. Normalising on write also makes
the §3 rule "all timestamps stored as UTC" hold at the one point every timestamp
passes through. `DateTime` is never used for anything.

### D16 — `PlaceLike` has a composite primary key
Spec §3 says all entities have a Guid `Id`; `PlaceLike` is a join row and uses
`(PlaceId, MemberId)` instead. This makes "liking twice is a no-op" (§4) an
invariant the database holds rather than a race the application has to win.

### D17 — Enums persist by name; `TimeSlots` persists as its bitmask
Storing by name means reordering an enum member in a later milestone cannot
silently reinterpret existing rows. `TimeSlots` is `[Flags]`, where a combined
value has no stable name, so it is stored as `int`.

### D18 — Soft delete is a model-level query filter
`Place` carries `HasQueryFilter(p => !p.IsDeleted)`, so §6's "list endpoints
exclude soft-deleted rows" cannot be broken by a future endpoint forgetting a
`Where`. The single opt-out allowed by the spec (`?includeDeleted=true` on
places) uses `IgnoreQueryFilters()`. `ItineraryItem` carries a matching filter
because `Place` is the required end of that relationship.

---

## Auth

### D19 — Session token format
`base64url(tripId:memberId) . base64url(HMACSHA256(payload))`, compared in fixed
time. The token proves identity only: membership is re-read from the database on
every trip-scoped request, so removing a member revokes access immediately
rather than at cookie expiry.

### D20 — The signing key is generated and cached on first run when unset
`Auth:SigningKey` (base64) always wins. With none configured, a 32-byte key is
generated and written to `.wego-signing-key` beside the app, so a restart does
not sign every member out. The file is gitignored. **Production deployments must
set `Auth:SigningKey` explicitly** — otherwise the key is per-machine and not
recoverable.

### D21 — Rate-limit partitioning falls back to a shared bucket
Joins are partitioned by `RemoteIpAddress`, populated from `X-Forwarded-For` via
`UseForwardedHeaders`. When there is no remote address (in-memory TestServer),
all callers share one `"unknown"` partition — this fails safe (shared limit)
rather than open (no limit). Because the whole test suite would then share that
bucket, `RateLimits:JoinPerMinute` is configurable and raised in tests other than
`JoinRateLimitTests`, which pins it to the spec's 10.

---

## Frontend

### D22 — `Patch<T>` distinguishes an absent field from an explicit null
Without it, PATCH cannot clear a nullable column, and a partial update risks
wiping fields the client never mentioned. `Patch<T>.IsSet` is false only when the
property was missing from the JSON object.

### D23 — Money is `number` holding integer minor units on the client
Mirrors the server's `long`. The only place it becomes a decimal is
`formatMoney`, on the way to the screen (§5.3: "formatting only at the edge").

### D25 — Place search is proxied through the backend, not called from the browser
**Scope note: this is an addition Quan asked for, not a spec requirement.**
Entering latitude and longitude by hand is unusable, so a place-name lookup was
added: `GET /trips/{tripId}/places/search?q=`.

It goes through the server rather than straight from the browser because
OpenStreetMap's Nominatim requires a `User-Agent` identifying the caller (which
a browser cannot set), limits callers to one request per second, and would
otherwise be reachable by anyone. Proxying lets the app honour the policy, cache
repeats, and keep the lookup behind the existing trip-membership filter so it is
not an open relay. It also matches the typed-`HttpClient` pattern §2 already
specifies for OSRM and Open-Meteo.

Results are biased towards the centroid of the trip's existing places
(`bounded=0`, so distant matches still appear but rank lower) — without it,
searching "quán ăn" matches the whole planet.

Search is a convenience, never a dependency: the form keeps a manual
coordinate entry path, and an upstream outage answers 502
`GEOCODING_UNAVAILABLE` while leaving place creation entirely unaffected. Both
are covered by tests.

### D38 — Map pins are coloured by category and carry their name
Identical pins stop being useful past about three places. Each marker is now a
Leaflet `DivIcon` rendering a coloured dot plus the place name.

**Category, not status,** is the colour axis. On a map the useful question is
"where is the food, where are the sights" — status is already the organising
idea of the wishlist, and using it here too would put two colour scales in
competition for the same pixels. Status shows as opacity instead: a confirmed
stop reads solid, an unbacked idea recedes without disappearing.

Every category carries a **glyph as well as a colour**. Roughly 1 in 12 men
cannot reliably separate the red and green pins, and a map that encodes meaning
only in hue is unreadable for them. A legend sits in the corner.

`DivIcon` takes a **raw HTML string**, which makes a place name untrusted markup
the moment it goes in one — names come from user input and from the geocoder.
`escapeHtml` guards that boundary and is tested with an `<img onerror>` payload.

### D39 — A place carries a description and reference links
**Quan's addition; the spec's §3 Place has neither.** A wishlist that records
only *where* a place is loses the reason it was saved. `Description` (≤2000
chars) and up to 10 `PlaceReference` links cover that.

Links are validated against a **scheme allowlist**, not a blocklist. They are
rendered as anchors, so `javascript:` and `data:` URLs execute on click — and
the set of dangerous schemes is open-ended while the set we want is exactly two.
A rejected link fails the whole write rather than being silently dropped, so
nobody believes they saved something they did not. Blank rows *are* dropped:
an empty row in a form is somebody who changed their mind.

References are **replaced wholesale** on PATCH rather than patched per link.
Per-link patching would need stable ids on the client for no real gain, and the
editor works on the list as a whole anyway. Omitting the field leaves them
untouched, per the usual `Patch<T>` semantics (D22).

### D40 — Reference rows are written through the DbSet, never the navigation collection
Replacing links by mutating `place.References` throws
`DbUpdateConcurrencyException`: EF's fixup both severs the relationship and
cascade-deletes the orphan, issuing two DELETEs for one row, and the second
affects nothing. `RemoveRange` on `db.PlaceReferences` plus `Add` for the new
rows avoids the fixup entirely. The response is then re-read, because the
tracked navigation still holds the rows that were just deleted.

### D27 — Leaflet's *default* marker images must be `import`ed, not built with `new URL(...)`
*(Superseded in effect by D38, which replaced the default pins entirely — kept
because the trap is worth remembering if an image-based icon ever returns.)*
`new URL('leaflet/dist/images/marker-icon.png', import.meta.url)` looks like the
idiomatic Vite asset reference and silently is not: Vite only rewrites that form
for **relative** paths, so a bare package specifier is left alone, the images are
never emitted, and the URLs 404 at runtime. Every map pin was invisible, with
nothing in the build output or the console to say why. Real `import` statements
make Vite emit (here, inline) the three files. `TripMap` carries a comment so the
"tidier" form is not reintroduced.

### D28 — Clicking the map picks a location
OpenStreetMap does not contain every place in Vietnam — a new homestay or a
roadside quán may simply not be there, and no geocoder can find what is not in
the data. Clicking the map sets the coordinates directly, so a place is always
addable regardless of search coverage. The form and the map share one draft
location, so picking on either shows on the other.

### D29 — The geocoder is sent the query exactly as typed
An earlier revision folded Vietnamese diacritics to ASCII before querying
Nominatim, on the evidence that "Thác Dải Yếm" returned nothing while
"thac dai yem" worked. **That evidence was false.** The queries were being sent
from Git Bash's curl, which transcoded the arguments to the Windows ANSI
codepage — `ộ` arrived as a literal `?` and `â` as the single byte `0xE2`. The
mangled query is what failed, not the accented one.

Re-tested with the bytes verified on the wire, Nominatim resolves
`Thác Dải Yếm`, `Đồi Chè Trái Tim` and `Rừng thông bản Áng` correctly and
returns results identical to the folded spellings. The folding was removed
rather than kept "just in case": it was a workaround for a bug that does not
exist, and leaving it would have meant shipping behaviour justified by a
measurement that was wrong.

What remains is `NominatimGeocoderTests`, which pins that a query reaches the
wire byte-for-byte as typed and is UTF-8 percent-encoded exactly once — the
regression that *would* have mattered.

### D30 — Search results are ranked by distance from the trip, and show it
Nominatim ranks by its own notion of importance, which for a Vietnamese place
name regularly puts another country first. Measured live: `Tiểu khu 32` returns
a street in Kaohsiung, `Hang Táu` one in Hong Kong. Both look entirely plausible
as a list of names — nothing in the result says it is 1,600 km away.

Results are therefore sorted by great-circle distance from the centroid of the
trip's existing places, and each carries `distanceKm`. The client shows the
distance and tags anything over 150 km as *xa chuyến đi*. For `Nông Trường` this
promotes the Mộc Châu one (4.6 km) above the four others elsewhere in Vietnam
(72–1,165 km).

`bounded=1` was considered and rejected: it does suppress the foreign matches,
but it also makes a genuinely distant stop — an airport, somewhere en route —
unfindable. Flagging preserves both. With no places on the trip yet there is
nothing to measure from, so the upstream order is passed through untouched.

The haversine lives in `Domain/Places/Geo.cs` because spec §5.4 needs the same
calculation for the travel-time fallback in milestone 4.

### D31 — What search cannot fix
OpenStreetMap does not contain every place in Vietnam, and no amount of query
tuning finds what is not in the data. Two real examples from testing:
`Tiểu khu 32 thị trấn Nông Trường` returns nothing anywhere in the world,
because "thị trấn Nông Trường" was renamed by the 2025 administrative reform
(OSM now carries `Phường Thảo Nguyên`) and `Tiểu khu 32` is not mapped as a
named place; and Mộc Châu's Hang Táu is simply absent.

So the empty state names the query that failed and points at the two things that
do work — a shorter name, or clicking the map — rather than saying "no results"
and leaving the user stuck. Click-to-place (D28) is the real answer here.

### D32 — A pasted map link is a first-class way to add a place
**Quan's decision, after OSM coverage proved insufficient (D31).**

`POST /trips/{tripId}/places/resolve-link` accepts a Google Maps link, an
OpenStreetMap link, or a bare coordinate pair, and returns a location. The
search box detects a pasted URL or coordinates and routes to this endpoint
instead of the geocoder, so it is one field rather than two.

This is deliberately not a Google integration. Nothing of Google's is queried,
cached or stored — the user searched on their side and handed over a URL, and
all we do is read the coordinates out of it. That sidesteps the API cost, the
key management, and the Maps Platform term that restricts showing Google place
content alongside a non-Google map. Coverage becomes "anything on Google Maps"
for no cost and no change to the §2 stack.

Parsing detail worth keeping: in a `/maps/place/` URL the `!3d…!4d…` pair is the
pin, while `@lat,lng` is only where the viewport happened to be. They differ by
tens of metres and sometimes far more, so the pin wins and the viewport is a
last resort.

### D33 — Only two hosts may ever be fetched
Expanding a `maps.app.goo.gl` short link means the server makes a request to a
URL the user supplied — textbook SSRF. Two guards, both tested:
`PlaceLink.TryGetExpandableUrl` admits only `maps.app.goo.gl` and `goo.gl`
(exact host match, so `maps.app.goo.gl.evil.com` fails), and automatic redirect
following is **off** so every hop is re-checked rather than chased blindly.
`localhost`, `127.0.0.1`, `169.254.169.254` and `file://` are covered by explicit
tests asserting no outbound call is made — including from an unauthenticated
caller, who cannot reach the endpoint at all.

### D34 — `SPEC_CONFLICT`: the Equal-split remainder is distributed per unit
**Two spec rules contradict each other; this is the arbitration, and Quan may
want to overrule it.**

- §5.3: "distribute the rounding remainder (±1 per unit) **to the payer's
  share** so that Σshares == Amount exactly."
- §9: the property test must assert "**|share_i − share_j| ≤ 1**".

For three or more members these cannot both hold. 101 ₫ split three ways gives
a base of 33 and a remainder of 2; handing the whole remainder to the payer
produces 35/33/33, a spread of 2, which §9 forbids. The property test failed on
exactly this.

Resolved in favour of §5.3's own parenthetical, "**±1 per unit**": the remainder
is handed out one minor unit at a time, starting with the payer. Both rules then
hold, and for the two-member case v1 actually targets the behaviour is
unchanged — §7.6's worked example still gives the payer 50,001 and the other
50,000.

The alternative reading (payer absorbs everything, drop §9's bound for n ≥ 3) is
equally defensible; it is just less fair and harder to explain to the person
holding the receipt.

### D35 — Only Equal splits are offered in the UI
The API supports `Custom` fully — validated for exact sum, non-negative shares,
and trip membership. The client only offers `Equal`, because a per-member share
editor is a real piece of UI and the two-person case does not need one yet. The
endpoint is not gated on this, so a custom split can be posted directly.

### D36 — "Today" is resolved in the trip's timezone, never the browser's or the server's
Calendar values reach the server timezone-free: `<input type="date">` yields a
plain `YYYY-MM-DD`, and `DateOnly`/`TimeOnly` carry it end to end. But two rules
need to know what *today* is — §7.12 (no forecast for a past trip) and §5.5 (a
forecast starts from today, not from a trip's already-past start date).

That question is answered in `Trip.TimeZoneId`. A Mộc Châu trip ends when it is
over in Mộc Châu, whoever is looking and from where. Deriving it from the
browser would make the answer depend on which member opened the app; deriving it
from the server would make it depend on where the app happens to be hosted.
`WeatherTests` freezes the clock at a moment that is 2 March in UTC and already
3 March in `Asia/Bangkok`, and asserts a trip ending 2 March has no forecast.

The trip timezone is also passed to Open-Meteo, so the forecast's day boundaries
are the traveller's rather than UTC's.

### D37 — Realtime broadcasts invalidate rather than patch
A broadcast payload carries the changed entity, so the client *could* apply it
directly. It invalidates the affected queries instead, because the server owns
derived state — a place's status after a like, the entire balance after an
expense — and reapplying those rules client-side would be a second
implementation to keep in agreement with the first. The extra fetch is cheap;
a divergence between two copies of the settlement algorithm would not be.

A client ignores its own echoes, so the person who made a change does not watch
it flicker.

### D26 — `@testing-library/react` is pinned to v16 for a single `@testing-library/dom`
RTL 14 depends on its own nested `@testing-library/dom@9`, while
`user-event@14` resolves the hoisted `@testing-library/dom@10`. RTL installs the
act() event wrapper on *its* copy, so every interaction driven by `user-event`
updated React state outside `act` and logged a warning — 128 of them across a
fully passing suite, which is exactly the noise that hides a real warning. RTL 16
takes `@testing-library/dom` as a peer dependency, so there is one copy and one
wrapper. `@testing-library/dom` is therefore an explicit devDependency.

### D24 — One client-side rule is mirrored, and marked
`PlaceForm` checks "at least one time slot" before submitting, marked
`// mirror of server rule` per §8. It is a convenience only; the server rejects
the same input independently, and the integration tests assert that.
