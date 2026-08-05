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

### D41 — The phone is the design target, and the tab bar is at the bottom
Every layout rule starts from a 390px screen and the two breakpoints only add
what a larger one can afford. Navigation sits at the bottom of the viewport
because that is where a thumb reaches; above 1024px it moves into a bar under
the title, where a pointer is what is actually being used.

The tab bar is a sibling of the header, never a child. `backdrop-filter` on the
header makes it the containing block for `position: fixed` descendants, so the
tab bar was being pinned to the bottom of a 60px header — landing invisibly on
top of the trip-info button and swallowing every click on it.

`env(safe-area-inset-*)` only resolves to a non-zero value with
`viewport-fit=cover` on the viewport meta, which the shell now sets.

### D42 — Nothing below 12px, nothing below 4.5:1, nothing smaller than 40px
The type scale has no step under 12px and the palette has no ink lighter than
`--c-ink-3`. This is a travel app: it is read outdoors, at arm's length, by
people who are walking.

The contrast figures in `tokens.css` are measured rather than estimated. An
earlier `--c-ink-3` was chosen by eye, annotated "4.6:1", and actually measured
3.71:1 — a fail for body text that had been written down as a pass.

44px is the target size for anything primary; 40px is the floor for dense
secondary controls, and the audit fails anything below it.

### D43 — Enum names are wire values, and `labels.ts` is the only place they become words
The API speaks the spec's enum names, which are English. The map legend was
already translating them while the card beside it was not, so a place read
"Tham quan" in one place and "Sight" in another. Every user-facing rendering of
a category, a time slot, or an expense type now goes through `api/labels.ts`;
an unknown value falls back to the wire name rather than to "Khác", because
showing what the server said is more honest than filing it under Other.

### D44 — Long forms live in a sheet behind a button, not on the page
The add-place and add-expense forms were permanently expanded, pushing the
wishlist and the balance — the things somebody opens those tabs to read — below
the fold. Both now open in a sheet from a floating action button, dismissed by
Escape, by the backdrop, or by saving. Focus moves in on open and returns to
whatever opened it.

The forms carry no heading of their own: the sheet is already titled, and
printing the title twice reads as a rendering fault.

### D45 — The design was verified by a harness, and the harness found the defects
`__ux.mjs` (temporary, deleted after the pass) drove a real browser across three
viewports and six surfaces — including inside the sheets, since that is where
the forms now live — checking horizontal overflow, tap-target size, text size,
contrast, clipping, accessible names, whether each control is actually the
element at its own centre, and whether the map has loaded enough tiles to cover
its box.

Five real defects came from it that review had not caught:

- Every `<input>` with no `type` attribute was unstyled and 28px tall. The base
  rule listed `input[type='text']` and friends, and an input with no type
  attribute is a text input that matches none of them. Now matched by exclusion.
- `.place-pin-dot` lost its `background` in the stylesheet rewrite, so every map
  pin rendered white while the legend beside it named five colours.
- Leaflet measures its container once. Below 1024px the map is built inside a
  `display: none` pane, measures zero, and paints one tile into the corner of a
  blank box. A `ResizeObserver` now re-measures and re-frames together.
- OpenStreetMap's attribution sits bottom-right, which is where the floating
  action button is. A map does not scroll, so the notice was permanently
  unreachable rather than briefly covered — it moved to bottom-left, and the
  app's chrome now clears Leaflet's z-index range, which runs to 1000.
- The itinerary's time field shared a row with the place name and dropped to a
  second row under a viewport media query. Desktop day columns are ~230px wide
  however large the window is, so the field overflowed and printed across the
  name. It is two rows at every width now.

Three of the harness's own findings were false and were fixed in the harness,
not the app: `color-mix()` resolves to `color(srgb …)` with 0–1 channels, which
read as near-black and reported every translucent surface as a contrast failure;
a gradient background cannot be sampled from `backgroundColor` at all, so the
balance card is skipped rather than guessed at; and a floating action button
covering a list item is the cost of the pattern, not a defect, so occlusion is
reported only when nothing can scroll it clear.

### D46 — `WeatherStrip` became `DayRail`
Below 1024px only the selected day's column is on screen, which makes that strip
the only way to reach another day. It returned `null` when there was no
forecast, so a weather outage — which spec §5.5 treats as ordinary, answering
204 for a trip with nowhere to forecast — would have stranded a phone on day
one. It renders whenever the trip has days; the forecast decorates it.

The name changed with the responsibility. A component called `WeatherStrip`
that must render without weather is a lie, and the test asserting it rendered
nothing was encoding the old contract, not protecting the new one.

### D47 — The session holds a set of trips, not one
A browser plans more than one trip: last October's and next month's. The cookie
carried a single `tripId:memberId`, so creating a second trip silently signed
the browser out of the first — the plan was still on the server, but nothing on
the device could reach it again.

The payload is now a comma-separated list of the same pairs, most recently used
first, capped at 20 so the cookie stays near 2KB. A single-membership payload is
the one-element case of the same format, so cookies issued before this change
still verify and still work; no migration.

The guarantees are unchanged and were re-tested as a set rather than as one:
the route's trip must be one the cookie holds, and the member row backing that
claim is re-read on every request. `/trips/mine` filters by the same rule, so
being removed from a trip takes it out of the switcher rather than leaving a row
that 403s.

`DELETE /session/trips/{id}` forgets a trip on one device. It is not leaving the
trip and not deleting it — the invite code still works and nobody else is
affected — and the UI says so on the control itself, because "Bỏ" next to a trip
somebody planned for months has to be unambiguous.

That endpoint names a trip but is not trip-scoped, so it is exempt from the IDOR
sweep by an explicit allowlist entry with its reason, rather than by loosening
the pattern that finds trip-scoped routes. A future endpoint taking a trip id
and forgetting to authorise it still fails that test.

### D48 — Which trip to open is a question about dates
The cookie orders memberships by when they were added, which is exactly the
wrong order for "which trip should this open". Adding last year's trip to keep
its wishlist would land you in it every time.

`mostRelevantTrip` picks the one underway (the one ending soonest, if two
overlap), else the next one starting, else the most recent one that has been.
A trip is still "upcoming" on its final day: you are on a trip on the morning it
ends. The device remembers the last trip opened, and that wins over the
calculation — but only while the cookie still holds it.

### D49 — Dark chrome, light canvas, one vivid accent
Following the structure of the Fly.io dashboard, which is what was asked for,
rather than any sampled value from it: the frame recedes to near-black, the
canvas stays light, and saturation is spent on the few things that carry
meaning.

The accent exists in two weights because one cannot serve both surfaces: the
deep violet clears 6:1 on white for text and buttons, and disappears on the dark
frame, where the bright one is used instead. Status colours reach the cards
they describe, so a card is identifiable as confirmed or merely an idea without
reading back up to its heading.

`.group-count` uses ink on the group's tint rather than the group's own colour:
coloured-on-tinted measured as low as 4.36:1 at 12px.

### D50 — The activity log is a reference, not a destination
It was a fourth tab. Nobody opens an app to read a log, and it was competing for
thumb space with the three things a trip actually is. It moved into the trip
sheet beside the other facts about the trip, and is now only fetched when that
sheet is open — it is the largest response the app makes.

### D51 — Selecting a place takes the map to it
Selecting used to only recolour a pin, which is invisible when the pin is
off-screen, so tapping a place in the list appeared to do nothing. The map now
flies to it — and below 1024px, where the list and the map are separate panes,
selecting brings the map into view first.

Framing the trip and flying to one place became one component. As separate
effects they fought: selecting flew the map, and then the resize that revealed
the pane refitted it to every place and undid the fly.

Both guard against a zero-sized map. Leaflet inside a `display: none` pane
measures zero, every derived coordinate is NaN, and `flyTo` throws "Invalid
LatLng object" — which took the whole render down until the audit caught it.

### D52 — The UX audit is a committed script
`npm run ux` drives the built app in a real browser across three viewports and
every surface, sheets included, and exits non-zero on a finding. It has now
caught, across two rounds, defects that neither review nor the unit suite did:

- a tab bar pinned to the header instead of the viewport, swallowing clicks on
  the button underneath it (`backdrop-filter` makes an ancestor the containing
  block for `position: fixed`)
- every `<input>` with no `type` attribute rendering unstyled at 28px
- map pins with no fill, beside a legend naming five colours
- a map painting one tile into the corner of a blank box
- OpenStreetMap's attribution permanently under the floating action button
- the floating button hidden from 1024px up, leaving **no way to add a place or
  an expense at all** on a desktop
- an SVG with no intrinsic size stretching a button into a 400px purple square
- `flyTo` throwing on a zero-sized map and taking the render with it

Three of its own findings were false and were fixed in the harness rather than
the app: `color-mix()` resolves to `color(srgb …)` with 0–1 channels that read
as near-black; a gradient cannot be sampled from `backgroundColor` at all; and a
floating button covering a list item is the cost of the pattern, not a defect —
so occlusion is only reported when nothing containing the element scrolls, which
includes a sheet's own body and not just the page.

### D53 — The activity log speaks Vietnamese too
The log stores a finished sentence at write time rather than a template, so its
wording is decided on the server. Those sentences were English — "Added expense
…", "Liked …" — sitting inside an otherwise Vietnamese app, in the one place
that reports what your travelling companions just did.

They are Vietnamese now, and each begins with the verb rather than the actor,
because the feed already prints the name above the line: the old strings that
embedded it read "Quân / Quân created trip …". Statuses inside those sentences
go through a `StatusLabel` on the server — the mirror of `labels.ts` on the
client, and the only place the server turns an enum into words.

Entries written before this stay as they were. Rewriting stored history to make
old rows match a new wording would be editing a log, which is the one thing a
log must not do.

### D54 — The review was right about the palette; the dark chrome was mine
An external review of the running app produced 20 findings and a screenshot of
the actual Fly.io dashboard. The dashboard is **light** — a pale violet-to-rose
wash behind white cards, frosted translucent chrome, tinted rounded tiles behind
icons, status pills pairing icon + label + count. The near-black frame in D49
was built from a description, not from the thing, and it was wrong.

The palette moved to that: gradient wash on `body` (fixed, so it stays put while
content scrolls), chrome at 88% white over a 22px blur, and the accent unchanged.
88% rather than 78% because at the lower value a card title behind the tab bar
stayed legible *as text* and competed with the tab labels — frosted chrome only
works when what is behind it becomes texture.

### D55 — Category icons are path data, not emoji
Emoji carry their own multi-colour artwork, so a 🍜 on a rose tile and a stroked
⛰ on a mint tile sat in identical tiles looking like two design systems — and
both were visually heavier than every stroke icon around them. Categories now
carry SVG path data in `placeMarkers.ts`, shared between React and the map's
DivIcon, drawn in the category's own colour at 1.75 stroke.

The card's left rail is the category tile now, not the like button. The like was
filled rose — which is this palette's *food* colour — so a waterfall and a tea
hill both wore a food-coloured tile, and the one scannable index down a long
list was spending the category palette on something else.

### D56 — What the audit cannot see
`npm run ux` passed with zero findings before and after the review that produced
these fixes. Everything below was invisible to it:

- A form that erased everything typed the moment the server rejected anything —
  including the searched location, so a duration complaint cost you the place.
- 20 seconds of white screen on a 400kbps connection, because `index.html` shipped
  an empty `<div id="root">` and 492KB of JavaScript had to execute first.
- Forty always-on map labels tiling into rows of white strips over the map.
- A place on the itinerary that could not be deleted, explained in English API
  instructions ("Re-send with ?force=true") to a Vietnamese user.
- One tap losing a trip with no confirmation, and a 21st trip silently evicting
  the two oldest — whose invite codes are only ever shown inside them.
- "12.5" parsed as 125 đồng and "0.4" as 4, silently.
- Neither sheet trapping focus: the pointer was blocked and the keyboard was not.
- A settlement — the answer the money tab exists for — rendered as the smallest,
  lowest-contrast line on the card, under a 32px number nobody needs.
- Coloured text on the violet gradient measuring 3.6:1 at one end and passing at
  the other. The audit skips gradients rather than guessing, so it said nothing.
- 820px having no layout at all: the mobile composition stretched, with the
  bottom 45% of the screen empty.

Two of the fixes were themselves caught by the audit on the next run — a text
link at 32px tall, and amber-on-amber at 4.36:1 — which is the arrangement
working as intended: the harness holds the measurable floor, and human review
finds what a floor cannot express.

### D57 — The wishlist shows one place at a time

Every card carried its description, its source link, its four action buttons and
its vote row whether or not anyone was looking at it. Six places filled 3946px:
you scrolled past four screens of buttons to see what was on the list.

Cards are now one row — icon, name, and a single meta line of duration, cost and
time slot — and the selected one expands in place to show everything it used to
show all the time. Same page, 2088px. The trade is that acting on a place costs
one tap first, which is the right trade: reading the list is the common act and
acting on a single place is the rare one.

### D58 — The overview strip is also the filter

The tally across the top ("2 đã chốt, 1 cân nhắc, 3 ý tưởng") answers how far
along the planning is without scrolling. Making those tiles the status filter as
well means the number and the way to act on it are one control instead of a
stat row above a chip row, both saying the same words.

"Chờ bạn" appears only when the count is above zero. A permanent "0 chờ bạn"
teaches you to stop reading the row.

### D59 — The tab bar floats on a phone and joins the app bar on a desktop

On a phone it is a frosted pill inset from the edges, sitting above the content
rather than walling off the bottom of the screen; the active tab fills violet and
puts its label beside its icon, so the current section is legible at a glance and
the other two stay compact.

Full-width, that same bar left a strip of empty chrome under the title. It is now
placed in the app's grid beside the title — title, trip switcher, and info on the
left, tabs right-aligned in the same row. One bar, no empty band.

### D60 — Switching trips is a button, not the first row of a list

The trip switcher opened by tapping the title, which is not a control anyone
expects to be a control, and choosing a trip meant reaching for the topmost row
of a sheet. There is now an explicit switch button beside the title, shown only
when the device is holding more than one trip, labelled with how many it is
holding.

### D61 — The trip name is renamed where it is displayed

A trip is named in the first thirty seconds of existing, before anyone has
picked dates or decided what the trip is, and `PATCH /trips/{id}` had accepted a
new name since milestone 1 — but nothing in the client ever called it. "Chuyến
đi mới" was permanent.

The heading is a button that becomes an input in place. Enter or blur commits,
Escape cancels, and a rejected save keeps the field open with what you typed
still in it. That last part is the whole point: a rename that swallows the new
name and shows the old one back is worse than no rename at all.

Escape is handled on keydown, which fires before the blur it causes — otherwise
the blur handler saved the edit that had just been cancelled.

### D62 — Desktop navigation is a sidebar

The tabs were right-aligned along the top. At 1440px that put ~750px of empty
chrome between the trip name and the first tab, and it put this app's three
primary sections exactly where every dashboard puts account and settings.
Neither is a spacing problem, so no amount of adjusting the gap was going to fix
it — the pattern was wrong.

A rail down the left is what this shape of product uses. It gives the trip's
identity, its switcher and its sections one column to share rather than one line
to compete for, and it leaves the content area free to be content. The phone
keeps the floating pill, which was never the problem.

The two chrome elements stay separate in the markup and are joined by a wrapper
that is `display: contents` below the breakpoint. A real box there at phone
widths would become the containing block for the tab bar's `position: fixed` and
pin it to the header — the same trap that `backdrop-filter` set earlier.

### D63 — The audit tests 320px

It ran at 390, 820 and 1440 — the widths this app was composed for, which is
exactly why they were the wrong ones to trust. 320 is the floor the industry
still designs to, and it is where a layout that merely works at 390 comes apart.

Adding it found no new failures, which is itself the finding: the overview strip
wraps to two rows and the cards hold. The value is that it cannot silently stop
being true.

### D64 — An empty trip's map looks at its own destination

`FALLBACK_CENTER` was a hardcoded Mộc Châu, so a Đà Lạt trip opened on a Sơn La
map and the first thing anybody did with a new trip was pan a thousand
kilometres. The destination was already a string on the trip; the geocoder the
add-place form uses can say where it is.

Asked only while the trip has no places — one lookup on a brand new trip and
none afterwards — and applied through `setView` rather than the `center` prop,
because `MapContainer` reads that once at mount and the answer arrives later.

### D65 — The whole shell is centred, not just the content

At full bleed on a wide monitor the rail sat against the left edge of the glass
with the content it labels a long way off. Capping the shell and centring it
lets the page show around the frame, which is what makes a dashboard read as a
composed page rather than a maximised window.

### D66 — The trip switcher is a menu

It was a button that replaced the entire screen with a trips page — a lot of
ceremony for "show me the other one", and it threw away where you were to do
it. A menu keeps the workspace behind it, and it puts starting a new trip in
the same place you go looking for an existing one, which is the moment you
discover you do not have it yet.

The full screen still exists behind "Xem tất cả": it carries dates, countdowns
and the way to forget a trip, and none of those belong in a menu.

The audit treats an open menu the way it already treated an open sheet — while
one is up, only it and its trigger are reachable. Without that, the menu
covering the row beneath it was reported as a control that could not be pressed,
which is the entire purpose of a menu.

### D67 — The rail is text and tiles, and the trip is a card

Every glyph sits in a bordered tinted tile that inverts to solid violet when
its row is current. That gives "where am I" a second signal beyond colour — the
tile is a filled shape or it is not — and it turns a column of line icons into a
set of objects rather than a list of links.

The trip itself is not one of the navigation items; it is what all of them are
about. It is drawn as a card lifted off the rail while everything below it is
flat text on the wash, so it reads as the heading of the column rather than its
first row.

`overflow-y: auto` on the rail computed `overflow-x` to auto as well, which put
a horizontal scrollbar under it as soon as the switcher's menu was wider than
the column. The rail's content is a fixed handful of rows that always fit, so it
does not scroll at all — and every label in it now truncates rather than pushing
the column wider than itself.

### D68 — The product is Travelism

Renamed everywhere the name is the product: the wordmark, the page title, the
web manifest, the icon's label, the npm package, the session cookie, the SQLite
file, the geocoder's User-Agent, and the top of the README.

The .NET assemblies, namespaces and directories are still `WeGo.*`, and every
path reference in the docs and scripts still points at them. That rename is 142
files of pure mechanics with nothing user-visible at the end of it, so it is
worth doing on its own rather than buried in a UI pass.

### D69 — A hover state has to be visibly less than the state beside it

Hovering a section in the rail filled its icon tile solid violet — which is
exactly how the *current* section is drawn. Running the pointer down the column
made each row look selected in turn, and while the pointer was anywhere in the
rail there was no way to tell where you actually were.

Hover deepens the tile now — stronger border, slightly heavier tint — and only
the current row fills it. The rule generalises: when a hover state borrows the
selected state's treatment, it does not read as "you could go here", it reads as
"you are here", and the real answer is destroyed.

### D70 — The map is furniture, so it is framed like furniture

A 2px accent border and a violet glow, at every width rather than only from
1024px up — below that it was full-bleed tiles butted against the wash and read
as an embed rather than part of the page. Two pixels and not one because a
hairline disappears wherever a road happens to run under it.

The corner cluster is new: "Xem toàn bộ" reframes the whole trip. Panning and
zooming were one-way before it — the map framed everything on arrival and then
had no way back, so losing your place meant reloading. Leaflet listens for
clicks natively on the container and React's `stopPropagation` does not reach
it, so the button needs `L.DomEvent.disableClickPropagation` or pressing it also
drops a draft pin underneath itself.

### D71 — Deployed as one machine, because the database is a file

SQLite on a Fly volume. A volume attaches to exactly one machine, so this app
runs as exactly one — never two. The deploy strategy is `immediate` rather than
the default, which wants a second machine alive beside the first and cannot give
it the volume. That costs a few seconds of downtime per deploy, which is the
correct trade for a single-writer database.

If it ever outgrows one machine the answer is Postgres, not more machines.

### D72 — Creating a trip needs a shared code; joining never does

Creating a trip is the only write an unauthenticated stranger can perform, and
the only one that consumes disk — everything else needs a session cookie or an
invite code. On a public host that is the single open door, so a shared code
closes it.

Deliberately only that door. Joining still needs nothing but the invite link,
which is the whole way a trip gets shared; gating that too would mean handing a
friend two secrets to look at one plan. An empty code means open, which is the
right default for anyone running this themselves.

`/config` reports whether a code is required and never what it is: the client
has to know whether to ask, and an access-code field on an open instance would
invent a barrier that is not there.

### D73 — Rate limits are per-IP, in three tiers

A global 600/minute backstop across every request including static files —
generous, because a café or a mobile carrier behind CGNAT presents dozens of
real people as one address, and this has to be a flood detector rather than a
quota. Then 5 trips/hour, because trip creation is the only anonymous path to
disk. Then 30 geocodes/minute, which is stricter than the load justifies:
Nominatim enforces its policy by banning the caller, so abuse there costs
everyone the feature rather than costing money.

The health check is exempt from all of it. A throttled health check reads as an
unhealthy app, and the platform answers that by restarting the machine — which
would turn a flood into an outage.

### D74 — Two production bugs the whole test suite could not see

Both found by driving the deployed site, and neither reachable from the 798
tests that pass on Windows.

**Alpine ships no IANA time zone database.** `TimeZoneInfo.FindSystemTimeZoneById`
threw for `Asia/Ho_Chi_Minh` — the default zone of every trip in this app — so
every single trip creation failed with a validation error blaming the client.
Windows and the Debian-based images carry their own copy, so nothing local could
reproduce it. Fixed with `apk add tzdata`.

**Forwarded headers were being ignored.** `KnownNetworks` and `KnownProxies`
default to loopback only, and a hosted reverse proxy never is. The middleware
was silently dropping them, and two things broke quietly: `Request.IsHttps`
stayed false behind TLS termination so the session cookie shipped without
`Secure`, and `RemoteIpAddress` was the proxy's, so every visitor on earth
shared one rate-limit partition and the per-IP limits protected nothing. The
limits were the more serious of the two — they looked correct in code, in tests,
and in the config, and were inert in production.

### D75 — The category picker is tiles, not a dropdown

A native `<select>` cannot carry an option's icon or its colour, so the one
screen where you say what a place *is* was the only screen in the app with none
of its category language on it — and on a phone it opened a full-height wheel to
choose between five things that fit on two rows. Both pickers on the form are
now tiles that take the option's own colour when selected, so the choice reads
as "this is the Food one" rather than merely "this one".

### D76 — Duration is asked in hours

The API stores minutes and nobody plans in them: "hai tiếng ở thác" is how a
duration gets decided. `hoursToMinutes` converts at the form edge and accepts a
comma as the decimal mark, because Vietnamese keyboards and Vietnamese habit
both produce "1,5" — rejecting that would refuse the most natural way to write
an hour and a half. It returns NaN rather than 0 for junk, so a place can never
be stored with no duration for the feasibility check to treat as free.

"Giờ mở cửa" became "Giờ có mặt dự kiến" on the same form. The field was always
free text, and what people wrote in it was when *they* meant to arrive.

### D77 — Distance between stops is straight-line, and says so

Each gap in a day carries how far apart its two stops are, in a row of its own
between the cards — the number belongs to the space between them, not to either
end. Computed on the client with Haversine: no request, no cache, instant.

Deliberately not road distance, which the server already knows how to ask a
routing service for and which feasibility uses. A number labelled "km" that is
40% short of the real road would be worse than no number, so it is written
"cách ~4,8 km" and never as a journey or a duration.

### D78 — The forecast tints the whole day chip

A row of grey cards with a different glyph on each made the weather something
you read one at a time. Each condition maps to a stable slug the stylesheet
colours on — amber for sun, blue for rain, violet for a storm — with every ink
chosen to clear AA on its own tint. The label never goes away, so colour is a
second channel rather than the only one, and "selected" outranks the forecast
because which day you are editing matters more than what the sky is doing.

### D79 — Local development is not rate limited

The production limits exist to stop a stranger filling the disk. Applied to a
machine running the app for one developer they only obstruct: the UX audit seeds
three trips per run and was refused on its second run of the hour, which reads
as a broken harness rather than a working limit. `appsettings.Development.json`
raises them; the deployed machine runs Production and is unaffected.

### D80 — The audit waits for tiles, not for a guess

Leaflet re-measures only once its pane is visible and only then asks for the
tiles covering the real size, so a fixed delay raced the tile server. Three runs
of the same layout reported 1, 2 and 7 tiles — the signature of a race, not a
defect. The audit now waits for the tile count itself, bounded, so a map that
genuinely never loads is still caught.
