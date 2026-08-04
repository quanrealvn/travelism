import { useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { ApiError } from './api/client'
import {
  queryKeys,
  useCreatePlace,
  useDeletePlace,
  usePlaces,
  useSession,
  useTrip,
} from './api/hooks'
import type { CreatePlaceRequest, TripSessionResponse } from './api/api-types'
import { StartScreen } from './components/StartScreen'
import { TripMap } from './components/TripMap'
import { PlaceForm } from './components/PlaceForm'
import { PlaceList } from './components/PlaceList'
import { formatMoney } from './api/money'

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

  const [selectedPlaceId, setSelectedPlaceId] = useState<string | null>(null)
  const [deleteError, setDeleteError] = useState<string | null>(null)

  if (trip.isLoading || places.isLoading) {
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
  const me = currentTrip.members.find((member) => member.id === memberId)

  function handleCreate(body: CreatePlaceRequest) {
    createPlace.mutate(body)
  }

  function handleDelete(placeId: string) {
    setDeleteError(null)
    deletePlace.mutate(
      { placeId },
      {
        onError: (error) => {
          // PLACE_IN_USE means the place is on the itinerary; milestone 3 adds
          // the confirm-and-force flow, so for now the reason is surfaced.
          setDeleteError(
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

      <main className="trip-body">
        <section className="map-panel">
          <TripMap
            places={currentPlaces}
            currency={currentTrip.currency}
            currencyExponent={currentTrip.currencyExponent}
            selectedPlaceId={selectedPlaceId}
            onSelectPlace={setSelectedPlaceId}
          />
        </section>

        <aside className="side-panel">
          <h2>Wishlist ({currentPlaces.length})</h2>

          {deleteError && (
            <p className="form-error" role="alert">
              {deleteError}
            </p>
          )}

          <PlaceList
            places={currentPlaces}
            currency={currentTrip.currency}
            currencyExponent={currentTrip.currencyExponent}
            selectedPlaceId={selectedPlaceId}
            deletingPlaceId={deletePlace.isPending ? deletePlace.variables.placeId : null}
            onSelect={setSelectedPlaceId}
            onDelete={handleDelete}
          />

          <PlaceForm
            tripId={tripId}
            currencyExponent={currentTrip.currencyExponent}
            pending={createPlace.isPending}
            fieldErrors={fieldErrors}
            submitError={createError}
            onSubmit={handleCreate}
          />
        </aside>
      </main>
    </div>
  )
}
