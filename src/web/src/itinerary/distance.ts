/**
 * Straight-line distance between two points on the plan.
 *
 * Deliberately not road distance. The server already knows how to ask a routing
 * service for real travel time — that is what feasibility uses — and this is
 * something else: a cheap, instant sense of how far apart the day's stops are,
 * computed on the client with no request and no cache.
 *
 * Presented as such. A number labelled "km" that is 40% short of what the road
 * actually is would be worse than no number, so the UI shows it as "cách ~X km"
 * and never as a journey.
 */

const EARTH_RADIUS_KM = 6371

export interface Point {
  lat: number
  lng: number
}

const toRadians = (degrees: number) => (degrees * Math.PI) / 180

/** Haversine. Accurate to a few metres at the scale of a city or a province. */
export function distanceKm(from: Point, to: Point): number {
  const dLat = toRadians(to.lat - from.lat)
  const dLng = toRadians(to.lng - from.lng)

  const a =
    Math.sin(dLat / 2) ** 2 +
    Math.cos(toRadians(from.lat)) * Math.cos(toRadians(to.lat)) * Math.sin(dLng / 2) ** 2

  return 2 * EARTH_RADIUS_KM * Math.asin(Math.min(1, Math.sqrt(a)))
}

/**
 * How far apart, in words.
 *
 * Under a kilometre reads in metres, because "0,3 km" is a way of saying "three
 * hundred metres" that nobody uses. Above it, one decimal until 10km and none
 * after — the precision stops being meaningful long before the number does.
 */
export function formatDistance(km: number): string {
  if (km < 1) {
    return `${Math.round(km * 100) * 10} m`
  }

  if (km < 10) {
    return `${km.toFixed(1).replace('.', ',')} km`
  }

  return `${Math.round(km)} km`
}
