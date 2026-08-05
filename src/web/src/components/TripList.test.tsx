import { describe, expect, it, vi } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { TripList } from './TripList'
import type { TripSummaryResponse } from '../api/api-types'

const TODAY = '2026-08-05'

function trip(overrides: Partial<TripSummaryResponse> = {}): TripSummaryResponse {
  return {
    id: 't1',
    name: 'Mộc Châu 3 ngày',
    destination: 'Mộc Châu, Sơn La',
    startDate: '2026-08-10',
    endDate: '2026-08-12',
    currency: 'VND',
    currencyExponent: 0,
    budgetAmount: 4_000_000,
    status: 'Planning',
    memberCount: 2,
    placeCount: 6,
    updatedAt: '2026-08-01T00:00:00+00:00',
    ...overrides,
  }
}

function renderList(trips: TripSummaryResponse[], today = TODAY) {
  const handlers = { onOpen: vi.fn(), onForget: vi.fn(), onNew: vi.fn() }

  render(
    <TripList
      trips={trips}
      today={today}
      activeTripId="t1"
      forgettingId={null}
      {...handlers}
    />,
  )

  return handlers
}

describe('TripList — splitting past from future', () => {
  it('files a trip that has not started under upcoming', () => {
    renderList([trip({ startDate: '2026-09-01', endDate: '2026-09-05' })])

    expect(screen.getByText(/sắp đi \(1\)/i)).toBeInTheDocument()
    expect(screen.queryByText(/đã đi/i)).not.toBeInTheDocument()
  })

  it('files a finished trip under past', () => {
    renderList([trip({ id: 't2', startDate: '2026-07-01', endDate: '2026-07-05' })])

    expect(screen.getByText(/đã đi \(1\)/i)).toBeInTheDocument()
    expect(screen.getByText(/sắp đi \(0\)/i)).toBeInTheDocument()
  })

  it('still counts a trip as upcoming on its final day', () => {
    // You are still on a trip on the morning it ends; filing it under "past"
    // while somebody is standing in it would be wrong.
    renderList([trip({ startDate: '2026-08-01', endDate: TODAY })])

    expect(screen.getByText(/sắp đi \(1\)/i)).toBeInTheDocument()
  })

  it('says a trip in progress is in progress', () => {
    renderList([trip({ startDate: '2026-08-01', endDate: '2026-08-09' })])

    expect(screen.getByTestId('trip-t1')).toHaveTextContent('Đang đi')
  })
})

describe('TripList — countdown', () => {
  it.each([
    ['2026-08-06', 'Ngày mai'],
    ['2026-08-12', 'Còn 7 ngày'],
    ['2026-11-05', 'Còn ~3 tháng'],
  ])('counts down to a trip starting %s', (startDate, expected) => {
    renderList([trip({ startDate, endDate: '2027-01-01' })])

    expect(screen.getByTestId('trip-t1')).toHaveTextContent(expected)
  })

  it.each([
    ['2026-08-04', 'Vừa xong'],
    ['2026-07-20', '16 ngày trước'],
    ['2026-02-05', '6 tháng trước'],
    ['2024-08-05', '2 năm trước'],
  ])('reports how long ago a trip ending %s was', (endDate, expected) => {
    renderList([trip({ startDate: '2020-01-01', endDate })])

    expect(screen.getByTestId('trip-t1')).toHaveTextContent(expected)
  })
})

describe('TripList — acting on a trip', () => {
  it('opens the trip that was tapped', async () => {
    const handlers = renderList([trip(), trip({ id: 't2', name: 'Đà Lạt' })])

    await userEvent.click(screen.getByText('Đà Lạt'))

    expect(handlers.onOpen).toHaveBeenCalledWith('t2')
  })

  it('marks which trip is currently open', () => {
    renderList([trip(), trip({ id: 't2', name: 'Đà Lạt' })])

    expect(screen.getByTestId('trip-t1')).toHaveClass('active')
    expect(screen.getByTestId('trip-t2')).not.toHaveClass('active')
  })

  it('says that forgetting is device-local rather than a deletion', async () => {
    // "Bỏ" next to a trip you planned for months has to be unambiguous.
    const handlers = renderList([trip()])

    const forget = within(screen.getByTestId('trip-t1')).getByLabelText(/bỏ .* khỏi thiết bị này/i)
    expect(forget).toHaveAttribute('title', expect.stringContaining('không xoá'))

    await userEvent.click(forget)
    expect(handlers.onForget).toHaveBeenCalledWith('t1')
  })

  it('shows how much is planned so an empty trip is obvious', () => {
    renderList([trip({ placeCount: 0, memberCount: 1 })])

    expect(screen.getByTestId('trip-t1')).toHaveTextContent('0 địa điểm · 1 người')
  })

  it('offers a way to start another trip', async () => {
    const handlers = renderList([trip()])

    await userEvent.click(screen.getByRole('button', { name: /chuyến đi mới/i }))

    expect(handlers.onNew).toHaveBeenCalled()
  })

  it('says so plainly when nothing is coming up', () => {
    renderList([trip({ startDate: '2020-01-01', endDate: '2020-01-05' })])

    expect(screen.getByText(/chưa có chuyến nào sắp tới/i)).toBeInTheDocument()
  })
})
