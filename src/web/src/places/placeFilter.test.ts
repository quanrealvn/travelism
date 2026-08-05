import { describe, expect, it } from 'vitest'
import { applyPlaceFilter, countByStatus, EMPTY_FILTER, isFilterActive } from './placeFilter'
import type { PlaceResponse } from '../api/api-types'

const ME = 'member-1'

function place(overrides: Partial<PlaceResponse> = {}): PlaceResponse {
  return {
    id: 'p1',
    tripId: 't1',
    name: 'Thác Dải Yếm',
    lat: 20.83,
    lng: 104.66,
    category: 'Sight',
    timeSlots: ['Morning'],
    estimatedDurationMinutes: 90,
    estimatedCost: 50_000,
    openHoursText: null,
    description: null,
    references: [],
    status: 'Idea',
    skipReason: null,
    isDeleted: false,
    likedByMemberIds: [],
    createdAt: '2026-03-01T00:00:00+00:00',
    updatedAt: '2026-03-01T00:00:00+00:00',
    updatedByMemberId: ME,
    ...overrides,
  }
}

const PLACES = [
  place({ id: 'a', name: 'Thác Dải Yếm', category: 'Sight' }),
  place({ id: 'b', name: 'Quán phở Cường', category: 'Food', likedByMemberIds: [ME] }),
  place({ id: 'c', name: 'Đồi chè trái tim', category: 'Photo' }),
  place({
    id: 'd',
    name: 'Homestay Mùa',
    category: 'Rest',
    description: 'Gần quán phở, đi bộ 5 phút',
  }),
]

const ids = (places: PlaceResponse[]) => places.map((p) => p.id)

describe('applyPlaceFilter', () => {
  it('returns everything when nothing is set', () => {
    expect(ids(applyPlaceFilter(PLACES, EMPTY_FILTER, ME))).toEqual(['a', 'b', 'c', 'd'])
  })

  it('matches on the name', () => {
    const found = applyPlaceFilter(PLACES, { ...EMPTY_FILTER, text: 'thác' }, ME)

    expect(ids(found)).toEqual(['a'])
  })

  it('ignores case and matches Vietnamese diacritics as written', () => {
    expect(ids(applyPlaceFilter(PLACES, { ...EMPTY_FILTER, text: 'ĐỒI CHÈ' }, ME))).toEqual(['c'])
  })

  it('matches on the description too', () => {
    // People write "quán phở gần homestay" in the note and remember that
    // rather than the name.
    const found = applyPlaceFilter(PLACES, { ...EMPTY_FILTER, text: 'đi bộ' }, ME)

    expect(ids(found)).toEqual(['d'])
  })

  it('filters by category', () => {
    const found = applyPlaceFilter(PLACES, { ...EMPTY_FILTER, categories: ['Food', 'Photo'] }, ME)

    expect(ids(found)).toEqual(['b', 'c'])
  })

  it('combines text and category as an AND', () => {
    const found = applyPlaceFilter(
      PLACES,
      { ...EMPTY_FILTER, text: 'quán', categories: ['Photo'] },
      ME,
    )

    expect(found).toEqual([])
  })

  it('shows only what I have not voted on', () => {
    const found = applyPlaceFilter(PLACES, { ...EMPTY_FILTER, unvotedOnly: true }, ME)

    expect(ids(found)).toEqual(['a', 'c', 'd'])
  })

  it('treats a blank search as no search rather than as no matches', () => {
    expect(applyPlaceFilter(PLACES, { ...EMPTY_FILTER, text: '   ' }, ME)).toHaveLength(4)
  })
})

describe('applyPlaceFilter — by status', () => {
  const MIXED = [
    place({ id: 'agreed', status: 'Confirmed' }),
    place({ id: 'maybe', status: 'Shortlist' }),
    place({ id: 'raw', status: 'Idea' }),
  ]

  it('narrows to one status', () => {
    const found = applyPlaceFilter(MIXED, { ...EMPTY_FILTER, statuses: ['Shortlist'] }, ME)

    expect(ids(found)).toEqual(['maybe'])
  })

  it('treats an empty list as every status rather than none', () => {
    expect(applyPlaceFilter(MIXED, EMPTY_FILTER, ME)).toHaveLength(3)
  })

  it('combines with a text search', () => {
    const found = applyPlaceFilter(
      [...MIXED, place({ id: 'other', name: 'Quán phở', status: 'Idea' })],
      { ...EMPTY_FILTER, statuses: ['Idea'], text: 'phở' },
      ME,
    )

    expect(ids(found)).toEqual(['other'])
  })
})

describe('countByStatus', () => {
  it('counts each status, including the ones with nothing in them', () => {
    const counts = countByStatus([
      place({ id: 'a', status: 'Confirmed' }),
      place({ id: 'b', status: 'Confirmed' }),
      place({ id: 'c', status: 'Idea' }),
    ])

    expect(counts).toEqual({ Confirmed: 2, Idea: 1, Shortlist: 0, Visited: 0, Skipped: 0 })
  })

  it('reports zeroes rather than an empty object for an empty trip', () => {
    // The overview renders every tile, so a missing key would print undefined.
    expect(countByStatus([])).toEqual({
      Idea: 0,
      Shortlist: 0,
      Confirmed: 0,
      Visited: 0,
      Skipped: 0,
    })
  })
})

describe('isFilterActive', () => {
  it('is false for the empty filter', () => {
    expect(isFilterActive(EMPTY_FILTER)).toBe(false)
    expect(isFilterActive({ ...EMPTY_FILTER, text: '  ' })).toBe(false)
  })

  it.each([
    { ...EMPTY_FILTER, text: 'phở' },
    { ...EMPTY_FILTER, categories: ['Food' as const] },
    { ...EMPTY_FILTER, statuses: ['Idea' as const] },
    { ...EMPTY_FILTER, unvotedOnly: true },
  ])('is true once something is set', (filter) => {
    expect(isFilterActive(filter)).toBe(true)
  })
})
