import { useState } from 'react'
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
  useExpenses,
  useFeasibility,
  useItinerary,
  useMoveItem,
  usePlaces,
  useRemoveItem,
  useScheduleItem,
  useSession,
  useSuggestions,
  useToggleLike,
  useTrip,
  useActivity,
  useUpdatePlace,
  useWeather,
} from './api/hooks'
import type {
  CreatePlaceRequest,
  IsoDate,
  PlaceReferenceRequest,
  PlaceStatus,
  TripSessionResponse,
} from './api/api-types'
import { StartScreen } from './components/StartScreen'
import { TripMap } from './components/TripMap'
import type { LatLng } from './components/TripMap'
import { ItineraryBoard } from './components/ItineraryBoard'
import { AddExpenseForm, ExpensePanel } from './components/ExpensePanel'
import { DayRail } from './components/DayRail'
import { ActivityFeed } from './components/ActivityFeed'
import { SuggestionsPanel } from './components/SuggestionsPanel'
import { tripDays } from './itinerary/tripDates'
import { useTripSync } from './api/useTripSync'
import { PlaceForm } from './components/PlaceForm'
import { PlaceList } from './components/PlaceList'
import { Sheet } from './components/Sheet'
import { TripSheet } from './components/TripSheet'
import { Spinner } from './components/Spinner'
import {
  IconCalendar,
  IconInfo,
  IconPin,
  IconPlus,
  IconPulse,
  IconWallet,
} from './components/icons'

type View = 'wishlist' | 'itinerary' | 'money' | 'activity'

/**
 * The four things a trip is: where you might go, when you are going, what it
 * costs, and what everyone has been doing. In that order, because that is the
 * order they are decided in.
 */
const TABS: { id: View; label: string; Icon: (props: { className?: string }) => JSX.Element }[] = [
  { id: 'wishlist', label: 'Wishlist', Icon: IconPin },
  { id: 'itinerary', label: 'Lịch trình', Icon: IconCalendar },
  { id: 'money', label: 'Chi tiêu', Icon: IconWallet },
  { id: 'activity', label: 'Hoạt động', Icon: IconPulse },
]

const SYNC_TITLE = {
  live: 'Đang đồng bộ trực tiếp',
  connecting: 'Đang kết nối lại',
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
      return error.message
  }
}

export function App() {
  const queryClient = useQueryClient()
  const session = useSession()

  function handleReady(created: TripSessionResponse) {
    queryClient.setQueryData(queryKeys.session, {
      tripId: created.trip.id,
      memberId: created.session.memberId,
    })
    queryClient.setQueryData(queryKeys.trip(created.trip.id), created.trip)
  }

  if (session.isLoading) {
    return <Spinner block label="Đang tải…" />
  }

  if (!session.data) {
    return <StartScreen onReady={handleReady} />
  }

  return <TripWorkspace tripId={session.data.tripId} memberId={session.data.memberId} />
}

function TripWorkspace({ tripId, memberId }: { tripId: string; memberId: string }) {
  const trip = useTrip(tripId)
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
  const [sheet, setSheet] = useState<'trip' | 'add-place' | 'add-expense' | null>(null)

  // Computed before the early returns below, because the suggestions query is a
  // hook and hooks cannot be called conditionally.
  const days = trip.data ? tripDays(trip.data.startDate, trip.data.endDate) : []
  const activeDate = selectedDate ?? days[0] ?? null
  const suggestions = useSuggestions(tripId, view === 'itinerary' ? activeDate : null)
  const feasibility = useFeasibility(tripId, view === 'itinerary' ? activeDate : null)
  const expenses = useExpenses(tripId)
  const balance = useBalance(tripId)
  const createExpense = useCreateExpense(tripId)
  const deleteExpense = useDeleteExpense(tripId)
  const syncStatus = useTripSync(tripId, memberId)
  const weather = useWeather(tripId)
  const activity = useActivity(tripId, view === 'activity')

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
              ? error.message
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
              ? (Object.values(error.fieldErrors())[0] ?? error.message)
              : 'Không lưu được mô tả.',
          )
        },
      },
    )
  }

  function handleDelete(placeId: string) {
    setActionError(null)
    deletePlace.mutate(
      { placeId },
      {
        onError: (error) => {
          // PLACE_IN_USE means the place is on the itinerary; milestone 3 adds
          // the confirm-and-force flow, so for now the reason is surfaced.
          setActionError(
            error instanceof ApiError && error.code === 'PLACE_IN_USE'
              ? `${error.message}`
              : 'Không xoá được địa điểm.',
          )
        },
      },
    )
  }

  const createError =
    createPlace.error instanceof ApiError
      ? (createPlace.error.problem.detail ?? createPlace.error.message)
      : createPlace.error
        ? 'Không thêm được địa điểm.'
        : null

  const fieldErrors =
    createPlace.error instanceof ApiError ? createPlace.error.fieldErrors() : {}

  const tabCounts: Record<View, number | null> = {
    wishlist: currentPlaces.length,
    itinerary: itineraryItems.length,
    money: expenses.data?.length ?? 0,
    activity: null,
  }

  return (
    <div className="app">
      <header className="topbar">
        <div className="topbar-main">
          <h1 className="topbar-title">{currentTrip.name}</h1>
          <p className="topbar-sub">
            <span className={`sync sync-${syncStatus}`} title={SYNC_TITLE[syncStatus]}>
              <span className="visually-hidden">{SYNC_TITLE[syncStatus]}</span>
            </span>
            {currentTrip.destination} · {days.length} ngày
          </p>
        </div>

        <div className="topbar-actions">
          <button
            type="button"
            className="icon-button"
            onClick={() => setSheet('trip')}
            aria-label="Thông tin chuyến đi và mã mời"
          >
            <IconInfo />
          </button>
        </div>

      </header>

      {/*
        A sibling of the header, never a child of it: the tab bar is fixed to
        the bottom of the viewport on a phone, and an ancestor carrying
        backdrop-filter would become its containing block and pin it to the
        header instead.
      */}
      <nav className="tabbar" role="tablist" aria-label="Khu vực">
        {TABS.map((tab) => (
          <button
            key={tab.id}
            type="button"
            role="tab"
            className="tabbar-item"
            aria-selected={view === tab.id}
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
                <TripMap
                  places={currentPlaces}
                  currency={currentTrip.currency}
                  currencyExponent={currentTrip.currencyExponent}
                  selectedPlaceId={selectedPlaceId}
                  onSelectPlace={setSelectedPlaceId}
                  draftLocation={draftLocation}
                  onPickLocation={setDraftLocation}
                  routePoints={routePoints}
                />
              </section>

              <section className="list-panel">
                <PlaceList
                  places={currentPlaces}
                  members={currentTrip.members}
                  myMemberId={memberId}
                  currency={currentTrip.currency}
                  currencyExponent={currentTrip.currencyExponent}
                  selectedPlaceId={selectedPlaceId}
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
                  onSelect={setSelectedPlaceId}
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

            <aside className="side-panel">
              <SuggestionsPanel
                date={activeDate}
                groups={suggestions.data ?? []}
                loading={suggestions.isFetching}
                currency={currentTrip.currency}
                currencyExponent={currentTrip.currencyExponent}
                onAdd={handleSchedulePlace}
              />
            </aside>
          </div>
        )}

        {view === 'money' && (
          <div className="money-body">
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

        {view === 'activity' && (
          <section className="side-panel">
            <h2 className="section-title">Hoạt động</h2>
            <ActivityFeed
              entries={activity.data ?? []}
              members={currentTrip.members}
              loading={activity.isLoading}
            />
          </section>
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

      {sheet === 'add-expense' && (
        <Sheet title="Thêm khoản chi" onClose={() => setSheet(null)}>
          <AddExpenseForm
            members={currentTrip.members}
            myMemberId={memberId}
            currencyExponent={currentTrip.currencyExponent}
            tripDays={days}
            pending={createExpense.isPending}
            submitError={
              createExpense.error instanceof ApiError
                ? (createExpense.error.problem.detail ?? createExpense.error.message)
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
