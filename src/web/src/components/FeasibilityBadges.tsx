import type { FeasibilityFindingResponse, FeasibilityLevel } from '../api/api-types'
import { describeFinding } from '../itinerary/feasibilityText'

/*
 * Glyphs, not letters. These were '✕', '!' and 'i', and the badge was not a
 * flex container — so an info finding rendered as "iChưa đặt giờ", where the
 * icon reads as a typo in the first word rather than as an icon at all.
 */
const LEVEL_ICONS: Record<FeasibilityLevel, string> = {
  error: '⛔',
  warning: '⚠️',
  info: 'ℹ️',
}

export function FeasibilityBadges({ findings }: { findings: FeasibilityFindingResponse[] }) {
  if (findings.length === 0) {
    return null
  }

  return (
    <ul className="feasibility-badges">
      {findings.map((finding, index) => (
        <li
          key={`${finding.code}-${index}`}
          className={`badge badge-${finding.level}`}
          data-testid={`badge-${finding.code}`}
        >
          <span aria-hidden="true">{LEVEL_ICONS[finding.level]}</span>
          {describeFinding(finding)}
        </li>
      ))}
    </ul>
  )
}
