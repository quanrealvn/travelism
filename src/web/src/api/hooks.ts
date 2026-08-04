import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'
import type { CreatePlaceRequest, PlaceResponse, UpdatePlaceRequest } from './api-types'

/** All server state flows through TanStack Query (spec §8). */
export const queryKeys = {
  session: ['session'] as const,
  trip: (tripId: string) => ['trip', tripId] as const,
  places: (tripId: string) => ['places', tripId] as const,
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
