import { describe, expect, it } from 'vitest'
import { mostRelevantTrip } from './defaultTrip'
import type { TripSummaryResponse } from '../api/api-types'

const TODAY = '2026-08-05'

function trip(id: string, startDate: string, endDate: string): TripSummaryResponse {
  return {
    id,
    name: id,
    destination: 'Somewhere',
    startDate,
    endDate,
    currency: 'VND',
    currencyExponent: 0,
    budgetAmount: null,
    status: 'Planning',
    memberCount: 1,
    placeCount: 0,
    updatedAt: '2026-01-01T00:00:00+00:00',
  }
}

describe('mostRelevantTrip', () => {
  it('opens the trip that is happening right now', () => {
    const chosen = mostRelevantTrip(
      [
        trip('past', '2020-01-01', '2020-01-05'),
        trip('now', '2026-08-01', '2026-08-09'),
        trip('later', '2026-12-01', '2026-12-05'),
      ],
      TODAY,
    )

    expect(chosen).toBe('now')
  })

  it('counts the first and last day as being on the trip', () => {
    expect(mostRelevantTrip([trip('a', TODAY, '2026-08-09')], TODAY)).toBe('a')
    expect(mostRelevantTrip([trip('a', '2026-08-01', TODAY)], TODAY)).toBe('a')
  })

  it('picks the one ending soonest when two overlap', () => {
    // Two trips at once is unusual but possible — a long one and a side trip.
    // The one about to end is the one being lived.
    const chosen = mostRelevantTrip(
      [trip('long', '2026-07-01', '2026-09-01'), trip('short', '2026-08-04', '2026-08-06')],
      TODAY,
    )

    expect(chosen).toBe('short')
  })

  it('falls to the next trip coming up when none is underway', () => {
    const chosen = mostRelevantTrip(
      [
        trip('far', '2027-01-01', '2027-01-05'),
        trip('soon', '2026-08-20', '2026-08-25'),
        trip('past', '2024-01-01', '2024-01-05'),
      ],
      TODAY,
    )

    expect(chosen).toBe('soon')
  })

  it('falls to the most recent past trip when nothing is coming', () => {
    // Somebody who has stopped planning still opens the app to look up what
    // last time cost.
    const chosen = mostRelevantTrip(
      [trip('older', '2020-01-01', '2020-01-05'), trip('newer', '2024-06-01', '2024-06-05')],
      TODAY,
    )

    expect(chosen).toBe('newer')
  })

  it('does not open last year’s trip just because it was added last', () => {
    // The session cookie orders by when a trip was added to the device, which
    // is exactly the wrong order for this question.
    const chosen = mostRelevantTrip(
      [trip('upcoming', '2026-09-01', '2026-09-05'), trip('added-last', '2024-10-04', '2024-10-08')],
      TODAY,
    )

    expect(chosen).toBe('upcoming')
  })

  it('has no answer when there are no trips', () => {
    expect(mostRelevantTrip([], TODAY)).toBeNull()
  })
})
