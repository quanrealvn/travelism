import type { PlaceCategory, PlaceResponse, PlaceStatus } from '../api/api-types'

export interface PlaceFilterState {
  text: string
  categories: PlaceCategory[]
  /** Empty means every status; otherwise only these. */
  statuses: PlaceStatus[]
  /** Only places this member has not voted on either way. */
  unvotedOnly: boolean
}

export const EMPTY_FILTER: PlaceFilterState = {
  text: '',
  categories: [],
  statuses: [],
  unvotedOnly: false,
}

export function isFilterActive(filter: PlaceFilterState): boolean {
  return (
    filter.text.trim() !== '' ||
    filter.categories.length > 0 ||
    filter.statuses.length > 0 ||
    filter.unvotedOnly
  )
}

/** How many places sit in each status, for the overview strip. */
export function countByStatus(places: PlaceResponse[]): Record<PlaceStatus, number> {
  const counts: Record<PlaceStatus, number> = {
    Idea: 0,
    Shortlist: 0,
    Confirmed: 0,
    Visited: 0,
    Skipped: 0,
  }

  for (const place of places) {
    counts[place.status] += 1
  }

  return counts
}

/**
 * Narrows the wishlist.
 *
 * Grouping by status is the only structure a long wishlist has, and it does not
 * help you find one place among forty — that is ten screens of scrolling with
 * nothing to search. Everything here filters in memory: the whole list is
 * already loaded, so there is no reason to ask the server.
 */
export function applyPlaceFilter(
  places: PlaceResponse[],
  filter: PlaceFilterState,
  myMemberId: string,
): PlaceResponse[] {
  const needle = filter.text.trim().toLocaleLowerCase('vi')

  return places.filter((place) => {
    if (filter.statuses.length > 0 && !filter.statuses.includes(place.status)) {
      return false
    }

    if (filter.categories.length > 0 && !filter.categories.includes(place.category)) {
      return false
    }

    if (filter.unvotedOnly && place.likedByMemberIds.includes(myMemberId)) {
      return false
    }

    if (needle === '') {
      return true
    }

    // The description too: people write "quán phở gần homestay" there, and it
    // is often what they remember rather than the name.
    return (
      place.name.toLocaleLowerCase('vi').includes(needle) ||
      (place.description ?? '').toLocaleLowerCase('vi').includes(needle)
    )
  })
}
