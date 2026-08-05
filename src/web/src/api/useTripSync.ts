import { useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import type { HubConnection } from '@microsoft/signalr'
import { queryKeys } from './hooks'

/** The broadcast shape from spec §5.8. */
export interface TripEvent {
  event:
    | 'PlaceChanged'
    | 'PlaceDeleted'
    | 'ItineraryChanged'
    | 'ExpenseChanged'
    | 'TripChanged'
    | 'MemberJoined'
  entityType: string
  entityId: string
  payload: unknown
  byMemberId: string
  at: string
}

export type SyncStatus = 'connecting' | 'live' | 'offline'

/**
 * Keeps this client in step with the others on the trip.
 *
 * Broadcasts invalidate the affected queries rather than being applied as
 * patches. The payload is enough to patch with, but the server owns derived
 * state — a place's status after a like, the whole balance after an expense —
 * and reapplying those rules here would be a second implementation to keep in
 * agreement with the first.
 *
 * On reconnect the entire trip is refetched (spec §5.8) instead of replaying
 * missed events: there is no durable event log, so anything else would be a
 * guess about what was missed.
 */
export function useTripSync(tripId: string | undefined, myMemberId: string | undefined): SyncStatus {
  const queryClient = useQueryClient()
  const [status, setStatus] = useState<SyncStatus>('connecting')
  const [networkDown, setNetworkDown] = useState(
    () => typeof navigator !== 'undefined' && navigator.onLine === false,
  )
  const connectionRef = useRef<HubConnection | null>(null)

  /*
   * The browser knows the network went away immediately; SignalR only finds out
   * when a keep-alive times out, which took about thirty seconds. For that half
   * minute the app displayed "Đang đồng bộ trực tiếp" while syncing nothing.
   */
  useEffect(() => {
    const goOffline = () => setNetworkDown(true)
    const goOnline = () => setNetworkDown(false)

    window.addEventListener('offline', goOffline)
    window.addEventListener('online', goOnline)
    return () => {
      window.removeEventListener('offline', goOffline)
      window.removeEventListener('online', goOnline)
    }
  }, [])

  useEffect(() => {
    if (!tripId) {
      return
    }

    const connection = new HubConnectionBuilder()
      .withUrl(`/hubs/trip?tripId=${tripId}`)
      // Rejoining matters more than reconnecting fast; the delays back off and
      // then settle, so a laptop closed for an hour still recovers.
      .withAutomaticReconnect([0, 2000, 5000, 10_000, 30_000])
      .configureLogging(LogLevel.Warning)
      .build()

    connectionRef.current = connection

    connection.on('tripEvent', (event: TripEvent) => {
      // Our own writes already updated the cache from their response; acting on
      // the echo would flicker the UI for the person who made the change.
      if (myMemberId && event.byMemberId === myMemberId) {
        return
      }

      switch (event.event) {
        case 'PlaceChanged':
        case 'PlaceDeleted':
          void queryClient.invalidateQueries({ queryKey: queryKeys.places(tripId) })
          void queryClient.invalidateQueries({ queryKey: ['suggestions', tripId] })
          break

        case 'ItineraryChanged':
          void queryClient.invalidateQueries({ queryKey: queryKeys.itinerary(tripId) })
          void queryClient.invalidateQueries({ queryKey: ['suggestions', tripId] })
          void queryClient.invalidateQueries({ queryKey: ['feasibility', tripId] })
          break

        case 'ExpenseChanged':
          void queryClient.invalidateQueries({ queryKey: queryKeys.expenses(tripId) })
          void queryClient.invalidateQueries({ queryKey: queryKeys.balance(tripId) })
          break

        case 'TripChanged':
        case 'MemberJoined':
          void queryClient.invalidateQueries({ queryKey: queryKeys.trip(tripId) })
          break
      }
    })

    connection.onreconnecting(() => setStatus('offline'))

    connection.onreconnected(() => {
      setStatus('live')
      // Spec §5.8: refetch everything rather than guess what was missed.
      void queryClient.invalidateQueries({ queryKey: ['trip', tripId] })
      void queryClient.invalidateQueries({ queryKey: ['places', tripId] })
      void queryClient.invalidateQueries({ queryKey: ['itinerary', tripId] })
      void queryClient.invalidateQueries({ queryKey: ['expenses', tripId] })
      void queryClient.invalidateQueries({ queryKey: ['balance', tripId] })
      void queryClient.invalidateQueries({ queryKey: ['suggestions', tripId] })
      void queryClient.invalidateQueries({ queryKey: ['feasibility', tripId] })
    })

    connection.onclose(() => setStatus('offline'))

    connection
      .start()
      .then(() => setStatus('live'))
      // Sync is an enhancement: without it the app still works on refetches,
      // so a failed connection must not break anything.
      .catch(() => setStatus('offline'))

    return () => {
      connectionRef.current = null
      if (connection.state !== HubConnectionState.Disconnected) {
        void connection.stop()
      }
    }
  }, [tripId, myMemberId, queryClient])

  // The network wins: a socket that has not noticed it is dead is not live.
  return networkDown ? 'offline' : status
}
