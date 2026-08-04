import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'
import type { CreatePlaceRequest, PlaceResponse, UpdatePlaceRequest } from './api-types'

/** All server state flows through TanStack Query (spec §8). */
export const queryKeys = {
  session: ['session'] as const,
  trip: (tripId: string) => ['trip', tripId] as const,
  places: (tripId: string) => ['places', tripId] as const,
  placeSearch: (tripId: string, query: string) => ['place-search', tripId, query] as const,
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
