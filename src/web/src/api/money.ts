/**
 * Money formatting. Amounts travel and are stored as integer minor units
 * (spec §5.3); this is the only place they become a decimal, and it happens on
 * the way to the screen — never on the way back into a calculation.
 */
export function formatMoney(
  minorUnits: number | null | undefined,
  currency: string,
  exponent: number,
): string {
  if (minorUnits === null || minorUnits === undefined) {
    return '—'
  }

  const major = exponent === 0 ? minorUnits : minorUnits / 10 ** exponent

  return new Intl.NumberFormat('vi-VN', {
    style: 'currency',
    currency,
    minimumFractionDigits: exponent,
    maximumFractionDigits: exponent,
  }).format(major)
}

/**
 * Parses user input into integer minor units. Returns null for blank input and
 * NaN for anything unparseable, so callers can tell "not given" from "invalid".
 */
export function parseMoney(input: string, exponent: number): number | null {
  const trimmed = input.trim()
  if (trimmed === '') {
    return null
  }

  const normalized = trimmed.replace(/[\s.,]/g, (match) => (match === ',' ? '.' : ''))
  const value = Number(normalized)
  if (!Number.isFinite(value) || value < 0) {
    return Number.NaN
  }

  return Math.round(value * 10 ** exponent)
}

export function formatDuration(minutes: number): string {
  if (minutes < 60) {
    return `${minutes} phút`
  }

  const hours = Math.floor(minutes / 60)
  const rest = minutes % 60
  return rest === 0 ? `${hours} giờ` : `${hours} giờ ${rest} phút`
}
