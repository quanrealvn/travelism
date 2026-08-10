import { useRef } from 'react'
import type { PointerEvent as ReactPointerEvent } from 'react'

import type { SnapPoint } from '../places/snapPoint'
import { SNAP_ORDER } from '../places/snapPoint'

const SNAP_LABEL: Record<SnapPoint, string> = {
  peek: 'Kéo lên để xem danh sách',
  half: 'Kéo để xem bản đồ hoặc danh sách',
  full: 'Kéo xuống để xem bản đồ',
}

interface SheetGripProps {
  snap: SnapPoint
  onSnap: (next: SnapPoint) => void
}

/**
 * The handle that moves the list sheet over the map.
 *
 * Only the handle is draggable, never the sheet body. Dragging the body would
 * have to fight the list's own scrolling — every such implementation ends up
 * guessing, from scroll position and gesture direction, whether a downward
 * swipe means "scroll up" or "close the sheet", and it guesses wrong often
 * enough to feel broken. A dedicated grip has no ambiguity to resolve.
 *
 * It is also a button. Dragging is a pointer gesture with no keyboard
 * equivalent, so tapping or pressing Enter cycles the same three stops — which
 * is the whole control, reachable without a pointer at all.
 */
export function SheetGrip({ snap, onSnap }: SheetGripProps) {
  const start = useRef<{ y: number; snap: SnapPoint } | null>(null)
  const moved = useRef(false)

  function handlePointerDown(event: ReactPointerEvent<HTMLButtonElement>) {
    start.current = { y: event.clientY, snap }
    moved.current = false
    event.currentTarget.setPointerCapture(event.pointerId)
  }

  function handlePointerMove(event: ReactPointerEvent<HTMLButtonElement>) {
    if (!start.current) {
      return
    }

    const dy = start.current.y - event.clientY
    // A threshold, so a tap that drifts a pixel is still a tap.
    if (Math.abs(dy) < 24) {
      return
    }

    moved.current = true

    const from = SNAP_ORDER.indexOf(start.current.snap)
    // One stop per ~90px of travel: far enough that a small correction does not
    // fly past the middle stop, close enough to cross the whole range in one
    // comfortable thumb movement.
    const steps = Math.trunc(dy / 90)
    const next = SNAP_ORDER[Math.min(SNAP_ORDER.length - 1, Math.max(0, from + steps))]

    if (next && next !== snap) {
      onSnap(next)
    }
  }

  function handlePointerUp() {
    // A tap cycles forward and wraps, so the control is complete without a drag.
    if (!moved.current) {
      const from = SNAP_ORDER.indexOf(snap)
      onSnap(SNAP_ORDER[(from + 1) % SNAP_ORDER.length]!)
    }

    start.current = null
  }

  return (
    <button
      type="button"
      className="sheet-grip"
      aria-label={SNAP_LABEL[snap]}
      onPointerDown={handlePointerDown}
      onPointerMove={handlePointerMove}
      onPointerUp={handlePointerUp}
      onPointerCancel={handlePointerUp}
    >
      <span className="sheet-grip-bar" aria-hidden="true" />
    </button>
  )
}
