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

/**
 * One row in the trip switcher. Smaller than {@link TripResponse} on purpose:
 * a browser may hold twenty, and the invite code is not sent for a trip nobody
 * has opened.
 */
export interface TripSummaryResponse {
  id: string
  name: string
  destination: string
  startDate: IsoDate
  endDate: IsoDate
  currency: string
  currencyExponent: number
  budgetAmount: number | null
  status: TripStatus
  memberCount: number
  placeCount: number
  updatedAt: string
}

export interface TripMembership {
  tripId: string
  memberId: string
}

/**
 * Who this browser is. `tripId` is the most recently created or joined trip —
 * the one to open when nothing else has been chosen — and `memberships` is
 * every trip it holds.
 */
export interface SessionEnvelope {
  tripId: string
  memberId: string
  memberships: TripMembership[]
}

export interface PlaceReferenceResponse {
  id: string
  url: string
  label: string | null
  /** The label if set, otherwise the host — never a raw 200-character URL. */
  displayName: string
}

export interface PlaceReferenceRequest {
  url: string
  label?: string | null
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
  description: string | null
  references: PlaceReferenceResponse[]
  status: PlaceStatus
  skipReason: string | null
  isDeleted: boolean
  likedByMemberIds: string[]
  createdAt: string
  updatedAt: string
  updatedByMemberId: string
}

export interface ItineraryItemResponse {
  id: string
  tripId: string
  placeId: string
  placeName: string
  placeCategory: PlaceCategory
  estimatedDurationMinutes: number
  lat: number
  lng: number
  date: IsoDate
  /** `HH:mm:ss`, or null for "sometime that day". */
  startTime: string | null
  note: string | null
  actualCost: number | null
  estimatedCost: number | null
  createdAt: string
  updatedAt: string
  updatedByMemberId: string
}

export interface DailyWeatherResponse {
  date: IsoDate
  maxTempC: number | null
  minTempC: number | null
  precipitationMm: number | null
  /** WMO weather code; see weatherText. */
  weatherCode: number | null
}

export interface WeatherResponse {
  lat: number
  lng: number
  timeZoneId: string
  /** True when served from cache during an upstream outage (spec §5.5). */
  stale: boolean
  days: DailyWeatherResponse[]
}

export interface ActivityResponse {
  id: string
  memberId: string
  action: string
  entityType: string
  entityId: string
  summaryText: string
  at: string
}

export type ExpenseCategory = 'Transport' | 'Lodging' | 'Food' | 'Tickets' | 'Other'
export type SplitType = 'Equal' | 'Custom'

export interface ExpenseShareResponse {
  memberId: string
  /** Integer minor units. */
  shareAmount: number
}

export interface ExpenseResponse {
  id: string
  tripId: string
  title: string
  amount: number
  currency: string
  paidByMemberId: string
  date: IsoDate
  category: ExpenseCategory
  splitType: SplitType
  shares: ExpenseShareResponse[]
  createdAt: string
  updatedAt: string
  updatedByMemberId: string
}

export interface MemberBalanceResponse {
  memberId: string
  paid: number
  owed: number
  /** Positive: the trip owes them. Negative: they owe the trip. */
  net: number
}

export interface TransferResponse {
  fromMemberId: string
  toMemberId: string
  amount: number
}

export interface BalanceResponse {
  balances: MemberBalanceResponse[]
  transfers: TransferResponse[]
  totalSpent: number
  currency: string
  currencyExponent: number
}

export interface CreateExpenseRequest {
  title: string
  amount: number
  paidByMemberId: string
  date: IsoDate
  category: ExpenseCategory
  splitType: SplitType
  shares?: { memberId: string; shareAmount: number }[]
}

export const ALL_EXPENSE_CATEGORIES: readonly ExpenseCategory[] = [
  'Transport',
  'Lodging',
  'Food',
  'Tickets',
  'Other',
]

export type FeasibilityLevel = 'error' | 'warning' | 'info'

export type FeasibilityCode =
  | 'OVERLAP'
  | 'INSUFFICIENT_TRAVEL_TIME'
  | 'IDLE_GAP'
  | 'TIMESLOT_MISMATCH'
  | 'UNSCHEDULED_TIME'
  | 'CROSSES_MIDNIGHT'

export interface FeasibilityFindingResponse {
  itineraryItemId: string
  level: FeasibilityLevel
  code: FeasibilityCode
  /** Code-specific detail: gapMinutes, travelMinutes, source, and so on. */
  data: Record<string, unknown>
}

export interface FeasibilityResponse {
  items: FeasibilityFindingResponse[]
}

export interface SuggestionResponse {
  placeId: string
  name: string
  category: PlaceCategory
  estimatedCost: number | null
}

export interface SuggestionGroupResponse {
  slot: TimeSlot
  places: SuggestionResponse[]
}

export interface CreateItineraryItemRequest {
  placeId: string
  date: IsoDate
  startTime?: string | null
  note?: string | null
  actualCost?: number | null
}

export interface UpdateItineraryItemRequest {
  date?: IsoDate
  startTime?: string | null
  note?: string | null
  actualCost?: number | null
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
  description?: string | null
  references?: PlaceReferenceRequest[]
}

export type UpdatePlaceRequest = Partial<CreatePlaceRequest>

/**
 * Every field is optional and each is patched independently: the server tells
 * "absent" apart from an explicit null, so sending `{ name }` renames the trip
 * without touching its dates, budget, or destination.
 */
export interface UpdateTripRequest {
  name?: string
  destination?: string
  startDate?: IsoDate | null
  endDate?: IsoDate | null
  timeZoneId?: string
  budgetAmount?: number | null
  status?: TripStatus
}

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
