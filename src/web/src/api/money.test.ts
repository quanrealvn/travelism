import { describe, expect, it } from 'vitest'
import { formatDuration, formatMoney, parseMoney } from './money'

describe('formatMoney', () => {
  it('renders a zero-decimal currency without a fractional part', () => {
    // VND has exponent 0, so 100_001 minor units is ₫100.001 — not ₫1000.01.
    expect(formatMoney(100_001, 'VND', 0)).toContain('100.001')
    expect(formatMoney(100_001, 'VND', 0)).not.toContain(',01')
  })

  it('scales a two-decimal currency by its exponent', () => {
    expect(formatMoney(12_345, 'USD', 2)).toContain('123,45')
  })

  it('renders a dash for an unknown amount', () => {
    expect(formatMoney(null, 'VND', 0)).toBe('—')
    expect(formatMoney(undefined, 'VND', 0)).toBe('—')
  })

  it('renders zero as a real amount rather than as unknown', () => {
    expect(formatMoney(0, 'VND', 0)).not.toBe('—')
  })

  it('does not lose precision on large integer amounts', () => {
    // A round-trip through a float would corrupt this; the value is an integer
    // number of minor units and must render digit for digit.
    expect(formatMoney(9_007_199_254_740_991, 'VND', 0)).toContain('9.007.199.254.740.991')
  })
})

describe('parseMoney', () => {
  it('treats blank input as "not given"', () => {
    expect(parseMoney('', 0)).toBeNull()
    expect(parseMoney('   ', 0)).toBeNull()
  })

  it('parses a plain integer for a zero-decimal currency', () => {
    expect(parseMoney('50000', 0)).toBe(50_000)
  })

  it('ignores thousands separators', () => {
    expect(parseMoney('1.200.000', 0)).toBe(1_200_000)
    expect(parseMoney('1 200 000', 0)).toBe(1_200_000)
  })

  it('scales to minor units for a two-decimal currency', () => {
    expect(parseMoney('12,34', 2)).toBe(1_234)
  })

  it('reports invalid input as NaN so it is distinguishable from blank', () => {
    expect(parseMoney('abc', 0)).toBeNaN()
    expect(parseMoney('-5', 0)).toBeNaN()
  })

  it('round-trips through formatting for a zero-decimal currency', () => {
    for (const amount of [0, 1, 999, 100_001, 1_234_567]) {
      expect(parseMoney(String(amount), 0)).toBe(amount)
    }
  })
})

describe('formatDuration', () => {
  it.each([
    [5, '5 phút'],
    [59, '59 phút'],
    [60, '1 giờ'],
    [90, '1 giờ 30 phút'],
    [1440, '24 giờ'],
  ])('formats %i minutes as %s', (minutes, expected) => {
    expect(formatDuration(minutes)).toBe(expected)
  })
})
