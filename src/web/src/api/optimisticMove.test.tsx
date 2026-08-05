import { afterEach, describe, expect, it, vi } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { queryKeys, useMoveItem } from './hooks'
import type { ItineraryItemResponse } from './api-types'

const TRIP = 't1'

function item(overrides: Partial<ItineraryItemResponse> = {}): ItineraryItemResponse {
  return {
    id: 'i1',
    tripId: TRIP,
    placeId: 'p1',
    placeName: 'Thác Dải Yếm',
    placeCategory: 'Sight',
    estimatedDurationMinutes: 90,
    lat: 20.8,
    lng: 104.6,
    date: '2026-03-01',
    startTime: '09:00:00',
    note: null,
    actualCost: null,
    estimatedCost: 50_000,
    createdAt: '2026-03-01T00:00:00+00:00',
    updatedAt: '2026-03-01T00:00:00+00:00',
    updatedByMemberId: 'm1',
    ...overrides,
  }
}

/** A client seeded with one item already on 1 March. */
function setup() {
  const client = new QueryClient({
    // gcTime must not be 0 here: the cache is seeded before the hook mounts,
    // and a zero collection window discards it before there is an observer.
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  client.setQueryData<ItineraryItemResponse[]>(queryKeys.itinerary(TRIP), [item()])

  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  )

  const dateOf = () =>
    client.getQueryData<ItineraryItemResponse[]>(queryKeys.itinerary(TRIP))?.[0]?.date

  return { client, wrapper, dateOf }
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('useMoveItem — optimistic drag (spec §7.15)', () => {
  it('moves the item immediately, before the server answers', async () => {
    // A drag that visibly snaps back for a round trip reads as broken.
    let release: (() => void) | undefined
    vi.stubGlobal(
      'fetch',
      vi.fn().mockImplementation(
        () =>
          new Promise((resolve) => {
            release = () =>
              resolve({
                ok: true,
                status: 200,
                json: async () => item({ date: '2026-03-03' }),
              } as Response)
          }),
      ),
    )

    const { wrapper, dateOf } = setup()
    const { result } = renderHook(() => useMoveItem(TRIP), { wrapper })

    result.current.mutate({ itemId: 'i1', body: { date: '2026-03-03' } })

    await waitFor(() => expect(dateOf()).toBe('2026-03-03'))
    expect(release).toBeDefined()

    release?.()
    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(dateOf()).toBe('2026-03-03')
  })

  it('rolls the item back to its original day when the drop is refused', async () => {
    // The exact case spec §7.15 names: dropping onto a day that already has
    // this place answers 409, and the card must return to where it was.
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        ok: false,
        status: 409,
        json: async () => ({
          status: 409,
          code: 'DUPLICATE_PLACE_ON_DATE',
          detail: 'already scheduled',
        }),
      } as Response),
    )

    const { wrapper, dateOf } = setup()
    const { result } = renderHook(() => useMoveItem(TRIP), { wrapper })

    result.current.mutate({ itemId: 'i1', body: { date: '2026-03-02' } })

    await waitFor(() => expect(result.current.isError).toBe(true))
    // The refused move must not stick.
    expect(dateOf()).toBe('2026-03-01')
  })

  it('rolls back a failed time change too', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        ok: false,
        status: 422,
        json: async () => ({ status: 422, code: 'VALIDATION_FAILED' }),
      } as Response),
    )

    const { wrapper, client } = setup()
    const { result } = renderHook(() => useMoveItem(TRIP), { wrapper })

    result.current.mutate({ itemId: 'i1', body: { startTime: '25:00:00' } })

    await waitFor(() => expect(result.current.isError).toBe(true))
    expect(
      client.getQueryData<ItineraryItemResponse[]>(queryKeys.itinerary(TRIP))?.[0]?.startTime,
    ).toBe('09:00:00')
  })

  it('clears a start time optimistically when it is set to null', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => item({ startTime: null }),
      } as Response),
    )

    const { wrapper, client } = setup()
    const { result } = renderHook(() => useMoveItem(TRIP), { wrapper })

    result.current.mutate({ itemId: 'i1', body: { startTime: null } })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(
      client.getQueryData<ItineraryItemResponse[]>(queryKeys.itinerary(TRIP))?.[0]?.startTime,
    ).toBeNull()
  })
})
