import { useState } from 'react'
import type { GeocodeResultResponse } from '../api/api-types'
import { ApiError } from '../api/client'
import { isLocationPaste, usePlaceLink, usePlaceSearch } from '../api/hooks'
import { useDebouncedSearchTerm } from '../hooks/useDebounced'
import { Spinner } from './Spinner'

interface PlaceSearchProps {
  tripId: string
  onPick: (result: GeocodeResultResponse) => void
}

/**
 * Beyond this, a match is almost certainly not the place that was meant — a
 * day trip does not span 150 km. Flagged rather than hidden, because a genuine
 * far-away stop (an airport, somewhere en route) must still be addable.
 */
const FAR_AWAY_KM = 150

function isFarAway(distanceKm: number | null): boolean {
  return distanceKm !== null && distanceKm > FAR_AWAY_KM
}

function formatDistance(distanceKm: number): string {
  if (distanceKm < 1) {
    return `${Math.round(distanceKm * 1000)} m`
  }

  return distanceKm < 10
    ? `${distanceKm.toFixed(1)} km`
    : `${Math.round(distanceKm).toLocaleString('vi-VN')} km`
}

/**
 * One box for two jobs: type a name to search OpenStreetMap, or paste a map
 * link to use it directly.
 *
 * The paste path matters because OSM does not have every place in Vietnam.
 * Planning already happens by sharing a Google Maps link in a group chat, so
 * pasting that link is both the most familiar action and the one with the best
 * coverage — the searching happened on Google's side.
 */
export function PlaceSearch({ tripId, onPick }: PlaceSearchProps) {
  const [term, setTerm] = useState('')
  const trimmed = term.trim()
  const pasted = isLocationPaste(trimmed)

  // A pasted link is resolved as-is; only typed names are debounced, since
  // there is no "still typing" to wait out when something was pasted whole.
  const debounced = useDebouncedSearchTerm(pasted ? '' : trimmed, 400)

  const search = usePlaceSearch(tripId, debounced)
  const link = usePlaceLink(tripId, pasted ? trimmed : '')

  const active = pasted ? link : search
  const results = pasted
    ? link.data
      ? [link.data]
      : []
    : (search.data ?? [])

  const showFeedback = pasted || debounced.length >= 2

  function pick(result: GeocodeResultResponse) {
    onPick(result)
    setTerm('')
  }

  return (
    <div className="place-search">
      <label>
        Tìm địa điểm
        <input
          type="search"
          value={term}
          onChange={(e) => setTerm(e.target.value)}
          placeholder="Tên địa điểm, hoặc dán link Google Maps"
          autoComplete="off"
          aria-label="Tìm địa điểm theo tên hoặc dán link bản đồ"
        />
      </label>

      <p className="search-hint search-hint-quiet">
        Mẹo: mở địa điểm trong Google Maps → Chia sẻ → dán link vào đây.
      </p>

      {showFeedback && active.isFetching && (
        <p className="search-hint inline-busy" role="status">
          <Spinner />
          {pasted ? 'Đang mở link…' : 'Đang tìm…'}
        </p>
      )}

      {showFeedback && active.isError && (
        <p className="search-hint search-hint-error" role="status">
          {describeError(active.error, pasted)}
        </p>
      )}

      {showFeedback && !active.isFetching && !active.isError && results.length === 0 && !pasted && (
        <p className="search-hint" role="status">
          Không tìm thấy “{debounced}”. Bản đồ OpenStreetMap không có mọi địa
          điểm ở Việt Nam. Thử tên ngắn hơn, dán link Google Maps, hoặc bấm
          thẳng lên bản đồ.
        </p>
      )}

      {results.length > 0 && (
        <ul className="search-results" aria-label="Kết quả tìm kiếm">
          {results.map((result) => (
            <li key={`${result.lat},${result.lng},${result.displayName}`}>
              <button type="button" onClick={() => pick(result)}>
                <span className="search-result-name">
                  {result.name === '' ? 'Vị trí từ link' : result.name}
                  {isFarAway(result.distanceKm) && (
                    <span className="search-result-far" title="Rất xa các địa điểm khác của chuyến đi">
                      xa chuyến đi
                    </span>
                  )}
                </span>
                <span className="search-result-meta">
                  {result.kind && <span className="search-result-kind">{result.kind}</span>}
                  {result.distanceKm !== null && (
                    <span className="search-result-distance">{formatDistance(result.distanceKm)}</span>
                  )}
                </span>
                <span className="search-result-address">{result.displayName}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

function describeError(error: unknown, pasted: boolean): string {
  if (error instanceof ApiError && error.code === 'LINK_NOT_RECOGNISED') {
    return 'Link này không chứa vị trí. Trong Google Maps, bấm Chia sẻ rồi sao chép link.'
  }

  if (error instanceof ApiError && error.code === 'GEOCODING_UNAVAILABLE') {
    return 'Không tìm được lúc này. Bạn có thể dán link Google Maps hoặc bấm lên bản đồ.'
  }

  return pasted
    ? 'Không mở được link. Thử dán lại, hoặc bấm thẳng lên bản đồ.'
    : 'Tìm kiếm lỗi. Dán link Google Maps hoặc bấm lên bản đồ nhé.'
}
