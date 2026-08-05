import type { IsoDate, TripSummaryResponse } from '../api/api-types'
import { formatMoney } from '../api/money'
import { formatDateLabel } from '../itinerary/tripDates'
import { IconClose, IconPin, IconPlus } from './icons'

interface TripListProps {
  trips: TripSummaryResponse[]
  /** Today where the traveller is standing, taken from their own clock. */
  today: IsoDate
  activeTripId: string | null
  forgettingId: string | null
  onOpen: (tripId: string) => void
  onForget: (tripId: string) => void
  onNew: () => void
}

/**
 * Every trip this browser holds, split into what is still ahead and what has
 * already happened.
 *
 * Past trips are kept rather than archived away: the wishlist of a place you
 * loved and the record of what it cost are the two things people actually go
 * back to.
 */
export function TripList({
  trips,
  today,
  activeTripId,
  forgettingId,
  onOpen,
  onForget,
  onNew,
}: TripListProps) {
  // A trip is past only once its last day is over — you are still on a trip on
  // the morning of the day it ends.
  const upcoming = trips.filter((trip) => trip.endDate >= today)
  const past = trips.filter((trip) => trip.endDate < today)

  return (
    <div className="trip-list">
      <section>
        <h2 className="section-title">Sắp đi ({upcoming.length})</h2>

        {upcoming.length === 0 ? (
          <p className="empty-state small">Chưa có chuyến nào sắp tới.</p>
        ) : (
          <ul className="trip-cards">
            {upcoming.map((trip) => (
              <TripCard
                key={trip.id}
                trip={trip}
                today={today}
                active={trip.id === activeTripId}
                forgetting={forgettingId === trip.id}
                onOpen={onOpen}
                onForget={onForget}
              />
            ))}
          </ul>
        )}
      </section>

      {past.length > 0 && (
        <section>
          <h2 className="section-title">Đã đi ({past.length})</h2>
          <ul className="trip-cards">
            {past.map((trip) => (
              <TripCard
                key={trip.id}
                trip={trip}
                today={today}
                active={trip.id === activeTripId}
                forgetting={forgettingId === trip.id}
                past
                onOpen={onOpen}
                onForget={onForget}
              />
            ))}
          </ul>
        </section>
      )}

      <button type="button" className="button-primary trip-list-new" onClick={onNew}>
        <IconPlus />
        Chuyến đi mới
      </button>
    </div>
  )
}

function TripCard({
  trip,
  today,
  active,
  forgetting,
  past = false,
  onOpen,
  onForget,
}: {
  trip: TripSummaryResponse
  today: IsoDate
  active: boolean
  forgetting: boolean
  past?: boolean
  onOpen: (tripId: string) => void
  onForget: (tripId: string) => void
}) {
  const className = ['trip-card', active ? 'active' : '', past ? 'past' : '']
    .filter(Boolean)
    .join(' ')

  return (
    <li className={className} data-testid={`trip-${trip.id}`}>
      <button type="button" className="trip-card-open" onClick={() => onOpen(trip.id)}>
        <span className="trip-card-name">{trip.name}</span>
        <span className="trip-card-where">
          <IconPin />
          {trip.destination}
        </span>
        <span className="trip-card-when">
          {formatDateLabel(trip.startDate)} – {formatDateLabel(trip.endDate)}
          <span className="trip-card-countdown">{countdown(trip, today)}</span>
        </span>
        <span className="trip-card-stats">
          <span>
            {trip.placeCount} địa điểm · {trip.memberCount} người
          </span>
          {trip.budgetAmount !== null && (
            <span>{formatMoney(trip.budgetAmount, trip.currency, trip.currencyExponent)}</span>
          )}
        </span>
      </button>

      <button
        type="button"
        className="trip-card-forget"
        onClick={() => onForget(trip.id)}
        disabled={forgetting}
        // Not "leave the trip" and not "delete it": this only removes it from
        // this device, and the invite code brings it back.
        title="Bỏ khỏi thiết bị này (không xoá chuyến đi)"
        aria-label={`Bỏ ${trip.name} khỏi thiết bị này`}
      >
        <IconClose />
      </button>
    </li>
  )
}

/** "Còn 12 ngày" / "Đang đi" / "3 tháng trước" — the thing you actually want to know. */
function countdown(trip: TripSummaryResponse, today: IsoDate): string {
  if (trip.startDate <= today && today <= trip.endDate) {
    return 'Đang đi'
  }

  const days = daysBetween(today, trip.startDate)
  if (days > 0) {
    if (days === 1) return 'Ngày mai'
    if (days < 30) return `Còn ${days} ngày`
    const months = Math.round(days / 30)
    return `Còn ~${months} tháng`
  }

  const since = daysBetween(trip.endDate, today)
  if (since <= 1) return 'Vừa xong'
  if (since < 30) return `${since} ngày trước`
  const months = Math.round(since / 30)
  return months < 12 ? `${months} tháng trước` : `${Math.round(months / 12)} năm trước`
}

/**
 * Whole days from one calendar date to another.
 *
 * Both are parsed as UTC and differenced in milliseconds: these are calendar
 * positions, not instants, so the local zone must never be consulted — a trip
 * spanning a daylight-saving change would otherwise gain or lose a day.
 */
function daysBetween(from: IsoDate, to: IsoDate): number {
  const [fy, fm, fd] = from.split('-').map(Number)
  const [ty, tm, td] = to.split('-').map(Number)
  const start = Date.UTC(fy ?? 1970, (fm ?? 1) - 1, fd ?? 1)
  const end = Date.UTC(ty ?? 1970, (tm ?? 1) - 1, td ?? 1)
  return Math.round((end - start) / 86_400_000)
}
