import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PlaceList } from './PlaceList'
import type { PlaceResponse } from '../api/api-types'

function place(overrides: Partial<PlaceResponse> = {}): PlaceResponse {
  return {
    id: 'p1',
    tripId: 't1',
    name: 'Thác Dải Yếm',
    lat: 20.8333,
    lng: 104.6667,
    category: 'Sight',
    timeSlots: ['Morning', 'Afternoon'],
    estimatedDurationMinutes: 90,
    estimatedCost: 50_000,
    openHoursText: null,
    status: 'Idea',
    skipReason: null,
    isDeleted: false,
    likedByMemberIds: [],
    createdAt: '2026-03-01T00:00:00+00:00',
    updatedAt: '2026-03-01T00:00:00+00:00',
    updatedByMemberId: 'm1',
    ...overrides,
  }
}

describe('PlaceList', () => {
  it('shows an empty state when there is nothing to plan yet', () => {
    render(
      <PlaceList
        places={[]}
        currency="VND"
        currencyExponent={0}
        selectedPlaceId={null}
        deletingPlaceId={null}
        onSelect={vi.fn()}
        onDelete={vi.fn()}
      />,
    )

    expect(screen.getByText(/chưa có địa điểm nào/i)).toBeInTheDocument()
  })

  it('renders cost using the trip currency exponent, not a hard-coded one', () => {
    render(
      <PlaceList
        places={[place({ estimatedCost: 100_001 })]}
        currency="VND"
        currencyExponent={0}
        selectedPlaceId={null}
        deletingPlaceId={null}
        onSelect={vi.fn()}
        onDelete={vi.fn()}
      />,
    )

    // 100_001 VND minor units is ₫100.001 — a two-decimal reading would show 1.000,01.
    expect(screen.getByText(/100\.001/)).toBeInTheDocument()
  })

  it('renders a missing cost as a dash rather than as zero', () => {
    render(
      <PlaceList
        places={[place({ estimatedCost: null })]}
        currency="VND"
        currencyExponent={0}
        selectedPlaceId={null}
        deletingPlaceId={null}
        onSelect={vi.fn()}
        onDelete={vi.fn()}
      />,
    )

    expect(screen.getByText(/—/)).toBeInTheDocument()
  })

  it('marks the selected place and reports selection changes', async () => {
    const onSelect = vi.fn()
    render(
      <PlaceList
        places={[place({ id: 'a', name: 'A' }), place({ id: 'b', name: 'B' })]}
        currency="VND"
        currencyExponent={0}
        selectedPlaceId="a"
        deletingPlaceId={null}
        onSelect={onSelect}
        onDelete={vi.fn()}
      />,
    )

    expect(screen.getByTestId('place-a')).toHaveClass('selected')
    expect(screen.getByTestId('place-b')).not.toHaveClass('selected')

    await userEvent.click(screen.getByText('B'))
    expect(onSelect).toHaveBeenCalledWith('b')
  })

  it('disables only the row currently being deleted', () => {
    render(
      <PlaceList
        places={[place({ id: 'a', name: 'A' }), place({ id: 'b', name: 'B' })]}
        currency="VND"
        currencyExponent={0}
        selectedPlaceId={null}
        deletingPlaceId="a"
        onSelect={vi.fn()}
        onDelete={vi.fn()}
      />,
    )

    expect(screen.getByLabelText('Xoá A')).toBeDisabled()
    expect(screen.getByLabelText('Xoá B')).toBeEnabled()
  })

  it('asks to delete the place whose button was pressed', async () => {
    const onDelete = vi.fn()
    render(
      <PlaceList
        places={[place({ id: 'a', name: 'A' }), place({ id: 'b', name: 'B' })]}
        currency="VND"
        currencyExponent={0}
        selectedPlaceId={null}
        deletingPlaceId={null}
        onSelect={vi.fn()}
        onDelete={onDelete}
      />,
    )

    await userEvent.click(screen.getByLabelText('Xoá B'))
    expect(onDelete).toHaveBeenCalledWith('b')
    expect(onDelete).toHaveBeenCalledTimes(1)
  })

  it('shows the place status so a confirmed place is visibly different', () => {
    render(
      <PlaceList
        places={[place({ status: 'Confirmed' })]}
        currency="VND"
        currencyExponent={0}
        selectedPlaceId={null}
        deletingPlaceId={null}
        onSelect={vi.fn()}
        onDelete={vi.fn()}
      />,
    )

    expect(screen.getByText('Confirmed')).toHaveClass('status-confirmed')
  })
})
