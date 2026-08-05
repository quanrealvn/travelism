import type { PlaceResponse, PlaceStatus } from '../api/api-types'
import { countByStatus } from '../places/placeFilter'
import type { PlaceFilterState } from '../places/placeFilter'

const TILES: { status: PlaceStatus; label: string }[] = [
  { status: 'Confirmed', label: 'Đã chốt' },
  { status: 'Shortlist', label: 'Cân nhắc' },
  { status: 'Idea', label: 'Ý tưởng' },
]

/*
 * Four tiles across a 390px screen leaves about 80px each, and an icon eats a
 * quarter of that — enough to truncate "Cân nhắc" to "Cân nhắ". The group
 * headings below carry the same icons at full size, so these are the copies
 * that can go.
 */

interface PlaceOverviewProps {
  places: PlaceResponse[]
  myMemberId: string
  filter: PlaceFilterState
  onChange: (next: PlaceFilterState) => void
}

/**
 * Where the trip stands, and the fastest way to act on it.
 *
 * The list was groups of cards and nothing else: you could not tell how far
 * along the planning was without scrolling all of it, and there was no answer
 * to "what still needs me". These read as status at a glance and each one is
 * also the filter for its own group, so the overview and the way to act on it
 * are the same control rather than two rows of chrome.
 */
export function PlaceOverview({ places, myMemberId, filter, onChange }: PlaceOverviewProps) {
  const counts = countByStatus(places)
  const needsMe = places.filter(
    (place) =>
      !place.likedByMemberIds.includes(myMemberId) &&
      (place.status === 'Idea' || place.status === 'Shortlist'),
  ).length

  function toggleStatus(status: PlaceStatus) {
    onChange({
      ...filter,
      unvotedOnly: false,
      statuses: filter.statuses.includes(status) ? [] : [status],
    })
  }

  return (
    <div className="overview" role="group" aria-label="Tổng quan wishlist">
      {TILES.map((tile) => {
        const active = filter.statuses.includes(tile.status)
        return (
          <button
            key={tile.status}
            type="button"
            className={`overview-tile status-${tile.status.toLowerCase()}`}
            aria-pressed={active}
            onClick={() => toggleStatus(tile.status)}
          >
            <span className="overview-count">{counts[tile.status]}</span>
            <span className="overview-label">{tile.label}</span>
          </button>
        )
      })}

      {/*
        Only when there is something to do. A permanent "0 chờ bạn" is a fourth
        tile teaching you to ignore the row.
      */}
      {needsMe > 0 && (
        <button
          type="button"
          className="overview-tile status-mine"
          aria-pressed={filter.unvotedOnly}
          onClick={() =>
            onChange({ ...filter, statuses: [], unvotedOnly: !filter.unvotedOnly })
          }
        >
          <span className="overview-count">{needsMe}</span>
          <span className="overview-label">Chờ bạn</span>
        </button>
      )}
    </div>
  )
}
