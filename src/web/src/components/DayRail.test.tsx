import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { DayRail } from './DayRail'
import { formatTemp, weatherText } from '../itinerary/weatherText'
import type { WeatherResponse } from '../api/api-types'

const DAYS = ['2026-03-01', '2026-03-02', '2026-03-03']

function weather(overrides: Partial<WeatherResponse> = {}): WeatherResponse {
  return {
    lat: 20.8386,
    lng: 104.6383,
    timeZoneId: 'Asia/Bangkok',
    stale: false,
    days: [
      { date: '2026-03-01', maxTempC: 28.4, minTempC: 18.2, precipitationMm: 0, weatherCode: 0 },
      { date: '2026-03-02', maxTempC: 24.9, minTempC: 17.1, precipitationMm: 12.5, weatherCode: 61 },
    ],
    ...overrides,
  }
}

describe('DayRail', () => {
  it('still offers every day when there is no forecast', () => {
    // This rail is the day switcher: on a phone only the selected day's column
    // is on screen, so if it vanished during a weather outage (spec §5.5
    // answers 204 for a trip with nowhere to forecast) you could never reach
    // day two.
    render(<DayRail weather={null} days={DAYS} selectedDate={null} onSelectDate={vi.fn()} />)

    expect(screen.getByTestId('weather-2026-03-01')).toBeInTheDocument()
    expect(screen.getByTestId('weather-2026-03-03')).toBeInTheDocument()
  })

  it('says nothing about the weather when there is no forecast', () => {
    // A question mark on every card reads as an error rather than as silence.
    render(<DayRail weather={null} days={DAYS} selectedDate={null} onSelectDate={vi.fn()} />)

    expect(screen.getByTestId('weather-2026-03-01')).not.toHaveTextContent(/chưa rõ/i)
  })

  it('renders nothing when the trip has no days', () => {
    const { container } = render(
      <DayRail weather={null} days={[]} selectedDate={null} onSelectDate={vi.fn()} />,
    )

    expect(container).toBeEmptyDOMElement()
  })

  it('shows a card for every trip day, including ones with no forecast', () => {
    // Keeps the rail aligned with the itinerary columns below it.
    render(<DayRail weather={weather()} days={DAYS} selectedDate={null} onSelectDate={vi.fn()} />)

    expect(screen.getByTestId('weather-2026-03-01')).toBeInTheDocument()
    expect(screen.getByTestId('weather-2026-03-03')).toBeInTheDocument()
  })

  it('shows the high and low for a forecast day', () => {
    render(<DayRail weather={weather()} days={DAYS} selectedDate={null} onSelectDate={vi.fn()} />)

    expect(screen.getByTestId('weather-2026-03-01')).toHaveTextContent('28° / 18°')
  })

  it('shows rainfall only when there is some', () => {
    render(<DayRail weather={weather()} days={DAYS} selectedDate={null} onSelectDate={vi.fn()} />)

    expect(screen.getByTestId('weather-2026-03-02')).toHaveTextContent('12.5 mm')
    expect(screen.getByTestId('weather-2026-03-01')).not.toHaveTextContent('mm')
  })

  it('says when the forecast came from cache during an outage', () => {
    // Spec §5.5: stale must be flagged, never presented as current.
    render(
      <DayRail
        weather={weather({ stale: true })}
        days={DAYS}
        selectedDate={null}
        onSelectDate={vi.fn()}
      />,
    )

    expect(screen.getByRole('status')).toHaveTextContent(/dự báo cũ/i)
  })

  it('does not cry stale for a fresh forecast', () => {
    render(<DayRail weather={weather()} days={DAYS} selectedDate={null} onSelectDate={vi.fn()} />)

    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('marks the selected day and reports a change', async () => {
    const onSelectDate = vi.fn()
    render(
      <DayRail
        weather={weather()}
        days={DAYS}
        selectedDate="2026-03-01"
        onSelectDate={onSelectDate}
      />,
    )

    const selected = screen.getByTestId('weather-2026-03-01')
    expect(selected).toHaveClass('selected')
    expect(selected).toHaveAttribute('aria-pressed', 'true')

    await userEvent.click(screen.getByTestId('weather-2026-03-02'))
    expect(onSelectDate).toHaveBeenCalledWith('2026-03-02')
  })
})

describe('weatherText', () => {
  it.each([
    [0, 'Nắng'],
    [2, 'Ít mây'],
    [3, 'Nhiều mây'],
    [45, 'Sương mù'],
    [55, 'Mưa phùn'],
    [63, 'Mưa'],
    [80, 'Mưa rào'],
    [95, 'Dông'],
  ])('maps WMO code %i to %s', (code, label) => {
    expect(weatherText(code).label).toBe(label)
  })

  it('handles a missing code rather than rendering undefined', () => {
    expect(weatherText(null).label).toMatch(/chưa rõ/i)
    expect(weatherText(null).icon).not.toBe('')
  })
})

describe('formatTemp', () => {
  it('rounds to a whole degree', () => {
    expect(formatTemp(28.4)).toBe('28°')
    expect(formatTemp(28.6)).toBe('29°')
  })

  it('handles a negative temperature', () => {
    expect(formatTemp(-3.2)).toBe('-3°')
  })

  it('shows a dash rather than zero when unknown', () => {
    // "0°" would be a forecast; "—" is the absence of one.
    expect(formatTemp(null)).toBe('—')
  })
})
