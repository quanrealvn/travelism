import type { IsoDate } from '../api/api-types'

/**
 * The trip's days as ISO date strings.
 *
 * Built by stepping the calendar date, never by adding milliseconds to a Date:
 * a trip spanning a daylight-saving change would otherwise gain or lose a day,
 * and spec §7.10 is explicit that a calendar date must never shift.
 */
export function tripDays(startDate: IsoDate, endDate: IsoDate): IsoDate[] {
  const days: IsoDate[] = []
  const [startYear, startMonth, startDay] = splitIso(startDate)
  const [endYear, endMonth, endDay] = splitIso(endDate)

  // UTC throughout: these are calendar positions, not instants, so the local
  // zone must never be consulted.
  const cursor = new Date(Date.UTC(startYear, startMonth - 1, startDay))
  const last = Date.UTC(endYear, endMonth - 1, endDay)

  // A guard rather than a while(true): a malformed range must not spin.
  for (let guard = 0; cursor.getTime() <= last && guard < 400; guard++) {
    days.push(toIso(cursor))
    cursor.setUTCDate(cursor.getUTCDate() + 1)
  }

  return days
}

function splitIso(value: IsoDate): [number, number, number] {
  const [year, month, day] = value.split('-').map(Number)
  return [year ?? 1970, month ?? 1, day ?? 1]
}

function toIso(date: Date): IsoDate {
  const year = date.getUTCFullYear().toString().padStart(4, '0')
  const month = (date.getUTCMonth() + 1).toString().padStart(2, '0')
  const day = date.getUTCDate().toString().padStart(2, '0')
  return `${year}-${month}-${day}`
}

const WEEKDAYS = ['CN', 'T2', 'T3', 'T4', 'T5', 'T6', 'T7']

/** "T4 · 04/03" — short enough for a column heading. */
export function formatDayLabel(date: IsoDate): string {
  const [year, month, day] = splitIso(date)
  const weekday = WEEKDAYS[new Date(Date.UTC(year, month - 1, day)).getUTCDay()] ?? ''
  return `${weekday} · ${String(day).padStart(2, '0')}/${String(month).padStart(2, '0')}`
}

/** `HH:mm:ss` → `HH:mm`; null stays null. */
export function formatTime(startTime: string | null): string | null {
  return startTime === null ? null : startTime.slice(0, 5)
}
