import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { TripSwitcher } from './TripSwitcher'
import type { TripSummaryResponse } from '../api/api-types'
import { api } from '../api/client'

function trip(overrides: Partial<TripSummaryResponse> = {}): TripSummaryResponse {
  return {
    id: 't1',
    name: 'Mộc Châu 3 ngày 2 đêm',
    destination: 'Mộc Châu, Sơn La',
    startDate: '2026-08-10',
    endDate: '2026-08-12',
    currency: 'VND',
    currencyExponent: 0,
    budgetAmount: null,
    status: 'Planning',
    memberCount: 2,
    placeCount: 6,
    updatedAt: '2026-08-01T00:00:00+00:00',
    ...overrides,
  }
}

const TRIPS = [trip(), trip({ id: 't2', name: 'Hà Giang mùa hoa', destination: 'Hà Giang' })]

function renderSwitcher(activeTripId = 't1') {
  vi.spyOn(api, 'myTrips').mockResolvedValue(TRIPS)

  const handlers = {
    onOpenTrip: vi.fn(),
    onNewTrip: vi.fn(),
    onSeeAll: vi.fn(),
  }

  render(
    <QueryClientProvider
      client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}
    >
      <TripSwitcher activeTripId={activeTripId} tripCount={2} {...handlers} />
    </QueryClientProvider>,
  )

  return handlers
}

const open = () => userEvent.click(screen.getByRole('button', { name: /đổi chuyến đi/i }))

describe('TripSwitcher', () => {
  it('stays closed until asked', () => {
    renderSwitcher()

    expect(screen.getByRole('button', { name: /đổi chuyến đi/i })).toHaveAttribute(
      'aria-expanded',
      'false',
    )
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
  })

  it('lists the trips this browser holds', async () => {
    renderSwitcher()

    await open()

    expect(await screen.findByRole('menuitem', { name: /hà giang mùa hoa/i })).toBeInTheDocument()
    expect(screen.getByRole('menu')).toBeInTheDocument()
  })

  it('says which trip is open in words, not only in colour', async () => {
    renderSwitcher('t1')

    await open()

    expect(
      await screen.findByRole('menuitem', { name: /mộc châu 3 ngày 2 đêm.*đang mở/i }),
    ).toBeInTheDocument()
  })

  it('switches without leaving the workspace', async () => {
    const { onOpenTrip } = renderSwitcher()

    await open()
    await userEvent.click(await screen.findByRole('menuitem', { name: /hà giang/i }))

    expect(onOpenTrip).toHaveBeenCalledWith('t2')
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
  })

  it('starts a new trip from the same menu you go to looking for one', async () => {
    const { onNewTrip } = renderSwitcher()

    await open()
    await userEvent.click(await screen.findByRole('menuitem', { name: /chuyến đi mới/i }))

    expect(onNewTrip).toHaveBeenCalled()
  })

  it('keeps a way through to the full screen, which carries what a menu cannot', async () => {
    const { onSeeAll } = renderSwitcher()

    await open()
    await userEvent.click(await screen.findByRole('menuitem', { name: /xem tất cả/i }))

    expect(onSeeAll).toHaveBeenCalled()
  })

  it('closes on Escape', async () => {
    renderSwitcher()

    await open()
    await screen.findByRole('menu')
    await userEvent.keyboard('{Escape}')

    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
  })
})
