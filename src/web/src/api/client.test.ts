import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, api } from './client'
import type { ProblemDetails } from './api-types'

function mockFetch(status: number, body: unknown, ok = status < 400) {
  const spy = vi.fn().mockResolvedValue({
    ok,
    status,
    json: async () => body,
  } as Response)

  vi.stubGlobal('fetch', spy)
  return spy
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('api client', () => {
  it('sends credentials so the HttpOnly session cookie rides along', async () => {
    const fetchSpy = mockFetch(200, [])

    await api.listPlaces('trip-1')

    expect(fetchSpy).toHaveBeenCalledWith(
      '/trips/trip-1/places',
      expect.objectContaining({ credentials: 'same-origin' }),
    )
  })

  it('appends force=true only when asked', async () => {
    const fetchSpy = mockFetch(200, {})

    await api.deletePlace('t', 'p')
    expect(fetchSpy).toHaveBeenCalledWith('/trips/t/places/p', expect.anything())

    await api.deletePlace('t', 'p', true)
    expect(fetchSpy).toHaveBeenCalledWith('/trips/t/places/p?force=true', expect.anything())
  })

  it('throws an ApiError carrying the server code', async () => {
    const problem: ProblemDetails = {
      status: 409,
      code: 'PLACE_IN_USE',
      detail: 'Scheduled on 2 days.',
      dates: ['2026-03-02', '2026-03-05'],
    }
    mockFetch(409, problem, false)

    const error = await api.deletePlace('t', 'p').catch((caught: unknown) => caught)

    expect(error).toBeInstanceOf(ApiError)
    const apiError = error as ApiError
    expect(apiError.code).toBe('PLACE_IN_USE')
    expect(apiError.status).toBe(409)
    expect(apiError.problem.dates).toEqual(['2026-03-02', '2026-03-05'])
  })

  it('exposes 422 field errors keyed by field for form display', async () => {
    mockFetch(
      422,
      {
        status: 422,
        code: 'VALIDATION_FAILED',
        errors: [
          { field: 'name', code: 'REQUIRED', message: 'Bắt buộc' },
          { field: 'lat', code: 'OUT_OF_RANGE', message: 'Ngoài phạm vi' },
        ],
      },
      false,
    )

    const error = (await api
      .createPlace('t', {
        name: '',
        lat: 999,
        lng: 0,
        category: 'Food',
        timeSlots: ['Morning'],
        estimatedDurationMinutes: 30,
      })
      .catch((caught: unknown) => caught)) as ApiError

    expect(error.fieldErrors()).toEqual({ name: 'Bắt buộc', lat: 'Ngoài phạm vi' })
  })

  it('survives an error response that is not ProblemDetails', async () => {
    // A proxy or load balancer can return HTML; the client must not crash on it.
    const spy = vi.fn().mockResolvedValue({
      ok: false,
      status: 502,
      json: async () => {
        throw new SyntaxError('Unexpected token <')
      },
    } as unknown as Response)
    vi.stubGlobal('fetch', spy)

    const error = (await api.getTrip('t').catch((caught: unknown) => caught)) as ApiError

    expect(error).toBeInstanceOf(ApiError)
    expect(error.status).toBe(502)
    expect(error.code).toBe('UNKNOWN')
  })
})
