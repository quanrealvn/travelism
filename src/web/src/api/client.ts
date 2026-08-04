import type {
  CreatePlaceRequest,
  CreateTripRequest,
  GeocodeResultResponse,
  JoinTripRequest,
  PlaceResponse,
  ProblemDetails,
  TripResponse,
  TripSessionResponse,
  UpdatePlaceRequest,
} from './api-types'

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

  /** Field errors from a 422, keyed by field name for form display. */
  fieldErrors(): Record<string, string> {
    const result: Record<string, string> = {}
    for (const error of this.problem.errors ?? []) {
      result[error.field] = error.message
    }
    return result
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
  createTrip: (body: CreateTripRequest) =>
    request<TripSessionResponse>('/trips', { method: 'POST', body: JSON.stringify(body) }),

  joinTrip: (body: JoinTripRequest) =>
    request<TripSessionResponse>('/trips/join', { method: 'POST', body: JSON.stringify(body) }),

  getSession: () => request<{ tripId: string; memberId: string }>('/session'),

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

  updatePlace: (tripId: string, placeId: string, body: UpdatePlaceRequest) =>
    request<PlaceResponse>(`/trips/${tripId}/places/${placeId}`, {
      method: 'PATCH',
      body: JSON.stringify(body),
    }),

  deletePlace: (tripId: string, placeId: string, force = false) =>
    request<PlaceResponse>(
      `/trips/${tripId}/places/${placeId}${force ? '?force=true' : ''}`,
      { method: 'DELETE' },
    ),
}
