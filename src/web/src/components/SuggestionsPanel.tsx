import type { IsoDate, SuggestionGroupResponse } from '../api/api-types'
import { formatMoney } from '../api/money'
import { placeCategoryLabel } from '../api/labels'
import { formatDayLabel } from '../itinerary/tripDates'
import { Spinner } from './Spinner'

interface SuggestionsPanelProps {
  date: IsoDate | null
  groups: SuggestionGroupResponse[]
  loading: boolean
  currency: string
  currencyExponent: number
  onAdd: (placeId: string, date: IsoDate) => void
}

const SLOT_LABELS: Record<string, string> = {
  Morning: 'Sáng',
  Noon: 'Trưa',
  Afternoon: 'Chiều',
  Evening: 'Tối',
}

/**
 * What could still go on the selected day, grouped by time of day.
 *
 * The server decides the order (spec §5.1: something unlike what is already
 * planned first, then cheapest), so this renders the list as given rather than
 * re-sorting it.
 */
export function SuggestionsPanel({
  date,
  groups,
  loading,
  currency,
  currencyExponent,
  onAdd,
}: SuggestionsPanelProps) {
  if (!date) {
    return (
      <p className="empty-state small">Chọn một ngày để xem gợi ý.</p>
    )
  }

  const total = groups.reduce((count, group) => count + group.places.length, 0)

  return (
    <div className="suggestions">
      <h3>Gợi ý cho {formatDayLabel(date)}</h3>

      {loading && (
        <p className="search-hint inline-busy" role="status">
          <Spinner />
          Đang tải…
        </p>
      )}

      {!loading && total === 0 && (
        <p className="empty-state small">
          Không còn địa điểm nào đã chốt để thêm vào ngày này.
        </p>
      )}

      {groups.map((group) =>
        group.places.length === 0 ? null : (
          <section key={group.slot} className="suggestion-group">
            <h4>{SLOT_LABELS[group.slot] ?? group.slot}</h4>
            <ul>
              {group.places.map((place) => (
                <li key={place.placeId}>
                  <button type="button" onClick={() => onAdd(place.placeId, date)}>
                    <span className="suggestion-name">{place.name}</span>
                    <span className="suggestion-meta">
                      {placeCategoryLabel(place.category)} ·{' '}
                      {formatMoney(place.estimatedCost, currency, currencyExponent)}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          </section>
        ),
      )}
    </div>
  )
}
