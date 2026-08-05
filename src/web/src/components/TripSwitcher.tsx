import { useEffect, useRef, useState } from 'react'
import { useMyTrips } from '../api/hooks'
import { shortDate } from '../api/labels'
import { IconCheck, IconChevron, IconPlus, IconSwitch } from './icons'
import { Spinner } from './Spinner'

interface TripSwitcherProps {
  activeTripId: string
  /** How many trips this browser holds, known without fetching the list. */
  tripCount: number
  onOpenTrip: (tripId: string) => void
  onNewTrip: () => void
  onSeeAll: () => void
}

/**
 * Switching trips, as a menu rather than a destination.
 *
 * It used to be a button that replaced the whole screen with a trips page —
 * which is a lot of ceremony for "show me the other one", and it threw away
 * where you were to do it. A menu keeps the workspace behind it and puts
 * starting a new trip in the same place you go to look for an existing one,
 * which is the moment you discover you do not have it yet.
 *
 * The full screen still exists behind "Xem tất cả": it carries dates,
 * countdowns and the way to forget a trip, none of which belong in a menu.
 */
export function TripSwitcher({
  activeTripId,
  tripCount,
  onOpenTrip,
  onNewTrip,
  onSeeAll,
}: TripSwitcherProps) {
  const [open, setOpen] = useState(false)
  const wrapper = useRef<HTMLDivElement>(null)
  const trigger = useRef<HTMLButtonElement>(null)

  // Only fetched once the menu is asked for: a browser can hold twenty trips
  // and the workspace never needs the list until now.
  const trips = useMyTrips(open)

  useEffect(() => {
    if (!open) {
      return
    }

    function onPointerDown(event: PointerEvent) {
      if (!wrapper.current?.contains(event.target as Node)) {
        setOpen(false)
      }
    }

    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        setOpen(false)
        // Focus goes back where it came from, not to the top of the document.
        trigger.current?.focus()
      }
    }

    document.addEventListener('pointerdown', onPointerDown)
    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('pointerdown', onPointerDown)
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [open])

  function choose(action: () => void) {
    setOpen(false)
    action()
  }

  return (
    <div className="trip-switcher" ref={wrapper}>
      <button
        ref={trigger}
        type="button"
        className="nav-action trip-switcher-trigger"
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen((current) => !current)}
        title="Đổi chuyến đi"
      >
        <span className="nav-action-icon">
          <IconSwitch />
        </span>
        <span className="nav-action-label">
          {/* Its own element so it can shrink and ellipsis. As a bare text node
              it was an anonymous flex item that pushed the row wider than the
              rail instead. */}
          <span className="nav-action-text">Đổi chuyến đi</span>
          <span className="nav-action-note">{tripCount}</span>
        </span>
        <IconChevron className="trip-switcher-caret" />
      </button>

      {open && (
        <div className="trip-menu" role="menu" aria-label="Chuyến đi của bạn">
          {trips.isLoading ? (
            <p className="trip-menu-busy" role="status">
              <Spinner />
              Đang tải…
            </p>
          ) : (
            <ul className="trip-menu-list">
              {(trips.data ?? []).map((trip) => {
                const current = trip.id === activeTripId
                return (
                  <li key={trip.id}>
                    <button
                      type="button"
                      role="menuitem"
                      className={current ? 'trip-menu-item is-current' : 'trip-menu-item'}
                      onClick={() => choose(() => onOpenTrip(trip.id))}
                    >
                      <span className="trip-menu-text">
                        <span className="trip-menu-name">{trip.name}</span>
                        <span className="trip-menu-meta">
                          {shortDate(trip.startDate)}–{shortDate(trip.endDate)} ·{' '}
                          {trip.destination}
                        </span>
                      </span>
                      {/* A tick, not only a highlight: which trip you are in is
                          worth saying in a shape as well as a colour. */}
                      {current && <IconCheck className="trip-menu-tick" />}
                      {current && <span className="visually-hidden">(đang mở)</span>}
                    </button>
                  </li>
                )
              })}
            </ul>
          )}

          <div className="trip-menu-foot">
            <button
              type="button"
              role="menuitem"
              className="trip-menu-action is-primary"
              onClick={() => choose(onNewTrip)}
            >
              <IconPlus />
              Chuyến đi mới
            </button>
            <button
              type="button"
              role="menuitem"
              className="trip-menu-action"
              onClick={() => choose(onSeeAll)}
            >
              Xem tất cả chuyến đi
            </button>
          </div>
        </div>
      )}
    </div>
  )
}
