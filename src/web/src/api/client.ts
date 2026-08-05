import type {
  ActivityResponse,
  AppConfigResponse,
  BalanceResponse,
  CreateExpenseRequest,
  CreateItineraryItemRequest,
  CreatePlaceRequest,
  CreateTripRequest,
  ExpenseResponse,
  FeasibilityResponse,
  GeocodeResultResponse,
  ItineraryItemResponse,
  JoinTripRequest,
  PlaceResponse,
  PlaceStatus,
  ProblemDetails,
  SuggestionGroupResponse,
  SessionEnvelope,
  TripResponse,
  TripSessionResponse,
  TripSummaryResponse,
  UpdateItineraryItemRequest,
  UpdatePlaceRequest,
  UpdateTripRequest,
  WeatherResponse,
} from './api-types'
import { fieldErrorText, problemText } from './labels'

/**
 * A failed request, carrying the server's stable `code` so callers branch on
 * that rather than on a status number or a message string.
 */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly problem: ProblemDetails,
  ) {
    super(problem.detail ?? problem.title ?? `Request failed with ${status}`)
    this.name = 'ApiError'
  }

  get code(): string {
    return this.problem.code
  }

  /**
   * Field errors from a 422, keyed by field name for form display, translated
   * where the code is one we know. The server's English message is the fallback
   * — see fieldErrorText.
   */
  fieldErrors(): Record<string, string> {
    const result: Record<string, string> = {}
    for (const error of this.problem.errors ?? []) {
      result[error.field] = fieldErrorText(error.field, error.code, error.message)
    }
    return result
  }

  /** The whole-request failure, in Vietnamese where the code is known. */
  get text(): string {
    return problemText(this.code, this.problem.detail ?? this.message)
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    // The session is an HttpOnly cookie, so it must ride along on every call.
    credentials: 'same-origin',
    headers: {
      'Content-Type': 'application/json',
      ...init?.headers,
    },
  })

  if (!response.ok) {
    throw new ApiError(response.status, await readProblem(response))
  }

  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}

async function readProblem(response: Response): Promise<ProblemDetails> {
  try {
    const body = (await response.json()) as ProblemDetails
    // A proxy or gateway could return a non-conforming body; keep the shape.
    return body?.code ? body : { status: response.status, code: 'UNKNOWN', detail: body?.detail }
  } catch {
    return { status: response.status, code: 'UNKNOWN' }
  }
}

export const api = {
  /** Deployment facts the start screen needs. Never carries the code itself. */
  config: () => request<AppConfigResponse>('/config'),

  createTrip: (body: CreateTripRequest) =>
    request<TripSessionResponse>('/trips', { method: 'POST', body: JSON.stringify(body) }),

  joinTrip: (body: JoinTripRequest) =>
    request<TripSessionResponse>('/trips/join', { method: 'POST', body: JSON.stringify(body) }),

  getSession: () => request<SessionEnvelope>('/session'),

  /** Every trip this browser holds, for the switcher. */
  myTrips: () => request<TripSummaryResponse[]>('/trips/mine'),

  /**
   * Removes a trip from this device. Not a deletion and not leaving the trip —
   * the invite code still works, and everyone else is unaffected.
   */
  forgetTrip: (tripId: string) =>
    request<void>(`/session/trips/${tripId}`, { method: 'DELETE' }),

  getTrip: (tripId: string) => request<TripResponse>(`/trips/${tripId}`),

  listPlaces: (tripId: string) => request<PlaceResponse[]>(`/trips/${tripId}/places`),

  searchPlaces: (tripId: string, query: string, signal?: AbortSignal) =>
    request<GeocodeResultResponse[]>(
      `/trips/${tripId}/places/search?q=${encodeURIComponent(query)}`,
      { signal },
    ),

  resolveLink: (tripId: string, url: string, signal?: AbortSignal) =>
    request<GeocodeResultResponse>(`/trips/${tripId}/places/resolve-link`, {
      method: 'POST',
      body: JSON.stringify({ url }),
      signal,
    }),

  createPlace: (tripId: string, body: CreatePlaceRequest) =>
    request<PlaceResponse>(`/trips/${tripId}/places`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  /**
   * Only the fields you send are touched — the server distinguishes "absent"
   * from an explicit null, so renaming a trip cannot clear its dates.
   */
  updateTrip: (tripId: string, body: UpdateTripRequest) =>
    request<TripResponse>(`/trips/${tripId}`, {
      method: 'PATCH',
      body: JSON.stringify(body),
    }),

  updatePlace: (tripId: string, placeId: string, body: UpdatePlaceRequest) =>
    request<PlaceResponse>(`/trips/${tripId}/places/${placeId}`, {
      method: 'PATCH',
      body: JSON.stringify(body),
    }),

  listItinerary: (tripId: string) =>
    request<ItineraryItemResponse[]>(`/trips/${tripId}/itinerary`),

  createItineraryItem: (tripId: string, body: CreateItineraryItemRequest) =>
    request<ItineraryItemResponse>(`/trips/${tripId}/itinerary`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  updateItineraryItem: (tripId: string, itemId: string, body: UpdateItineraryItemRequest) =>
    request<ItineraryItemResponse>(`/trips/${tripId}/itinerary/${itemId}`, {
      method: 'PATCH',
      body: JSON.stringify(body),
    }),

  deleteItineraryItem: (tripId: string, itemId: string) =>
    request<ItineraryItemResponse>(`/trips/${tripId}/itinerary/${itemId}`, { method: 'DELETE' }),

  /** 204 means "nothing to forecast" (spec §5.5), which surfaces as null. */
  weather: (tripId: string) => request<WeatherResponse | null>(`/trips/${tripId}/weather`),

  activity: (tripId: string, limit = 40) =>
    request<ActivityResponse[]>(`/trips/${tripId}/activity?limit=${limit}`),

  listExpenses: (tripId: string) => request<ExpenseResponse[]>(`/trips/${tripId}/expenses`),

  createExpense: (tripId: string, body: CreateExpenseRequest) =>
    request<ExpenseResponse>(`/trips/${tripId}/expenses`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  deleteExpense: (tripId: string, expenseId: string) =>
    request<ExpenseResponse>(`/trips/${tripId}/expenses/${expenseId}`, { method: 'DELETE' }),

  balance: (tripId: string) => request<BalanceResponse>(`/trips/${tripId}/balance`),

  feasibility: (tripId: string, date: string) =>
    request<FeasibilityResponse>(`/trips/${tripId}/itinerary/feasibility?date=${date}`),

  suggestions: (tripId: string, date: string) =>
    request<SuggestionGroupResponse[]>(`/trips/${tripId}/suggestions?date=${date}`),

  likePlace: (tripId: string, placeId: string) =>
    request<PlaceResponse>(`/trips/${tripId}/places/${placeId}/like`, { method: 'POST' }),

  unlikePlace: (tripId: string, placeId: string) =>
    request<PlaceResponse>(`/trips/${tripId}/places/${placeId}/like`, { method: 'DELETE' }),

  changePlaceStatus: (tripId: string, placeId: string, status: PlaceStatus, skipReason?: string) =>
    request<PlaceResponse>(`/trips/${tripId}/places/${placeId}/status`, {
      method: 'POST',
      body: JSON.stringify({ status, skipReason: skipReason ?? null }),
    }),

  deletePlace: (tripId: string, placeId: string, force = false) =>
    request<PlaceResponse>(
      `/trips/${tripId}/places/${placeId}${force ? '?force=true' : ''}`,
      { method: 'DELETE' },
    ),
}
