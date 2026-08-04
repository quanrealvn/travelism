/**
 * Hand-maintained mirror of the server DTOs (spec §8). One file, so a contract
 * change has exactly one place to land on the client.
 *
 * Money is `number` holding integer MINOR units, matching the server's `long`.
 * Never do arithmetic that could produce a fraction on these — format at the
 * edge with `formatMoney`.
 */

export type TripStatus = 'Planning' | 'Ongoing' | 'Completed'
export type MemberRole = 'Owner' | 'Editor'
export type PlaceCategory = 'Food' | 'Sight' | 'Photo' | 'Rest' | 'Other'
export type TimeSlot = 'Morning' | 'Noon' | 'Afternoon' | 'Evening'
export type PlaceStatus = 'Idea' | 'Shortlist' | 'Confirmed' | 'Visited' | 'Skipped'

/** ISO calendar date, `YYYY-MM-DD`. Never a Date object — see spec §7.10. */
export type IsoDate = string

export interface MemberResponse {
  id: string
  displayName: string
  role: MemberRole
  createdAt: string
}

export interface TripResponse {
  id: string
  name: string
  destination: string
  startDate: IsoDate
  endDate: IsoDate
  timeZoneId: string
  currency: string
  currencyExponent: number
  budgetAmount: number | null
  status: TripStatus
  inviteCode: string
  members: MemberResponse[]
  createdAt: string
  updatedAt: string
  updatedByMemberId: string
}

export interface PlaceResponse {
  id: string
  tripId: string
  name: string
  lat: number
  lng: number
  category: PlaceCategory
  timeSlots: TimeSlot[]
  estimatedDurationMinutes: number
  estimatedCost: number | null
  openHoursText: string | null
  status: PlaceStatus
  skipReason: string | null
  isDeleted: boolean
  likedByMemberIds: string[]
  createdAt: string
  updatedAt: string
  updatedByMemberId: string
}

/** A candidate location returned by the place-name search. */
export interface GeocodeResultResponse {
  /** Short label, prefilled into the place name field. */
  name: string
  /** Full address, so two places with the same name are distinguishable. */
  displayName: string
  lat: number
  lng: number
  /** Upstream classification such as "restaurant"; may be null. */
  kind: string | null
  /**
   * Straight-line km from the trip's existing places, or null when the trip has
   * none. A free-text search can match a plausible-looking name on another
   * continent, so this is what tells the two apart.
   */
  distanceKm: number | null
}

export interface SessionResponse {
  tripId: string
  memberId: string
  displayName: string
  role: MemberRole
}

export interface TripSessionResponse {
  trip: TripResponse
  session: SessionResponse
}

export interface CreateTripRequest {
  name: string
  destination: string
  startDate: IsoDate
  endDate: IsoDate
  timeZoneId?: string
  currency?: string
  budgetAmount?: number | null
  ownerDisplayName: string
}

export interface JoinTripRequest {
  inviteCode: string
  displayName: string
}

export interface CreatePlaceRequest {
  name: string
  lat: number
  lng: number
  category: PlaceCategory
  timeSlots: TimeSlot[]
  estimatedDurationMinutes: number
  estimatedCost?: number | null
  openHoursText?: string | null
}

export type UpdatePlaceRequest = Partial<CreatePlaceRequest>

/** RFC 7807 body with the stable `code` extension the server always sends. */
export interface ProblemDetails {
  type?: string
  title?: string
  status: number
  detail?: string
  code: string
  errors?: ProblemFieldError[]
  /** Present on PLACE_IN_USE: the dates the place is scheduled on. */
  dates?: IsoDate[]
  /** Present on ITEMS_OUT_OF_RANGE. */
  itemIds?: string[]
}

export interface ProblemFieldError {
  field: string
  code: string
  message: string
}

export const ALL_TIME_SLOTS: readonly TimeSlot[] = ['Morning', 'Noon', 'Afternoon', 'Evening']

export const ALL_CATEGORIES: readonly PlaceCategory[] = [
  'Food',
  'Sight',
  'Photo',
  'Rest',
  'Other',
]
