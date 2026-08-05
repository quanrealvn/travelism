import { describe, expect, it } from 'vitest'
import { applyPlaceFilter, EMPTY_FILTER, isFilterActive } from './placeFilter'
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

describe('isFilterActive', () => {
  it('is false for the empty filter', () => {
    expect(isFilterActive(EMPTY_FILTER)).toBe(false)
    expect(isFilterActive({ ...EMPTY_FILTER, text: '  ' })).toBe(false)
  })

  it.each([
    { ...EMPTY_FILTER, text: 'phở' },
    { ...EMPTY_FILTER, categories: ['Food' as const] },
    { ...EMPTY_FILTER, unvotedOnly: true },
  ])('is true once something is set', (filter) => {
    expect(isFilterActive(filter)).toBe(true)
  })
})
