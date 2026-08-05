/**
 * WMO weather codes, which is what Open-Meteo returns.
 *
 * Grouped rather than enumerated: the distinction between "slight" and
 * "moderate" drizzle does not change whether you bring a coat, and a planner
 * only needs to know what the day will feel like.
 */
/**
 * What a forecast looks like.
 *
 * `kind` is a stable slug the stylesheet colours on — sun is amber, rain is
 * blue, a storm is violet — so the weather strip reads at a glance instead of
 * being a row of identical grey chips with different glyphs in them. Every pair
 * is a tinted background with ink chosen to clear AA on it, so the colour is
 * decoration over the label rather than a replacement for it.
 */
export type WeatherKind =
  | 'unknown'
  | 'clear'
  | 'partly'
  | 'cloudy'
  | 'fog'
  | 'drizzle'
  | 'rain'
  | 'snow'
  | 'storm'

export interface WeatherLook {
  icon: string
  label: string
  kind: WeatherKind
}

export function weatherText(code: number | null): WeatherLook {
  if (code === null) {
    return { icon: '·', label: 'Chưa rõ', kind: 'unknown' }
  }

  if (code === 0) return { icon: '☀️', label: 'Nắng', kind: 'clear' }
  if (code <= 2) return { icon: '⛅', label: 'Ít mây', kind: 'partly' }
  if (code === 3) return { icon: '☁️', label: 'Nhiều mây', kind: 'cloudy' }
  if (code <= 48) return { icon: '🌫️', label: 'Sương mù', kind: 'fog' }
  if (code <= 57) return { icon: '🌦️', label: 'Mưa phùn', kind: 'drizzle' }
  if (code <= 67) return { icon: '🌧️', label: 'Mưa', kind: 'rain' }
  if (code <= 77) return { icon: '🌨️', label: 'Tuyết', kind: 'snow' }
  if (code <= 82) return { icon: '🌧️', label: 'Mưa rào', kind: 'rain' }
  if (code <= 86) return { icon: '🌨️', label: 'Mưa tuyết', kind: 'snow' }

  return { icon: '⛈️', label: 'Dông', kind: 'storm' }
}

/** One decimal is as much precision as a forecast deserves. */
export function formatTemp(celsius: number | null): string {
  return celsius === null ? '—' : `${Math.round(celsius)}°`
}
