import type { PlaceResponse } from '../api/api-types'
import { formatDuration, formatMoney } from '../api/money'

interface PlaceListProps {
  places: PlaceResponse[]
  currency: string
  currencyExponent: number
  selectedPlaceId: string | null
  deletingPlaceId: string | null
  onSelect: (placeId: string) => void
  onDelete: (placeId: string) => void
}

export function PlaceList({
  places,
  currency,
  currencyExponent,
  selectedPlaceId,
  deletingPlaceId,
  onSelect,
  onDelete,
}: PlaceListProps) {
  if (places.length === 0) {
    return (
      <p className="empty-state">
        Chưa có địa điểm nào. Thêm địa điểm đầu tiên để bắt đầu lên kế hoạch.
      </p>
    )
  }

  return (
    <ul className="place-list" aria-label="Danh sách địa điểm">
      {places.map((place) => (
        <li
          key={place.id}
          className={place.id === selectedPlaceId ? 'place selected' : 'place'}
          data-testid={`place-${place.id}`}
        >
          <button type="button" className="place-main" onClick={() => onSelect(place.id)}>
            <span className="place-name">{place.name}</span>
            <span className="place-meta">
              {place.category} · {formatDuration(place.estimatedDurationMinutes)} ·{' '}
              {formatMoney(place.estimatedCost, currency, currencyExponent)}
            </span>
            <span className="place-slots">{place.timeSlots.join(' · ')}</span>
            {place.openHoursText && <span className="place-hours">{place.openHoursText}</span>}
          </button>

          <span className={`status status-${place.status.toLowerCase()}`}>{place.status}</span>

          <button
            type="button"
            className="place-delete"
            onClick={() => onDelete(place.id)}
            disabled={deletingPlaceId === place.id}
            aria-label={`Xoá ${place.name}`}
          >
            {deletingPlaceId === place.id ? '…' : '✕'}
          </button>
        </li>
      ))}
    </ul>
  )
}
