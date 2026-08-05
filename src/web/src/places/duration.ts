/**
 * Durations as people say them, converted to what the API stores.
 *
 * The API keeps whole minutes; nobody plans in them. "Hai tiếng ở thác" is how
 * a duration gets decided, so the form asks for hours and this is the single
 * place that converts — the same shape as `parseMoney`, which exists for the
 * same reason at the money edge.
 */

/**
 * Hours as typed → whole minutes.
 *
 * Accepts a comma as the decimal mark as well as a dot: Vietnamese keyboards
 * and Vietnamese habit both produce "1,5", and refusing that would reject the
 * most natural way to write an hour and a half.
 *
 * NaN for anything that is not a positive number, so the caller refuses it
 * rather than silently storing a place with no duration — which the feasibility
 * check would then reason about as though the stop were free.
 */
export function hoursToMinutes(input: string): number {
  const trimmed = input.trim().replace(',', '.')
  if (trimmed === '' || !/^\d*\.?\d+$/.test(trimmed)) {
    return Number.NaN
  }

  const hours = Number(trimmed)
  if (!Number.isFinite(hours) || hours <= 0) {
    return Number.NaN
  }

  return Math.round(hours * 60)
}

/** Minutes back to a value the hours field can show, without a trailing ",0". */
export function minutesToHours(minutes: number): string {
  const hours = minutes / 60
  return (Number.isInteger(hours) ? String(hours) : hours.toFixed(2).replace(/0+$/, '')).replace(
    '.',
    ',',
  )
}
