import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ItineraryBoard } from './ItineraryBoard'
import type { ItineraryItemResponse, PlaceResponse } from '../api/api-types'

const DAYS = ['2026-03-01', '2026-03-02']

function place(overrides: Partial<PlaceResponse> = {}): PlaceResponse {
  return {
    id: 'p1',
    tripId: 't1',
    name: 'Thác Dải Yếm',
    lat: 20.8333,
    lng: 104.6667,
    category: 'Sight',
    timeSlots: ['Morning'],
    estimatedDurationMinutes: 90,
    estimatedCost: 50_000,
    openHoursText: null,
    description: null,
    references: [],
    status: 'Confirmed',
    skipReason: null,
    isDeleted: false,
    likedByMemberIds: [],
    createdAt: '2026-03-01T00:00:00+00:00',
    updatedAt: '2026-03-01T00:00:00+00:00',
    updatedByMemberId: 'm1',
    ...overrides,
  }
}

function item(overrides: Partial<ItineraryItemResponse> = {}): ItineraryItemResponse {
  return {
    id: 'i1',
    tripId: 't1',
    placeId: 'p1',
    placeName: 'Thác Dải Yếm',
    placeCategory: 'Sight',
    lat: 20.8333,
    lng: 104.6667,
    date: '2026-03-01',
    startTime: null,
    note: null,
    actualCost: null,
    estimatedCost: 50_000,
    estimatedDurationMinutes: 90,
    createdAt: '2026-03-01T00:00:00+00:00',
    updatedAt: '2026-03-01T00:00:00+00:00',
    updatedByMemberId: 'm1',
    ...overrides,
  }
}

function renderBoard(overrides: Partial<Parameters<typeof ItineraryBoard>[0]> = {}) {
  const handlers = {
    onMoveItem: vi.fn(),
    onSchedulePlace: vi.fn(),
    onRemoveItem: vi.fn(),
    onSetTime: vi.fn(),
    onSelectDate: vi.fn(),
  }

  render(
    <ItineraryBoard
      days={DAYS}
      items={[]}
      confirmedPlaces={[place()]}
      currency="VND"
      currencyExponent={0}
      movingItemId={null}
      findings={[]}
      selectedDate="2026-03-01"
      {...handlers}
      {...overrides}
    />,
  )

  return handlers
}

describe('ItineraryBoard pool', () => {
  it('adds a confirmed place to the selected day in one tap', async () => {
    // Dragging is a mouse gesture, and on a phone only the selected day is on
    // screen — so there is nowhere to drag to.
    const handlers = renderBoard()

    await userEvent.click(screen.getByRole('button', { name: /thêm thác dải yếm vào/i }))

    expect(handlers.onSchedulePlace).toHaveBeenCalledWith('p1', '2026-03-01')
  })

  it('does not offer to add a place that is already on that day', () => {
    // mirror of server rule (spec §6): a place appears at most once per date,
    // so this button could only ever fail.
    renderBoard({ items: [item({ date: '2026-03-01' })] })

    expect(screen.queryByRole('button', { name: /thêm thác dải yếm vào/i })).not.toBeInTheDocument()
  })

  it('still offers to add it when it is only scheduled on another day', () => {
    renderBoard({ items: [item({ date: '2026-03-02' })] })

    expect(screen.getByRole('button', { name: /thêm thác dải yếm vào/i })).toBeInTheDocument()
  })

  it('keeps the place in the pool once scheduled, so it can still be moved', () => {
    renderBoard({ items: [item({ date: '2026-03-01' })] })

    expect(screen.getByTestId('pool-p1')).toBeInTheDocument()
  })

  it('says why there is nothing to drag when nothing is confirmed', () => {
    renderBoard({ confirmedPlaces: [] })

    expect(screen.getByText(/thích một địa điểm/i)).toBeInTheDocument()
  })
})
