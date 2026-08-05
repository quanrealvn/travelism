import { ALL_CATEGORIES } from '../api/api-types'
import type { PlaceCategory } from '../api/api-types'
import { placeCategoryLabel } from '../api/labels'
import { categoryStyle } from '../map/placeMarkers'
import { EMPTY_FILTER, isFilterActive } from '../places/placeFilter'
import type { PlaceFilterState } from '../places/placeFilter'

interface PlaceFilterProps {
  value: PlaceFilterState
  /** How many places match, for the "nothing found" case. */
  matchCount: number
  totalCount: number
  onChange: (next: PlaceFilterState) => void
}

/**
 * The controls for narrowing a long wishlist. The filtering itself lives in
 * places/placeFilter.ts, so it can be tested without a DOM.
 */
export function PlaceFilter({ value, matchCount, totalCount, onChange }: PlaceFilterProps) {
  function toggleCategory(category: PlaceCategory) {
    onChange({
      ...value,
      categories: value.categories.includes(category)
        ? value.categories.filter((c) => c !== category)
        : [...value.categories, category],
    })
  }

  return (
    <div className="place-filter">
      <input
        type="search"
        className="place-filter-text"
        value={value.text}
        onChange={(event) => onChange({ ...value, text: event.target.value })}
        placeholder="Tìm địa điểm"
        aria-label="Tìm trong danh sách địa điểm"
      />

      <div className="place-filter-chips" role="group" aria-label="Lọc theo loại">
        {ALL_CATEGORIES.map((category) => {
          const active = value.categories.includes(category)
          return (
            <button
              key={category}
              type="button"
              className="filter-chip"
              aria-pressed={active}
              // The category's own colour, so the chips match the pins and the
              // card edges rather than being a fourth vocabulary.
              style={{ '--chip-colour': categoryStyle(category).color } as React.CSSProperties}
              onClick={() => toggleCategory(category)}
            >
              {placeCategoryLabel(category)}
            </button>
          )
        })}

        <button
          type="button"
          className="filter-chip"
          aria-pressed={value.unvotedOnly}
          onClick={() => onChange({ ...value, unvotedOnly: !value.unvotedOnly })}
        >
          Chưa bỏ phiếu
        </button>
      </div>

      {isFilterActive(value) && (
        <p className="place-filter-summary" role="status">
          {matchCount} / {totalCount} địa điểm
          <button type="button" className="link-button" onClick={() => onChange(EMPTY_FILTER)}>
            Bỏ lọc
          </button>
        </p>
      )}
    </div>
  )
}
