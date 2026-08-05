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
 *
 * Separators follow vi-VN: `.` groups thousands and `,` is the decimal point.
 * A currency with no minor unit — which VND is — has no decimal point at all,
 * so a `,` in that input is refused rather than interpreted. Reading "12,5" as
 * 13 đồng and "1.2" as 12 is the same keystroke meaning either 12 or 1.2
 * million, and there is no reading of it that is safe to guess at.
 */
export function parseMoney(input: string, exponent: number): number | null {
  const trimmed = input.trim().replace(/\s/g, '')
  if (trimmed === '') {
    return null
  }

  if (exponent === 0) {
    // Plain digits, or digits grouped in threes by dots. "12.5" fails on the
    // group of one and "12,5" on the comma, both of which previously parsed to
    // something 10× or 100× out with no warning.
    if (!/^\d+$/.test(trimmed) && !/^\d{1,3}(\.\d{3})+$/.test(trimmed)) {
      return Number.NaN
    }

    return Number(trimmed.replace(/\./g, ''))
  }

  const normalized = trimmed.replace(/\./g, '').replace(',', '.')

  // Number('') is 0 and Number('1e6') is a million; neither is somebody typing
  // an amount into a money field.
  if (!/^\d+(\.\d+)?$/.test(normalized)) {
    return Number.NaN
  }

  return Math.round(Number(normalized) * 10 ** exponent)
}

export function formatDuration(minutes: number): string {
  if (minutes < 60) {
    return `${minutes} phút`
  }

  const hours = Math.floor(minutes / 60)
  const rest = minutes % 60
  return rest === 0 ? `${hours} giờ` : `${hours} giờ ${rest} phút`
}
