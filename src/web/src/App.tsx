import { lazy, Suspense, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { ApiError } from './api/client'
import {
  queryKeys,
  useChangePlaceStatus,
  useCreatePlace,
  useBalance,
  useCreateExpense,
  useDeleteExpense,
  useDeletePlace,
  useDestinationCenter,
  useExpenses,
  useFeasibility,
  useItinerary,
  useMoveItem,
  usePlaces,
  useRemoveItem,
  useScheduleItem,
  useSession,
  useToggleLike,
  useTrip,
  useActivity,
  useForgetTrip,
  useMyTrips,
  useUpdatePlace,
  useUpdateTrip,
  useWeather,
} from './api/hooks'
import type {
  CreatePlaceRequest,
  IsoDate,
  PlaceReferenceRequest,
  PlaceStatus,
  TripSessionResponse,
  TripSummaryResponse,
} from './api/api-types'
import { StartScreen } from './components/StartScreen'
import type { LatLng } from './components/TripMap'
import { AddExpenseForm, ExpensePanel } from './components/ExpensePanel'
import { DayRail } from './components/DayRail'
import { todayIso, tripDays } from './itinerary/tripDates'
import { useTripSync } from './api/useTripSync'
import { PlaceForm } from './components/PlaceForm'
import { PlaceList } from './components/PlaceList'
import { Sheet } from './components/Sheet'
import { ConfirmDialog } from './components/ConfirmDialog'
import { PlaceFilter } from './components/PlaceFilter'
import { PlaceOverview } from './components/PlaceOverview'
import { applyPlaceFilter, EMPTY_FILTER } from './places/placeFilter'
import type { PlaceFilterState } from './places/placeFilter'
import { TripSheet } from './components/TripSheet'
import { TripList } from './components/TripList'
import { Spinner } from './components/Spinner'
import { useNarrowScreen } from './hooks/useNarrowScreen'
import { mostRelevantTrip } from './trips/defaultTrip'
import { TripTitle } from './components/TripTitle'
import { TripSwitcher } from './components/TripSwitcher'
import {
  IconCalendar,
  IconClose,
  IconCompass,
  IconInfo,
  IconPin,
  IconPlus,
  IconWallet,
} from './components/icons'

/*
 * Split out of the entry bundle. Leaflet and dnd-kit together are most of the
 * JavaScript this app ships, and on a slow connection every byte of them was
 * blocking the first paint for people who had not opened a map or a day yet.
 */
const TripMap = lazy(() =>
  import('./components/TripMap').then((m) => ({ default: m.TripMap })),
)
const ItineraryBoard = lazy(() =>
  import('./components/ItineraryBoard').then((m) => ({ default: m.ItineraryBoard })),
)

type View = 'wishlist' | 'itinerary' | 'money'

/**
 * The three things a trip is: where you might go, when you are going, and what
 * it costs. In that order, because that is the order they are decided in.
 *
 * "What everyone has been doing" used to be a fourth tab. It is a log, not a
 * destination — nobody opens an app to read one — so it moved into the trip
 * sheet, where it sits beside the other facts about the trip.
 */
const TABS: { id: View; label: string; Icon: (props: { className?: string }) => JSX.Element }[] = [
  { id: 'wishlist', label: 'Địa điểm', Icon: IconPin },
  { id: 'itinerary', label: 'Lịch trình', Icon: IconCalendar },
  { id: 'money', label: 'Chi tiêu', Icon: IconWallet },
]

const SYNC_TITLE = {
  live: 'Đang đồng bộ trực tiếp',
  connecting: 'Đang kết nối lại…',
  offline: 'Ngoại tuyến',
} as const

/** Turns the server's stable codes into something a traveller can act on. */
function describeItineraryError(error: unknown): string {
  if (!(error instanceof ApiError)) {
    return 'Không lưu được thay đổi.'
  }

  switch (error.code) {
    case 'DUPLICATE_PLACE_ON_DATE':
      return 'Địa điểm này đã có trong ngày đó rồi.'
    case 'DATE_OUT_OF_RANGE':
      return 'Ngày đó nằm ngoài chuyến đi.'
    default:
      return error.text
  }
}

/**
 * Which trip this browser last had open.
 *
 * Kept on the device rather than on the server: it is a preference of this
 * screen, not a fact about the trip, and two people sharing a plan should not
 * move each other around.
 */
const ACTIVE_TRIP_KEY = 'travelism.activeTripId'

function readActiveTripId(): string | null {
  try {
    return window.localStorage.getItem(ACTIVE_TRIP_KEY)
  } catch {
    // Private mode and blocked storage both throw. The app opens on the most
    // recent trip instead, which is the same thing a first visit does.
    return null
  }
}

function writeActiveTripId(tripId: string | null): void {
  try {
    if (tripId === null) {
      window.localStorage.removeItem(ACTIVE_TRIP_KEY)
    } else {
      window.localStorage.setItem(ACTIVE_TRIP_KEY, tripId)
    }
  } catch {
    // Nothing to recover: losing the preference costs one tap next time.
  }
}

export function App() {
  const queryClient = useQueryClient()
  const session = useSession()
  const [activeTripId, setActiveTripId] = useState<string | null>(readActiveTripId)
  const [showTrips, setShowTrips] = useState(false)

  // Only needed when there is a choice to make: which trip to open by default
  // is a question about dates, and one trip answers it by itself.
  const holdsSeveral = (session.data?.memberships?.length ?? 0) > 1
  const trips = useMyTrips(holdsSeveral)

  function handleReady(created: TripSessionResponse) {
    queryClient.setQueryData(queryKeys.session, {
      tripId: created.trip.id,
      memberId: created.session.memberId,
      memberships: [{ tripId: created.trip.id, memberId: created.session.memberId }],
    })
    queryClient.setQueryData(queryKeys.trip(created.trip.id), created.trip)
    void queryClient.invalidateQueries({ queryKey: queryKeys.session })
    void queryClient.invalidateQueries({ queryKey: queryKeys.myTrips })
    selectTrip(created.trip.id)
  }

  function selectTrip(tripId: string | null) {
    writeActiveTripId(tripId)
    setActiveTripId(tripId)
    setShowTrips(false)
  }

  if (session.isLoading) {
    return <Spinner block label="Đang tải…" />
  }

  if (!session.data) {
    return <StartScreen onReady={handleReady} />
  }

  const memberships = session.data.memberships ?? []

  if (memberships.length === 0) {
    return <StartScreen onReady={handleReady} />
  }

  // The remembered trip only counts if the browser still holds it — the cookie
  // is the authority, and a trip forgotten on another tab must not resurrect.
  const remembered = memberships.find((m) => m.tripId === activeTripId)

  // Waiting rather than opening the wrong trip and correcting: a workspace that
  // swaps out from under somebody a second after it appears is worse than a
  // spinner, and this only happens on a cold start with several trips.
  if (!remembered && holdsSeveral && trips.isLoading) {
    return <Spinner block label="Đang tải chuyến đi…" />
  }

  const preferred = remembered
    ? remembered.tripId
    : (mostRelevantTrip(trips.data ?? [], todayIso()) ?? memberships[0]!.tripId)

  const current = memberships.find((m) => m.tripId === preferred) ?? memberships[0]!

  if (showTrips) {
    return (
      <TripsScreen
        activeTripId={current.tripId}
        onOpen={selectTrip}
        onReady={handleReady}
        onClose={() => setShowTrips(false)}
      />
    )
  }

  return (
    <TripWorkspace
      // Remounts on a trip change, so no view state — selected place, open day,
      // draft location — survives into a different trip.
      key={current.tripId}
      tripId={current.tripId}
      memberId={current.memberId}
      tripCount={memberships.length}
      onBrowseTrips={() => setShowTrips(true)}
      onOpenTrip={selectTrip}
      onTripCreated={handleReady}
    />
  )
}


/**
 * The invite code for a trip that is about to be forgotten.
 *
 * Fetched on demand rather than carried in the trip summary: a browser holds up
 * to twenty of those, and the code that lets somebody into a trip has no
 * business being sent for nineteen trips nobody is looking at.
 */
function InviteCodeForTrip({ tripId }: { tripId: string }) {
  const trip = useTrip(tripId)

  if (trip.isLoading) {
    return (
      <p className="search-hint inline-busy" role="status">
        <Spinner />
        Đang lấy mã mời…
      </p>
    )
  }

  if (!trip.data) {
    return <p className="search-hint">Không lấy được mã mời.</p>
  }

  return <code className="invite-code">{trip.data.inviteCode}</code>
}

/**
 * The trips this browser holds, and the way to start another.
 *
 * Its own screen rather than a dropdown: on a phone a list of trips with dates
 * and countdowns does not fit in a menu, and this is also where a returning
 * traveller lands when they want last year's plan rather than next week's.
 */
function TripsScreen({
  activeTripId,
  onOpen,
  onReady,
  onClose,
}: {
  activeTripId: string
  onOpen: (tripId: string) => void
  onReady: (created: TripSessionResponse) => void
  onClose: () => void
}) {
  const trips = useMyTrips()
  const forget = useForgetTrip()
  const [adding, setAdding] = useState(false)
  const [pendingForget, setPendingForget] = useState<TripSummaryResponse | null>(null)

  return (
    <div className="app">
      <header className="topbar">
        {/* flex:1 so the close control goes to the far edge, as it does on the
            workspace header — on desktop it was stranded mid-bar beside the
            title, breaking its own convention. */}
        <div className="topbar-main" style={{ flex: 1 }}>
          <h1 className="topbar-title">Chuyến đi của bạn</h1>
          <p className="topbar-sub">{trips.data?.length ?? 0} chuyến trên thiết bị này</p>
        </div>
        <div className="topbar-actions">
          <button type="button" className="icon-button" onClick={onClose} aria-label="Đóng">
            <IconClose />
          </button>
        </div>
      </header>

      <main className="content content-plain">
        {trips.isLoading ? (
          <Spinner block label="Đang tải chuyến đi…" />
        ) : (
          <TripList
            trips={trips.data ?? []}
            today={todayIso()}
            activeTripId={activeTripId}
            forgettingId={forget.isPending ? forget.variables : null}
            onOpen={onOpen}
            onForget={(tripId) =>
              setPendingForget(trips.data?.find((trip) => trip.id === tripId) ?? null)
            }
            onNew={() => setAdding(true)}
          />
        )}
      </main>

      {pendingForget && (
        <ConfirmDialog
          title="Bỏ khỏi thiết bị này"
          confirmLabel="Bỏ khỏi thiết bị"
          destructive
          pending={forget.isPending}
          onCancel={() => setPendingForget(null)}
          onConfirm={() => {
            forget.mutate(pendingForget.id)
            setPendingForget(null)
          }}
        >
          <p>
            <strong>{pendingForget.name}</strong> sẽ không còn hiện trên thiết bị này.
          </p>
          <p className="confirm-detail">
            Chuyến đi không bị xoá — những người khác vẫn thấy bình thường, và bạn quay
            lại được bằng mã mời. Ghi lại mã này nếu bạn muốn quay lại:
          </p>
          {/*
            The invite code is only ever shown inside a trip. Forgetting one you
            own without seeing it first made the trip unreachable forever, even
            though it was still sitting on the server.
          */}
          <InviteCodeForTrip tripId={pendingForget.id} />
        </ConfirmDialog>
      )}

      {adding && (
        <Sheet title="Chuyến đi mới" onClose={() => setAdding(false)}>
          <StartScreen
            onReady={(created) => {
              setAdding(false)
              onReady(created)
            }}
          />
        </Sheet>
      )}
    </div>
  )
}

function TripWorkspace({
  tripId,
  memberId,
  tripCount,
  onBrowseTrips,
  onOpenTrip,
  onTripCreated,
}: {
  tripId: string
  memberId: string
  tripCount: number
  onBrowseTrips: () => void
  onOpenTrip: (tripId: string) => void
  onTripCreated: (created: TripSessionResponse) => void
}) {
  const trip = useTrip(tripId)
  const renameTrip = useUpdateTrip(tripId)
  const places = usePlaces(tripId)
  const createPlace = useCreatePlace(tripId)
  const deletePlace = useDeletePlace(tripId)
  const toggleLike = useToggleLike(tripId, memberId)
  const changeStatus = useChangePlaceStatus(tripId)
  const updatePlace = useUpdatePlace(tripId)
  const itinerary = useItinerary(tripId)
  const scheduleItem = useScheduleItem(tripId)
  const moveItem = useMoveItem(tripId)
  const removeItem = useRemoveItem(tripId)

  const [selectedPlaceId, setSelectedPlaceId] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  // The location being composed, shared by the map and the form so a click on
  // one shows up on the other.
  const [draftLocation, setDraftLocation] = useState<LatLng | null>(null)
  const [view, setView] = useState<View>('wishlist')
  const [selectedDate, setSelectedDate] = useState<IsoDate | null>(null)
  // Below 1024px the map and the list share the screen one at a time; above it
  // they sit side by side and this is ignored.
  const [pane, setPane] = useState<'list' | 'map'>('list')
  const [filter, setFilter] = useState<PlaceFilterState>(EMPTY_FILTER)
  const narrow = useNarrowScreen()
  const [sheet, setSheet] = useState<
    'trip' | 'add-place' | 'add-expense' | 'new-trip' | null
  >(null)
  const [pendingForceDelete, setPendingForceDelete] = useState<{
    placeId: string
    name: string
    dayCount: number
  } | null>(null)

  // Computed before the early returns below, because these are hooks and hooks
  // cannot be called conditionally.
  const days = trip.data ? tripDays(trip.data.startDate, trip.data.endDate) : []
  const activeDate = selectedDate ?? days[0] ?? null
  const feasibility = useFeasibility(tripId, view === 'itinerary' ? activeDate : null)
  // Only asked while the trip is empty, so this costs one geocode on a brand
  // new trip and nothing at all afterwards.
  const destinationCenter = useDestinationCenter(
    tripId,
    trip.data?.destination ?? '',
    (places.data?.length ?? 0) === 0,
  )
  const expenses = useExpenses(tripId)
  const balance = useBalance(tripId)
  const createExpense = useCreateExpense(tripId)
  const deleteExpense = useDeleteExpense(tripId)
  const syncStatus = useTripSync(tripId, memberId)
  const weather = useWeather(tripId)
  // Only fetched when the trip sheet is actually open: the log is a reference,
  // not something anybody watches, and it is the largest response in the app.
  const activity = useActivity(tripId, sheet === 'trip')

  if (trip.isLoading || places.isLoading || itinerary.isLoading) {
    return <Spinner block label="Đang tải chuyến đi…" />
  }

  if (trip.isError || !trip.data) {
    return (
      <p className="form-error" role="alert">
        Không tải được chuyến đi.
      </p>
    )
  }

  const currentTrip = trip.data
  const currentPlaces = places.data ?? []
  const itineraryItems = itinerary.data ?? []

  const confirmedPlaces = currentPlaces.filter((place) => place.status === 'Confirmed')

  // The list narrows; the map does not. A filter is a way of reading the list,
  // and hiding pins would quietly change what "the trip" looks like.
  const visiblePlaces = applyPlaceFilter(currentPlaces, filter, memberId)

  // The selected day's stops in visiting order, for the route line on the map.
  // Untimed items go last, matching how the day itself reads.
  const routePoints = itineraryItems
    .filter((item) => item.date === activeDate)
    .slice()
    .sort((a, b) => {
      if (a.startTime === null) return b.startTime === null ? 0 : 1
      if (b.startTime === null) return -1
      return a.startTime.localeCompare(b.startTime)
    })
    .map((item) => ({ lat: item.lat, lng: item.lng }))

  function handleSchedulePlace(placeId: string, date: IsoDate) {
    setActionError(null)
    scheduleItem.mutate(
      { placeId, date },
      { onError: (error) => setActionError(describeItineraryError(error)) },
    )
  }

  function handleMoveItem(itemId: string, toDate: IsoDate) {
    setActionError(null)
    moveItem.mutate(
      { itemId, body: { date: toDate } },
      { onError: (error) => setActionError(describeItineraryError(error)) },
    )
  }

  function handleSetTime(itemId: string, startTime: string | null) {
    setActionError(null)
    moveItem.mutate(
      { itemId, body: { startTime } },
      { onError: (error) => setActionError(describeItineraryError(error)) },
    )
  }

  /**
   * Picking a place opens it, and points the map at it.
   *
   * It used to swap the whole screen for the map on a phone, which threw away
   * your place in the list to show you a pin. The card expanding is the
   * feedback now, and the map is one more tap from inside it — the map itself
   * still moves either way, so it is already framed when you get there.
   */
  function handleSelectPlace(placeId: string) {
    setSelectedPlaceId((current) => (current === placeId ? null : placeId))
  }

  function handleCreate(body: CreatePlaceRequest) {
    // The sheet stays up on failure so the error lands next to the field that
    // caused it, rather than behind a dismissed form.
    createPlace.mutate(body, {
      onSuccess: () => {
        setSheet(null)
        setDraftLocation(null)
      },
    })
  }

  function handleChangeStatus(placeId: string, status: PlaceStatus) {
    setActionError(null)
    // A skip reason is optional (spec §4); prompting keeps it to one click when
    // the traveller has nothing to add.
    const skipReason =
      status === 'Skipped'
        ? (window.prompt('Vì sao bỏ qua? (có thể để trống)') ?? undefined)
        : undefined

    changeStatus.mutate(
      { placeId, status, skipReason: skipReason?.trim() === '' ? undefined : skipReason },
      {
        onError: (error) => {
          setActionError(
            error instanceof ApiError && error.code === 'INVALID_STATUS_TRANSITION'
              ? error.text
              : 'Không đổi được trạng thái.',
          )
        },
      },
    )
  }

  function handleSaveDetail(
    placeId: string,
    description: string | null,
    references: PlaceReferenceRequest[],
  ) {
    setActionError(null)
    updatePlace.mutate(
      { placeId, body: { description, references } },
      {
        onError: (error) => {
          setActionError(
            error instanceof ApiError
              ? (Object.values(error.fieldErrors())[0] ?? error.text)
              : 'Không lưu được mô tả.',
          )
        },
      },
    )
  }

  function handleDelete(placeId: string, force = false) {
    setActionError(null)
    deletePlace.mutate(
      { placeId, force },
      {
        onError: (error) => {
          // A place on the itinerary is refused until the caller says to take
          // it off the plan too. The server answers with the English
          // instruction "Re-send with ?force=true", which was being shown
          // verbatim to a Vietnamese user who had no way to act on it — the
          // capability existed and nothing in the UI offered it.
          if (error instanceof ApiError && error.code === 'PLACE_IN_USE') {
            const place = currentPlaces.find((candidate) => candidate.id === placeId)
            const dates = (error.problem as { dates?: string[] }).dates ?? []
            setPendingForceDelete({
              placeId,
              name: place?.name ?? 'Địa điểm này',
              dayCount: dates.length,
            })
            return
          }

          setActionError('Không xoá được địa điểm.')
        },
      },
    )
  }

  const createError =
    createPlace.error instanceof ApiError
      ? createPlace.error.text
      : createPlace.error
        ? 'Không thêm được địa điểm.'
        : null

  const fieldErrors =
    createPlace.error instanceof ApiError ? createPlace.error.fieldErrors() : {}

  const tabCounts: Record<View, number | null> = {
    wishlist: currentPlaces.length,
    itinerary: itineraryItems.length,
    money: expenses.data?.length ?? 0,
  }

  return (
    <div className="app">
      {/*
        `display: contents` on a phone, so the header and the tab bar lay out
        against the app shell exactly as they did — the bar on top, the pill
        floating at the bottom. From 1024px up this box becomes the sidebar
        that holds both. It carries no backdrop-filter and no transform, which
        is what lets the tab bar stay fixed to the viewport inside it.
      */}
      <div className="chrome">
        <header className="topbar">
        <div className="brand">
          <span className="brand-mark" aria-hidden="true">
            <IconCompass />
          </span>
          <span className="brand-word">Travelism</span>
        </div>

        <div className="topbar-main">
          {/*
            The name is edited where it is displayed. It used to be the trip
            switcher instead, which meant the one piece of text every trip gets
            wrong at creation was also the only heading you must not click.
          */}
          <TripTitle
            name={currentTrip.name}
            onRename={(name) => renameTrip.mutateAsync({ name })}
          />
          {/*
            The day count comes first: it is the most useful number here and
            a long destination used to push it off the end entirely.
          */}
          <p className="topbar-sub">
            {/*
              Live is the normal state and says nothing — a dot is enough.
              Anything else is worth a word: this was the one signal in the
              app carried by colour alone, on an 8px dot, with the label
              hidden and the explanation in a hover title nobody on a phone
              can reach.
            */}
            <span className={`sync sync-${syncStatus}`}>
              <span className={syncStatus === 'live' ? 'visually-hidden' : undefined}>
                {SYNC_TITLE[syncStatus]}
              </span>
            </span>
            <span className="topbar-sub-text">
              {days.length} ngày · {currentTrip.destination}
            </span>
          </p>
        </div>

        <div className="topbar-actions">
          {/*
            Labels are carried in the markup at every width and only hidden
            visually on a phone, so these keep their accessible names as icon
            buttons and grow into full rows in the sidebar.
          */}
          <TripSwitcher
            activeTripId={tripId}
            tripCount={tripCount}
            onOpenTrip={onOpenTrip}
            onNewTrip={() => setSheet('new-trip')}
            onSeeAll={onBrowseTrips}
          />

          <button
            type="button"
            className="nav-action"
            onClick={() => setSheet('trip')}
            title="Thông tin chuyến đi"
          >
            {/* The tile is its own element so it can be drawn as one in the
                rail and be nothing at all inside a phone's icon button. */}
            <span className="nav-action-icon">
              <IconInfo />
            </span>
            <span className="nav-action-label">
              <span className="nav-action-text">Thông tin &amp; mã mời</span>
            </span>
          </button>
        </div>
        </header>

      {/*
        A sibling of the header, never a child of it: the tab bar is fixed to
        the bottom of the viewport on a phone, and an ancestor carrying
        backdrop-filter would become its containing block and pin it to the
        header instead.
      */}
      <nav
        className="tabbar"
        role="tablist"
        aria-label="Khu vực"
        // A tablist is one stop with arrow keys inside it, not three stops.
        // Screen readers announce "tab, 1 of 3" and their users press arrows.
        onKeyDown={(event) => {
          const step = event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : 0
          if (step === 0) {
            return
          }

          event.preventDefault()
          const index = TABS.findIndex((tab) => tab.id === view)
          const next = TABS[(index + step + TABS.length) % TABS.length]!
          setView(next.id)
          event.currentTarget
            .querySelector<HTMLElement>(`[data-tab="${next.id}"]`)
            ?.focus()
        }}
      >
        {TABS.map((tab) => (
          <button
            key={tab.id}
            type="button"
            role="tab"
            className="tabbar-item"
            data-tab={tab.id}
            aria-selected={view === tab.id}
            tabIndex={view === tab.id ? 0 : -1}
            onClick={() => setView(tab.id)}
          >
            <span className="tabbar-icon">
              <tab.Icon />
            </span>
            <span className="tabbar-label">{tab.label}</span>
            {/* A sibling of the icon, not a child of it: on desktop the badge
                sits inline after the label, and nested inside a 1.125rem icon
                box it spilled out below as a stray number. */}
            {tabCounts[tab.id] !== null && tabCounts[tab.id]! > 0 && (
              <span className="tabbar-count">{tabCounts[tab.id]}</span>
            )}
          </button>
        ))}
        </nav>
      </div>

      <main className="content">
        {actionError && (
          <p className="form-error" role="alert">
            {actionError}
          </p>
        )}

        {view === 'wishlist' && (
          <>
            <div className="pane-switch" role="group" aria-label="Cách xem wishlist">
              <button type="button" aria-pressed={pane === 'list'} onClick={() => setPane('list')}>
                Danh sách
              </button>
              <button type="button" aria-pressed={pane === 'map'} onClick={() => setPane('map')}>
                Bản đồ
              </button>
            </div>

            <div className="wishlist-view" data-pane={pane}>
              <section className="map-panel">
                <Suspense fallback={<Spinner block label="Đang tải bản đồ…" />}>
                  <TripMap
                    places={currentPlaces}
                    currency={currentTrip.currency}
                    currencyExponent={currentTrip.currencyExponent}
                    selectedPlaceId={selectedPlaceId}
                    onSelectPlace={setSelectedPlaceId}
                    draftLocation={draftLocation}
                    onPickLocation={setDraftLocation}
                    routePoints={routePoints}
                    destinationCenter={destinationCenter.data ?? null}
                  />
                </Suspense>
              </section>

              <section className="list-panel">
                {/*
                  The floating button is hidden from 1024px up, where a thumb
                  is not what is reaching for it — so the wide layout needs its
                  own way in, or there is no way to add a place at all.
                */}
                <div className="panel-head">
                  <h2 className="section-title">Địa điểm ({currentPlaces.length})</h2>
                  <button
                    type="button"
                    className="button-primary panel-head-action"
                    onClick={() => setSheet('add-place')}
                  >
                    <IconPlus />
                    Thêm địa điểm
                  </button>
                </div>

                {currentPlaces.length > 0 && (
                  <PlaceOverview
                    places={currentPlaces}
                    myMemberId={memberId}
                    filter={filter}
                    onChange={setFilter}
                  />
                )}

                {/* Only worth the space once the list is long enough to lose
                    something in. */}
                {currentPlaces.length >= 8 && (
                  <PlaceFilter
                    value={filter}
                    matchCount={visiblePlaces.length}
                    totalCount={currentPlaces.length}
                    onChange={setFilter}
                  />
                )}

                <PlaceList
                  places={visiblePlaces}
                  members={currentTrip.members}
                  myMemberId={memberId}
                  currency={currentTrip.currency}
                  currencyExponent={currentTrip.currencyExponent}
                  selectedPlaceId={selectedPlaceId}
                  showsOnMap={narrow}
                  deletingPlaceId={deletePlace.isPending ? deletePlace.variables.placeId : null}
                  busyPlaceId={
                    toggleLike.isPending
                      ? toggleLike.variables.placeId
                      : changeStatus.isPending
                        ? changeStatus.variables.placeId
                        : updatePlace.isPending
                          ? updatePlace.variables.placeId
                          : null
                  }
                  tripUnderway={currentTrip.status !== 'Planning'}
                  onSelect={handleSelectPlace}
                  onShowOnMap={() => setPane("map")}
                  onDelete={handleDelete}
                  onToggleLike={(placeId, liked) => toggleLike.mutate({ placeId, liked })}
                  onChangeStatus={handleChangeStatus}
                  onSaveDetail={handleSaveDetail}
                />
              </section>
            </div>
          </>
        )}

        {view === 'itinerary' && (
          <div className="itinerary-body">
            <DayRail
              weather={weather.data}
              days={days}
              selectedDate={activeDate}
              onSelectDate={setSelectedDate}
            />

            <Suspense fallback={<Spinner block label="Đang tải lịch trình…" />}>
              <ItineraryBoard
                days={days}
                items={itineraryItems}
                confirmedPlaces={confirmedPlaces}
                currency={currentTrip.currency}
                currencyExponent={currentTrip.currencyExponent}
                movingItemId={moveItem.isPending ? moveItem.variables.itemId : null}
                findings={feasibility.data?.items ?? []}
                selectedDate={activeDate}
                onSelectDate={setSelectedDate}
                onMoveItem={handleMoveItem}
                onSchedulePlace={handleSchedulePlace}
                onRemoveItem={(itemId) => removeItem.mutate(itemId)}
                onSetTime={handleSetTime}
              />
            </Suspense>

          </div>
        )}

        {view === 'money' && (
          <div className="money-body">
            <div className="panel-head">
              <h2 className="section-title">Chi tiêu</h2>
              <button
                type="button"
                className="button-primary panel-head-action"
                onClick={() => setSheet('add-expense')}
              >
                <IconPlus />
                Thêm khoản chi
              </button>
            </div>

            <ExpensePanel
              expenses={expenses.data ?? []}
              balance={balance.data}
              members={currentTrip.members}
              myMemberId={memberId}
              currency={currentTrip.currency}
              currencyExponent={currentTrip.currencyExponent}
              deletingId={deleteExpense.isPending ? deleteExpense.variables : null}
              onDelete={(expenseId) => deleteExpense.mutate(expenseId)}
            />
          </div>
        )}

      </main>

      {view === 'wishlist' && (
        <button type="button" className="fab" onClick={() => setSheet('add-place')}>
          <IconPlus />
          Thêm địa điểm
        </button>
      )}

      {view === 'money' && (
        <button type="button" className="fab" onClick={() => setSheet('add-expense')}>
          <IconPlus />
          Thêm khoản chi
        </button>
      )}

      {sheet === 'trip' && (
        <TripSheet
          trip={currentTrip}
          myMemberId={memberId}
          syncStatus={syncStatus}
          activity={activity.data ?? []}
          activityLoading={activity.isLoading}
          onClose={() => setSheet(null)}
        />
      )}

      {sheet === 'add-place' && (
        <Sheet title="Thêm địa điểm" onClose={() => setSheet(null)}>
          <PlaceForm
            tripId={tripId}
            currencyExponent={currentTrip.currencyExponent}
            pending={createPlace.isPending}
            fieldErrors={fieldErrors}
            submitError={createError}
            onSubmit={handleCreate}
            mapPick={draftLocation}
            onLocationChange={setDraftLocation}
          />
        </Sheet>
      )}

      {pendingForceDelete && (
        <ConfirmDialog
          title="Xoá địa điểm"
          confirmLabel="Xoá cả khỏi lịch trình"
          destructive
          pending={deletePlace.isPending}
          onCancel={() => setPendingForceDelete(null)}
          onConfirm={() => {
            const { placeId } = pendingForceDelete
            setPendingForceDelete(null)
            handleDelete(placeId, true)
          }}
        >
          <p>
            <strong>{pendingForceDelete.name}</strong> đang nằm trong lịch trình{' '}
            {pendingForceDelete.dayCount} ngày.
          </p>
          <p className="confirm-detail">
            Xoá địa điểm sẽ bỏ luôn khỏi những ngày đó. Không thể hoàn tác.
          </p>
        </ConfirmDialog>
      )}

      {sheet === 'new-trip' && (
        <Sheet title="Chuyến đi mới" onClose={() => setSheet(null)}>
          <StartScreen
            onReady={(created) => {
              setSheet(null)
              onTripCreated(created)
            }}
          />
        </Sheet>
      )}

      {sheet === 'add-expense' && (
        <Sheet title="Thêm khoản chi" onClose={() => setSheet(null)}>
          <AddExpenseForm
            members={currentTrip.members}
            myMemberId={memberId}
            currency={currentTrip.currency}
            currencyExponent={currentTrip.currencyExponent}
            tripDays={days}
            pending={createExpense.isPending}
            submitError={
              createExpense.error instanceof ApiError
                ? createExpense.error.text
                : null
            }
            onAdd={(body) =>
              createExpense.mutate(body, { onSuccess: () => setSheet(null) })
            }
          />
        </Sheet>
      )}
    </div>
  )
}
