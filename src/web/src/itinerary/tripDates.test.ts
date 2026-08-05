import { describe, expect, it } from 'vitest'
import { formatDayLabel, formatTime, tripDays } from './tripDates'

describe('tripDays', () => {
  it('includes both the first and the last day', () => {
    expect(tripDays('2026-03-01', '2026-03-03')).toEqual([
      '2026-03-01',
      '2026-03-02',
      '2026-03-03',
    ])
  })

  it('handles a single-day trip', () => {
    expect(tripDays('2026-03-01', '2026-03-01')).toEqual(['2026-03-01'])
  })

  it('crosses a month boundary', () => {
    expect(tripDays('2026-02-27', '2026-03-02')).toEqual([
      '2026-02-27',
      '2026-02-28',
      '2026-03-01',
      '2026-03-02',
    ])
  })

  it('crosses a year boundary', () => {
    expect(tripDays('2026-12-30', '2027-01-02')).toEqual([
      '2026-12-30',
      '2026-12-31',
      '2027-01-01',
      '2027-01-02',
    ])
  })

  it('handles a leap day', () => {
    expect(tripDays('2028-02-27', '2028-03-01')).toEqual([
      '2028-02-27',
      '2028-02-28',
      '2028-02-29',
      '2028-03-01',
    ])
  })

  it('does not gain or lose a day across a daylight-saving change', () => {
    // Europe/London springs forward on 2026-03-29. Stepping in local time
    // would produce a 23-hour day and drop or duplicate one.
    const days = tripDays('2026-03-27', '2026-03-31')

    expect(days).toEqual([
      '2026-03-27',
      '2026-03-28',
      '2026-03-29',
      '2026-03-30',
      '2026-03-31',
    ])
  })

  it('returns nothing when the range is inverted rather than looping', () => {
    expect(tripDays('2026-03-05', '2026-03-01')).toEqual([])
  })

  it('produces the inclusive day count for a long trip', () => {
    // The spec's maximum span, read inclusively (see DECISIONS D7).
    expect(tripDays('2026-03-01', '2026-04-29')).toHaveLength(60)
  })
})

describe('formatDayLabel', () => {
  it('shows the weekday and the day of the month', () => {
    // 2026-03-01 is a Sunday.
    expect(formatDayLabel('2026-03-01')).toBe('CN · 01/03')
  })

  it('pads single digits so the columns line up', () => {
    expect(formatDayLabel('2026-03-04')).toContain('04/03')
  })
})

describe('formatTime', () => {
  it('trims seconds off a wire time', () => {
    expect(formatTime('09:30:00')).toBe('09:30')
    expect(formatTime('23:59:00')).toBe('23:59')
  })

  it('keeps null as null so "sometime that day" survives', () => {
    expect(formatTime(null)).toBeNull()
  })
})
