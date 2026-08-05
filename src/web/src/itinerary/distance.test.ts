import { describe, expect, it } from 'vitest'
import { distanceKm, formatDistance } from './distance'

// Two real points in the trip the app is built around, ~9.4km apart.
const THAC_DAI_YEM = { lat: 20.817975, lng: 104.591686 }
const MOC_CHAU = { lat: 20.845, lng: 104.628 }

describe('distanceKm', () => {
  it('measures a short hop between two stops', () => {
    expect(distanceKm(THAC_DAI_YEM, MOC_CHAU)).toBeCloseTo(4.7, 0)
  })

  it('is zero for a point against itself', () => {
    expect(distanceKm(MOC_CHAU, MOC_CHAU)).toBe(0)
  })

  it('is symmetric', () => {
    expect(distanceKm(THAC_DAI_YEM, MOC_CHAU)).toBeCloseTo(
      distanceKm(MOC_CHAU, THAC_DAI_YEM),
      10,
    )
  })

  it('handles antipodal points without NaN', () => {
    // sqrt of a value a hair above 1 from floating-point error would make
    // Math.asin return NaN, which would render as "cách ~NaN km".
    const distance = distanceKm({ lat: 0, lng: 0 }, { lat: 0, lng: 180 })
    expect(Number.isFinite(distance)).toBe(true)
    expect(distance).toBeCloseTo(20015, 0)
  })

  it('crosses the meridian correctly', () => {
    expect(distanceKm({ lat: 51.5, lng: -0.1 }, { lat: 51.5, lng: 0.1 })).toBeCloseTo(13.9, 0)
  })
})

describe('formatDistance', () => {
  it('uses metres below a kilometre, because "0,3 km" is not how anyone says it', () => {
    expect(formatDistance(0.3)).toBe('300 m')
    expect(formatDistance(0.85)).toBe('850 m')
  })

  it('uses one decimal up to 10km', () => {
    expect(formatDistance(4.72)).toBe('4,7 km')
  })

  it('drops the decimal beyond 10km, where it stops meaning anything', () => {
    expect(formatDistance(42.4)).toBe('42 km')
  })

  it('writes the decimal with a comma, as Vietnamese does', () => {
    expect(formatDistance(1.5)).toContain(',')
    expect(formatDistance(1.5)).not.toContain('.')
  })
})
