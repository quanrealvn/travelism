import type { IsoDate, TripSummaryResponse } from '../api/api-types'

/**
 * The trip to open when nobody has chosen one: the one happening now, else the
 * next one coming up, else the most recent one that has been.
 *
 * "Most recently added" — which is the order the session cookie keeps — is the
 * wrong answer. Entering last year's trip into the app to keep its wishlist
 * would otherwise land you in it every time you opened the app.
 *
 * Dates compare as strings because they are ISO-8601: lexicographic order and
 * chronological order are the same, with no parsing and no time zone involved.
 */
export function mostRelevantTrip(
  trips: TripSummaryResponse[],
  today: IsoDate,
): string | null {
  // Underway: the one ending soonest, since that is the one being lived.
  const inProgress = trips
    .filter((trip) => trip.startDate <= today && today <= trip.endDate)
    .sort((a, b) => a.endDate.localeCompare(b.endDate))
  if (inProgress[0]) {
    return inProgress[0].id
  }

  const upcoming = trips
    .filter((trip) => trip.startDate > today)
    .sort((a, b) => a.startDate.localeCompare(b.startDate))
  if (upcoming[0]) {
    return upcoming[0].id
  }

  const past = [...trips].sort((a, b) => b.endDate.localeCompare(a.endDate))
  return past[0]?.id ?? null
}
