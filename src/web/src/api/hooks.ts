import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'
import type {
  CreateExpenseRequest,
  CreateItineraryItemRequest,
  CreatePlaceRequest,
  ItineraryItemResponse,
  PlaceResponse,
  PlaceStatus,
  UpdateItineraryItemRequest,
  UpdatePlaceRequest,
} from './api-types'

/** All server state flows through TanStack Query (spec §8). */
export const queryKeys = {
  session: ['session'] as const,
  trip: (tripId: string) => ['trip', tripId] as const,
  places: (tripId: string) => ['places', tripId] as const,
  placeSearch: (tripId: string, query: string) => ['place-search', tripId, query] as const,
  placeLink: (tripId: string, url: string) => ['place-link', tripId, url] as const,
  itinerary: (tripId: string) => ['itinerary', tripId] as const,
  suggestions: (tripId: string, date: string) => ['suggestions', tripId, date] as const,
  feasibility: (tripId: string, date: string) => ['feasibility', tripId, date] as const,
  expenses: (tripId: string) => ['expenses', tripId] as const,
  balance: (tripId: string) => ['balance', tripId] as const,
  weather: (tripId: string) => ['weather', tripId] as const,
  activity: (tripId: string) => ['activity', tripId] as const,
}

export function useWeather(tripId: string) {
  return useQuery({
    queryKey: queryKeys.weather(tripId),
    queryFn: () => api.weather(tripId),
    // The server caches for three hours; there is no value in asking more often.
    staleTime: 30 * 60_000,
    retry: false,
  })
}

export function useActivity(tripId: string, enabled: boolean) {
  return useQuery({
    queryKey: queryKeys.activity(tripId),
    queryFn: () => api.activity(tripId),
    enabled,
  })
}

export function useExpenses(tripId: string) {
  return useQuery({
    queryKey: queryKeys.expenses(tripId),
    queryFn: () => api.listExpenses(tripId),
  })
}

export function useBalance(tripId: string) {
  return useQuery({
    queryKey: queryKeys.balance(tripId),
    queryFn: () => api.balance(tripId),
  })
}

export function useCreateExpense(tripId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (body: CreateExpenseRequest) => api.createExpense(tripId, body),
    onSuccess: () => {
      // The balance is derived from every expense, so it is refetched rather
      // than patched — recomputing settlement on the client would duplicate
      // logic the server already owns.
      void queryClient.invalidateQueries({ queryKey: queryKeys.expenses(tripId) })
      void queryClient.invalidateQueries({ queryKey: queryKeys.balance(tripId) })
    },
  })
}

export function useDeleteExpense(tripId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (expenseId: string) => api.deleteExpense(tripId, expenseId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.expenses(tripId) })
      void queryClient.invalidateQueries({ queryKey: queryKeys.balance(tripId) })
    },
  })
}

/**
 * Feasibility for one day. Re-runs whenever the itinerary changes, because
 * moving a single item can make — or break — the rest of the day.
 */
export function useFeasibility(tripId: string, date: string | null) {
  return useQuery({
    queryKey: queryKeys.feasibility(tripId, date ?? ''),
    queryFn: () => api.feasibility(tripId, date!),
    enabled: Boolean(date),
    // Short: the first read may hit the routing service, later ones are cached
    // server-side, and a stale verdict on a plan being edited is worse than a
    // refetch.
    staleTime: 5_000,
    retry: false,
  })
}

export function useItinerary(tripId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.itinerary(tripId ?? ''),
    queryFn: () => api.listItinerary(tripId!),
    enabled: Boolean(tripId),
  })
}

export function useSuggestions(tripId: string, date: string | null) {
  return useQuery({
    queryKey: queryKeys.suggestions(tripId, date ?? ''),
    queryFn: () => api.suggestions(tripId, date!),
    enabled: Boolean(date),
    staleTime: 30_000,
  })
}

export function useScheduleItem(tripId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (body: CreateItineraryItemRequest) => api.createItineraryItem(tripId, body),
    onSuccess: (created) => {
      queryClient.setQueryData<ItineraryItemResponse[]>(queryKeys.itinerary(tripId), (current) =>
        current ? [...current, created] : [created],
      )
      // Scheduling removes the place from that day's suggestions.
      void queryClient.invalidateQueries({ queryKey: ['suggestions', tripId] })
      // One move can make — or break — the rest of the day, so the whole day
      // is re-checked rather than just the item that moved.
      void queryClient.invalidateQueries({ queryKey: ['feasibility', tripId] })
    },
  })
}

/**
 * Moving an item between days. Optimistic, because a drag that visibly snaps
 * back after a round trip feels broken — and spec §7.15 requires the rollback
 * when the server refuses the drop.
 */
export function useMoveItem(tripId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ itemId, body }: { itemId: string; body: UpdateItineraryItemRequest }) =>
      api.updateItineraryItem(tripId, itemId, body),

    onMutate: async ({ itemId, body }) => {
      await queryClient.cancelQueries({ queryKey: queryKeys.itinerary(tripId) })
      const previous = queryClient.getQueryData<ItineraryItemResponse[]>(queryKeys.itinerary(tripId))

      queryClient.setQueryData<ItineraryItemResponse[]>(queryKeys.itinerary(tripId), (current) =>
        current?.map((item) =>
          item.id === itemId
            ? {
                ...item,
                date: body.date ?? item.date,
                startTime: body.startTime === undefined ? item.startTime : body.startTime,
              }
            : item,
        ),
      )

      return { previous }
    },

    onError: (_error, _variables, context) => {
      // Spec §7.15: put the item back exactly where it was.
      if (context?.previous) {
        queryClient.setQueryData(queryKeys.itinerary(tripId), context.previous)
      }
    },

    onSuccess: (updated) => {
      queryClient.setQueryData<ItineraryItemResponse[]>(queryKeys.itinerary(tripId), (current) =>
        current?.map((item) => (item.id === updated.id ? updated : item)),
      )
    },

    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: ['suggestions', tripId] })
      // One move can make — or break — the rest of the day, so the whole day
      // is re-checked rather than just the item that moved.
      void queryClient.invalidateQueries({ queryKey: ['feasibility', tripId] })
    },
  })
}

export function useRemoveItem(tripId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (itemId: string) => api.deleteItineraryItem(tripId, itemId),
    onSuccess: (removed) => {
      queryClient.setQueryData<ItineraryItemResponse[]>(queryKeys.itinerary(tripId), (current) =>
        current?.filter((item) => item.id !== removed.id),
      )
      void queryClient.invalidateQueries({ queryKey: ['suggestions', tripId] })
      // One move can make — or break — the rest of the day, so the whole day
      // is re-checked rather than just the item that moved.
      void queryClient.invalidateQueries({ queryKey: ['feasibility', tripId] })
    },
  })
}

/**
 * Recognises input that is a location rather than a name to search for: a
 * pasted map link, or a coordinate pair copied out of one.
 *
 * mirror of server rule — PlaceLink.Parse is authoritative and re-checks
 * everything. This only decides which endpoint to call.
 */
const URL_PREFIX = /^https?:\/\//i
const COORDINATE_PAIR = /^\s*-?\d{1,3}(\.\d+)?\s*[,\s]\s*-?\d{1,3}(\.\d+)?\s*$/

export function isLocationPaste(input: string): boolean {
  return URL_PREFIX.test(input.trim()) || COORDINATE_PAIR.test(input)
}

/** Resolves a pasted map link into a single location. */
export function usePlaceLink(tripId: string, url: string) {
  return useQuery({
    queryKey: queryKeys.placeLink(tripId, url),
    queryFn: ({ signal }) => api.resolveLink(tripId, url, signal),
    enabled: url.trim().length > 0 && isLocationPaste(url),
    staleTime: 10 * 60_000,
    retry: false,
  })
}

/**
 * Place-name lookup for the add-place form. The caller passes an
 * already-debounced query; results are cached so retyping or reopening the
 * form does not re-hit the shared upstream geocoder.
 */
export function usePlaceSearch(tripId: string, query: string) {
  return useQuery({
    queryKey: queryKeys.placeSearch(tripId, query),
    queryFn: ({ signal }) => api.searchPlaces(tripId, query, signal),
    // Mirrors the server's minimum query length so a one-character keystroke
    // never leaves the browser.
    enabled: query.trim().length >= 2,
    staleTime: 10 * 60_000,
    retry: false,
  })
}

export function useSession() {
  return useQuery({
    queryKey: queryKeys.session,
    queryFn: api.getSession,
    retry: false,
  })
}

export function useTrip(tripId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.trip(tripId ?? ''),
    queryFn: () => api.getTrip(tripId!),
    enabled: Boolean(tripId),
  })
}

export function usePlaces(tripId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.places(tripId ?? ''),
    queryFn: () => api.listPlaces(tripId!),
    enabled: Boolean(tripId),
  })
}

export function useCreatePlace(tripId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (body: CreatePlaceRequest) => api.createPlace(tripId, body),
    onSuccess: (created) => {
      queryClient.setQueryData<PlaceResponse[]>(queryKeys.places(tripId), (current) =>
        current ? [...current, created] : [created],
      )
    },
  })
}

export function useUpdatePlace(tripId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ placeId, body }: { placeId: string; body: UpdatePlaceRequest }) =>
      api.updatePlace(tripId, placeId, body),
    onSuccess: (updated) => {
      queryClient.setQueryData<PlaceResponse[]>(queryKeys.places(tripId), (current) =>
        current?.map((place) => (place.id === updated.id ? updated : place)),
      )
    },
  })
}

/**
 * Toggling a like is optimistic: the vote flips immediately and rolls back if
 * the server refuses. The status that comes back may differ from a naive guess
 * — the server owns promotion to Confirmed — so the response replaces the row
 * wholesale rather than being merged.
 */
export function useToggleLike(tripId: string, myMemberId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ placeId, liked }: { placeId: string; liked: boolean }) =>
      liked ? api.unlikePlace(tripId, placeId) : api.likePlace(tripId, placeId),

    onMutate: async ({ placeId, liked }) => {
      await queryClient.cancelQueries({ queryKey: queryKeys.places(tripId) })
      const previous = queryClient.getQueryData<PlaceResponse[]>(queryKeys.places(tripId))

      queryClient.setQueryData<PlaceResponse[]>(queryKeys.places(tripId), (current) =>
        current?.map((place) =>
          place.id === placeId
            ? {
                ...place,
                // mirror of server rule: only the vote is predicted here. The
                // resulting status is the server's call and arrives with the response.
                likedByMemberIds: liked
                  ? place.likedByMemberIds.filter((id) => id !== myMemberId)
                  : [...place.likedByMemberIds, myMemberId],
              }
            : place,
        ),
      )

      return { previous }
    },

    onError: (_error, _variables, context) => {
      if (context?.previous) {
        queryClient.setQueryData(queryKeys.places(tripId), context.previous)
      }
    },

    onSuccess: (updated) => {
      queryClient.setQueryData<PlaceResponse[]>(queryKeys.places(tripId), (current) =>
        current?.map((place) => (place.id === updated.id ? updated : place)),
      )
    },
  })
}

export function useChangePlaceStatus(tripId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({
      placeId,
      status,
      skipReason,
    }: {
      placeId: string
      status: PlaceStatus
      skipReason?: string
    }) => api.changePlaceStatus(tripId, placeId, status, skipReason),

    onSuccess: (updated) => {
      queryClient.setQueryData<PlaceResponse[]>(queryKeys.places(tripId), (current) =>
        current?.map((place) => (place.id === updated.id ? updated : place)),
      )
    },
  })
}

export function useDeletePlace(tripId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ placeId, force }: { placeId: string; force?: boolean }) =>
      api.deletePlace(tripId, placeId, force),
    onSuccess: (deleted) => {
      // Soft-deleted places are excluded from the default list, so drop it here
      // rather than refetching just to observe the same removal.
      queryClient.setQueryData<PlaceResponse[]>(queryKeys.places(tripId), (current) =>
        current?.filter((place) => place.id !== deleted.id),
      )
    },
  })
}
