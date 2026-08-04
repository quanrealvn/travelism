import { useState } from 'react'
import type { GeocodeResultResponse } from '../api/api-types'
import { ApiError } from '../api/client'
import { usePlaceSearch } from '../api/hooks'
import { useDebouncedSearchTerm } from '../hooks/useDebounced'

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
 * Type a place name, pick from the matches, and the coordinates come with it.
 * Search is a convenience, never a gate: if nothing matches — or the geocoder
 * is down — the form still accepts coordinates entered by hand.
 */
export function PlaceSearch({ tripId, onPick }: PlaceSearchProps) {
  const [term, setTerm] = useState('')
  const debounced = useDebouncedSearchTerm(term.trim(), 400)
  const search = usePlaceSearch(tripId, debounced)

  const showResults = debounced.length >= 2
  const results = search.data ?? []

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
          placeholder="ví dụ: Thác Dải Yếm"
          autoComplete="off"
          aria-label="Tìm địa điểm theo tên"
        />
      </label>

      {showResults && search.isFetching && <p className="search-hint">Đang tìm…</p>}

      {showResults && search.isError && (
        <p className="search-hint search-hint-error" role="status">
          {search.error instanceof ApiError && search.error.code === 'GEOCODING_UNAVAILABLE'
            ? 'Không tìm được lúc này. Bạn có thể nhập toạ độ thủ công bên dưới.'
            : 'Tìm kiếm lỗi. Nhập toạ độ thủ công bên dưới nhé.'}
        </p>
      )}

      {showResults && !search.isFetching && !search.isError && results.length === 0 && (
        <p className="search-hint" role="status">
          Không tìm thấy “{debounced}”. Bản đồ OpenStreetMap không có mọi địa
          điểm ở Việt Nam. Thử tên ngắn hơn (ví dụ “Nông Trường” thay vì cả địa
          chỉ), hoặc bấm thẳng lên bản đồ để chọn vị trí.
        </p>
      )}

      {results.length > 0 && (
        <ul className="search-results" aria-label="Kết quả tìm kiếm">
          {results.map((result) => (
            <li key={`${result.lat},${result.lng},${result.displayName}`}>
              <button type="button" onClick={() => pick(result)}>
                <span className="search-result-name">
                  {result.name}
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
