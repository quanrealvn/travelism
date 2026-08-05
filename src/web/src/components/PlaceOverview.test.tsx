import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PlaceOverview } from './PlaceOverview'
import { EMPTY_FILTER } from '../places/placeFilter'
import type { PlaceResponse } from '../api/api-types'

const ME = 'member-1'
const OTHER = 'member-2'

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

function renderOverview(places: PlaceResponse[], filter = EMPTY_FILTER) {
  const onChange = vi.fn()
  render(
    <PlaceOverview places={places} myMemberId={ME} filter={filter} onChange={onChange} />,
  )
  return onChange
}

const tile = (name: RegExp) => screen.getByRole('button', { name })

describe('PlaceOverview', () => {
  it('counts each status', () => {
    renderOverview([
      place({ id: 'a', status: 'Confirmed' }),
      place({ id: 'b', status: 'Confirmed' }),
      place({ id: 'c', status: 'Shortlist' }),
    ])

    expect(tile(/đã chốt/i)).toHaveTextContent('2')
    expect(tile(/cân nhắc/i)).toHaveTextContent('1')
    expect(tile(/ý tưởng/i)).toHaveTextContent('0')
  })

  it('filters to a status when its tile is pressed', async () => {
    const onChange = renderOverview([place({ status: 'Confirmed' })])

    await userEvent.click(tile(/đã chốt/i))

    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ statuses: ['Confirmed'], unvotedOnly: false }),
    )
  })

  it('clears the filter when the pressed tile is pressed again', async () => {
    const onChange = renderOverview([place({ status: 'Confirmed' })], {
      ...EMPTY_FILTER,
      statuses: ['Confirmed'],
    })

    await userEvent.click(tile(/đã chốt/i))

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ statuses: [] }))
  })

  it('replaces rather than accumulates, so a tile always means one group', async () => {
    const onChange = renderOverview([place({ status: 'Idea' })], {
      ...EMPTY_FILTER,
      statuses: ['Confirmed'],
    })

    await userEvent.click(tile(/ý tưởng/i))

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ statuses: ['Idea'] }))
  })

  it('counts what is waiting on me across both undecided groups', () => {
    renderOverview([
      place({ id: 'a', status: 'Idea' }),
      place({ id: 'b', status: 'Shortlist', likedByMemberIds: [OTHER] }),
      place({ id: 'c', status: 'Shortlist', likedByMemberIds: [ME] }),
      // Already decided — nothing is being asked of me.
      place({ id: 'd', status: 'Confirmed', likedByMemberIds: [ME, OTHER] }),
    ])

    expect(tile(/chờ bạn/i)).toHaveTextContent('2')
  })

  it('hides the waiting tile when nothing is waiting', () => {
    // A permanent "0 chờ bạn" teaches you to ignore the row.
    renderOverview([place({ status: 'Confirmed', likedByMemberIds: [ME, OTHER] })])

    expect(screen.queryByRole('button', { name: /chờ bạn/i })).not.toBeInTheDocument()
  })

  it('says which tile is filtering', () => {
    renderOverview([place({ status: 'Confirmed' })], {
      ...EMPTY_FILTER,
      statuses: ['Confirmed'],
    })

    expect(tile(/đã chốt/i)).toHaveAttribute('aria-pressed', 'true')
    expect(tile(/ý tưởng/i)).toHaveAttribute('aria-pressed', 'false')
  })
})
