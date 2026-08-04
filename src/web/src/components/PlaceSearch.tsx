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
          Không tìm thấy. Thử tên khác, hoặc nhập toạ độ thủ công bên dưới.
        </p>
      )}

      {results.length > 0 && (
        <ul className="search-results" aria-label="Kết quả tìm kiếm">
          {results.map((result) => (
            <li key={`${result.lat},${result.lng},${result.displayName}`}>
              <button type="button" onClick={() => pick(result)}>
                <span className="search-result-name">{result.name}</span>
                {result.kind && <span className="search-result-kind">{result.kind}</span>}
                <span className="search-result-address">{result.displayName}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
