import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'
import type { CreatePlaceRequest, PlaceResponse, UpdatePlaceRequest } from './api-types'

/** All server state flows through TanStack Query (spec §8). */
export const queryKeys = {
  session: ['session'] as const,
  trip: (tripId: string) => ['trip', tripId] as const,
  places: (tripId: string) => ['places', tripId] as const,
  placeSearch: (tripId: string, query: string) => ['place-search', tripId, query] as const,
  placeLink: (tripId: string, url: string) => ['place-link', tripId, url] as const,
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
