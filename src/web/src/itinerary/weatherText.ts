/**
 * WMO weather codes, which is what Open-Meteo returns.
 *
 * Grouped rather than enumerated: the distinction between "slight" and
 * "moderate" drizzle does not change whether you bring a coat, and a planner
 * only needs to know what the day will feel like.
 */
export function weatherText(code: number | null): { icon: string; label: string } {
  if (code === null) {
    return { icon: '·', label: 'Chưa rõ' }
  }

  if (code === 0) return { icon: '☀', label: 'Nắng' }
  if (code <= 2) return { icon: '⛅', label: 'Ít mây' }
  if (code === 3) return { icon: '☁', label: 'Nhiều mây' }
  if (code <= 48) return { icon: '🌫', label: 'Sương mù' }
  if (code <= 57) return { icon: '🌦', label: 'Mưa phùn' }
  if (code <= 67) return { icon: '🌧', label: 'Mưa' }
  if (code <= 77) return { icon: '🌨', label: 'Tuyết' }
  if (code <= 82) return { icon: '🌧', label: 'Mưa rào' }
  if (code <= 86) return { icon: '🌨', label: 'Mưa tuyết' }

  return { icon: '⛈', label: 'Dông' }
}

/** One decimal is as much precision as a forecast deserves. */
export function formatTemp(celsius: number | null): string {
  return celsius === null ? '—' : `${Math.round(celsius)}°`
}
