import { useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { ApiError } from './api/client'
import {
  queryKeys,
  useChangePlaceStatus,
  useCreatePlace,
  useDeletePlace,
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
} from './api/hooks'
import type {
  CreatePlaceRequest,
  IsoDate,
  PlaceStatus,
  TripSessionResponse,
} from './api/api-types'
import { StartScreen } from './components/StartScreen'
import { TripMap } from './components/TripMap'
import type { LatLng } from './components/TripMap'
import { ItineraryBoard } from './components/ItineraryBoard'
import { SuggestionsPanel } from './components/SuggestionsPanel'
import { tripDays } from './itinerary/tripDates'
import { PlaceForm } from './components/PlaceForm'
import { PlaceList } from './components/PlaceList'
import { formatMoney } from './api/money'

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
    return <p className="loading">Đang tải…</p>
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
  const itinerary = useItinerary(tripId)
  const scheduleItem = useScheduleItem(tripId)
  const moveItem = useMoveItem(tripId)
  const removeItem = useRemoveItem(tripId)

  const [selectedPlaceId, setSelectedPlaceId] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  // The location being composed, shared by the map and the form so a click on
  // one shows up on the other.
  const [draftLocation, setDraftLocation] = useState<LatLng | null>(null)
  const [view, setView] = useState<'wishlist' | 'itinerary'>('wishlist')
  const [selectedDate, setSelectedDate] = useState<IsoDate | null>(null)

  // Computed before the early returns below, because the suggestions query is a
  // hook and hooks cannot be called conditionally.
  const days = trip.data ? tripDays(trip.data.startDate, trip.data.endDate) : []
  const activeDate = selectedDate ?? days[0] ?? null
  const suggestions = useSuggestions(tripId, view === 'itinerary' ? activeDate : null)
  const feasibility = useFeasibility(tripId, view === 'itinerary' ? activeDate : null)

  if (trip.isLoading || places.isLoading || itinerary.isLoading) {
    return <p className="loading">Đang tải chuyến đi…</p>
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
  const me = currentTrip.members.find((member) => member.id === memberId)

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
    createPlace.mutate(body)
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

  return (
    <div className="workspace">
      <header className="trip-header">
        <div>
          <h1>{currentTrip.name}</h1>
          <p className="trip-meta">
            {currentTrip.destination} · {currentTrip.startDate} → {currentTrip.endDate} ·{' '}
            {currentTrip.timeZoneId}
          </p>
          {currentTrip.budgetAmount !== null && (
            <p className="trip-budget">
              Ngân sách:{' '}
              {formatMoney(
                currentTrip.budgetAmount,
                currentTrip.currency,
                currentTrip.currencyExponent,
              )}
            </p>
          )}
        </div>

        <div className="trip-invite">
          <span className="label">Mã mời</span>
          <code>{currentTrip.inviteCode}</code>
          <p className="members">
            {currentTrip.members.map((member) => member.displayName).join(', ')}
            {me && ` · bạn là ${me.displayName}`}
          </p>
        </div>
      </header>

      <nav className="view-tabs" role="tablist">
        <button
          type="button"
          role="tab"
          aria-selected={view === 'wishlist'}
          onClick={() => setView('wishlist')}
        >
          Wishlist ({currentPlaces.length})
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={view === 'itinerary'}
          onClick={() => setView('itinerary')}
        >
          Lịch trình ({itineraryItems.length})
        </button>
      </nav>

      {actionError && (
        <p className="form-error" role="alert">
          {actionError}
        </p>
      )}

      {view === 'itinerary' && (
        <main className="itinerary-body">
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
        </main>
      )}

      <main className="trip-body" hidden={view !== 'wishlist'}>
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

        <aside className="side-panel">
          <h2>Wishlist ({currentPlaces.length})</h2>

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
                  : null
            }
            tripUnderway={currentTrip.status !== 'Planning'}
            onSelect={setSelectedPlaceId}
            onDelete={handleDelete}
            onToggleLike={(placeId, liked) => toggleLike.mutate({ placeId, liked })}
            onChangeStatus={handleChangeStatus}
          />

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
        </aside>
      </main>
    </div>
  )
}
