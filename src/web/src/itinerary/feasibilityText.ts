import type { FeasibilityFindingResponse, FeasibilityLevel } from '../api/api-types'

/**
 * Turns a feasibility finding into something a traveller can act on.
 *
 * The server sends a stable code plus its numbers; the wording lives here so
 * the API stays a contract rather than a source of Vietnamese copy.
 */
export function describeFinding(finding: FeasibilityFindingResponse): string {
  const data = finding.data
  const gap = asNumber(data.gapMinutes)
  const travel = asNumber(data.travelMinutes)
  const estimated = data.source === 'haversine'

  switch (finding.code) {
    case 'OVERLAP': {
      const overlap = asNumber(data.overlapMinutes)
      return overlap === null
        ? 'Trùng giờ với điểm trước'
        : `Trùng giờ ${overlap} phút với điểm trước`
    }

    case 'INSUFFICIENT_TRAVEL_TIME':
      return (
        `Chỉ có ${gap ?? '?'} phút nhưng cần ${travel ?? '?'} phút di chuyển` +
        // Spec §5.4: an estimate must never be mistaken for a measurement.
        (estimated ? ' (ước tính)' : '')
      )

    case 'IDLE_GAP':
      return `Trống ${asNumber(data.idleMinutes) ?? '?'} phút`

    case 'TIMESLOT_MISMATCH':
      return `Giờ này không hợp với địa điểm (${String(data.actualSlot ?? '')})`

    case 'UNSCHEDULED_TIME':
      return 'Chưa đặt giờ'

    case 'CROSSES_MIDNIGHT':
      return 'Kéo dài qua nửa đêm'

    default:
      return finding.code
  }
}

/** Worst level present, for summarising a whole day in one mark. */
export function worstLevel(findings: FeasibilityFindingResponse[]): FeasibilityLevel | null {
  if (findings.some((f) => f.level === 'error')) return 'error'
  if (findings.some((f) => f.level === 'warning')) return 'warning'
  if (findings.length > 0) return 'info'
  return null
}

function asNumber(value: unknown): number | null {
  return typeof value === 'number' ? value : null
}
