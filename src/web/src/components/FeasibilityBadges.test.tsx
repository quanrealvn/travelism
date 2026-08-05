import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { FeasibilityBadges } from './FeasibilityBadges'
import { describeFinding, worstLevel } from '../itinerary/feasibilityText'
import type { FeasibilityFindingResponse } from '../api/api-types'

function finding(overrides: Partial<FeasibilityFindingResponse> = {}): FeasibilityFindingResponse {
  return {
    itineraryItemId: 'i1',
    level: 'error',
    code: 'OVERLAP',
    data: {},
    ...overrides,
  }
}

describe('describeFinding', () => {
  it('states how badly two stops overlap', () => {
    expect(
      describeFinding(finding({ code: 'OVERLAP', data: { overlapMinutes: 45 } })),
    ).toContain('45')
  })

  it('states both the gap and the drive it has to cover', () => {
    const text = describeFinding(
      finding({
        code: 'INSUFFICIENT_TRAVEL_TIME',
        data: { gapMinutes: 10, travelMinutes: 40, source: 'osrm' },
      }),
    )

    expect(text).toContain('10')
    expect(text).toContain('40')
  })

  it('marks a haversine figure as an estimate', () => {
    // Spec §5.4: the source must be visible, so nobody plans around a guess
    // believing it was measured.
    const text = describeFinding(
      finding({
        code: 'INSUFFICIENT_TRAVEL_TIME',
        data: { gapMinutes: 10, travelMinutes: 40, source: 'haversine' },
      }),
    )

    expect(text).toMatch(/ước tính/i)
  })

  it('does not call a routed figure an estimate', () => {
    const text = describeFinding(
      finding({
        code: 'INSUFFICIENT_TRAVEL_TIME',
        data: { gapMinutes: 10, travelMinutes: 40, source: 'osrm' },
      }),
    )

    expect(text).not.toMatch(/ước tính/i)
  })

  it('describes each remaining code', () => {
    expect(describeFinding(finding({ code: 'IDLE_GAP', data: { idleMinutes: 150 } }))).toContain('150')
    expect(
      describeFinding(finding({ code: 'TIMESLOT_MISMATCH', data: { actualSlot: 'Noon' } })),
    ).toContain('Noon')
    expect(describeFinding(finding({ code: 'UNSCHEDULED_TIME' }))).toMatch(/chưa đặt giờ/i)
    expect(describeFinding(finding({ code: 'CROSSES_MIDNIGHT' }))).toMatch(/nửa đêm/i)
  })

  it('survives a finding whose numbers are missing', () => {
    // The data bag is code-specific; a missing field must not render "NaN".
    const text = describeFinding(finding({ code: 'INSUFFICIENT_TRAVEL_TIME', data: {} }))

    expect(text).not.toContain('NaN')
    expect(text).not.toContain('undefined')
  })
})

describe('FeasibilityBadges', () => {
  it('renders nothing when the day is fine', () => {
    const { container } = render(<FeasibilityBadges findings={[]} />)

    expect(container).toBeEmptyDOMElement()
  })

  it('renders one badge per finding, carrying its level', () => {
    render(
      <FeasibilityBadges
        findings={[
          finding({ code: 'OVERLAP', level: 'error', data: { overlapMinutes: 30 } }),
          finding({ code: 'TIMESLOT_MISMATCH', level: 'warning', data: { actualSlot: 'Noon' } }),
        ]}
      />,
    )

    expect(screen.getByTestId('badge-OVERLAP')).toHaveClass('badge-error')
    expect(screen.getByTestId('badge-TIMESLOT_MISMATCH')).toHaveClass('badge-warning')
  })
})

describe('worstLevel', () => {
  it('is null for a clean day', () => {
    expect(worstLevel([])).toBeNull()
  })

  it('reports an error over anything else present', () => {
    expect(
      worstLevel([finding({ level: 'info' }), finding({ level: 'error' }), finding({ level: 'warning' })]),
    ).toBe('error')
  })

  it('reports a warning over information', () => {
    expect(worstLevel([finding({ level: 'info' }), finding({ level: 'warning' })])).toBe('warning')
  })

  it('reports information when that is all there is', () => {
    expect(worstLevel([finding({ level: 'info' })])).toBe('info')
  })
})
