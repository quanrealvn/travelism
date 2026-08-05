import type { FeasibilityFindingResponse, FeasibilityLevel } from '../api/api-types'
import { describeFinding } from '../itinerary/feasibilityText'

const LEVEL_ICONS: Record<FeasibilityLevel, string> = {
  error: '✕',
  warning: '!',
  info: 'i',
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
