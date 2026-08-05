import { describe, expect, it } from 'vitest'
import {
  allCategoryStyles,
  categoryStyle,
  escapeHtml,
  LABEL_ZOOM,
  markerHtml,
  statusOpacity,
} from './placeMarkers'
import { ALL_CATEGORIES } from '../api/api-types'
import type { PlaceCategory, PlaceResponse, PlaceStatus } from '../api/api-types'

function place(overrides: Partial<PlaceResponse> = {}): PlaceResponse {
  return {
    id: 'p1',
    tripId: 't1',
    name: 'Thác Dải Yếm',
    lat: 20.8333,
    lng: 104.6667,
    category: 'Sight',
    timeSlots: ['Morning'],
    estimatedDurationMinutes: 90,
    estimatedCost: 50_000,
    openHoursText: null,
    description: null,
    references: [],
    status: 'Confirmed',
    skipReason: null,
    isDeleted: false,
    likedByMemberIds: [],
    createdAt: '2026-03-01T00:00:00+00:00',
    updatedAt: '2026-03-01T00:00:00+00:00',
    updatedByMemberId: 'm1',
    ...overrides,
  }
}

describe('categoryStyle', () => {
  it('gives every category its own colour', () => {
    const colours = ALL_CATEGORIES.map((category) => categoryStyle(category).color)

    expect(new Set(colours).size).toBe(ALL_CATEGORIES.length)
  })

  it('gives every category an icon as well as a colour', () => {
    // Colour alone would be unreadable for anyone who cannot separate the red
    // and green pins.
    for (const category of ALL_CATEGORIES) {
      expect(categoryStyle(category).path).not.toBe('')
      expect(categoryStyle(category).label).not.toBe('')
    }
  })

  it('draws every category with its own icon', () => {
    const paths = ALL_CATEGORIES.map((category) => categoryStyle(category).path)

    expect(new Set(paths).size).toBe(ALL_CATEGORIES.length)
  })

  it('falls back rather than returning undefined for an unknown category', () => {
    // A category added server-side before the client knows about it.
    const style = categoryStyle('Nightlife' as PlaceCategory)

    expect(style.color).toBe(categoryStyle('Other').color)
  })

  it('lists every category in the legend', () => {
    expect(allCategoryStyles().map((s) => s.category)).toEqual([...ALL_CATEGORIES])
  })
})

describe('statusOpacity', () => {
  it('shows a confirmed place at full strength', () => {
    expect(statusOpacity('Confirmed')).toBe(1)
  })

  it('fades an idea without hiding it', () => {
    const opacity = statusOpacity('Idea')

    expect(opacity).toBeLessThan(1)
    expect(opacity).toBeGreaterThan(0.5)
  })

  it('ranks settledness in the expected order', () => {
    expect(statusOpacity('Confirmed')).toBeGreaterThan(statusOpacity('Shortlist'))
    expect(statusOpacity('Shortlist')).toBeGreaterThan(statusOpacity('Idea'))
    expect(statusOpacity('Idea')).toBeGreaterThan(statusOpacity('Visited'))
  })

  it('never returns an invisible marker', () => {
    const statuses: PlaceStatus[] = ['Idea', 'Shortlist', 'Confirmed', 'Visited', 'Skipped']

    for (const status of statuses) {
      expect(statusOpacity(status)).toBeGreaterThan(0)
      expect(statusOpacity(status)).toBeLessThanOrEqual(1)
    }
  })
})

describe('escapeHtml', () => {
  it('escapes the characters that would break the marker markup', () => {
    expect(escapeHtml('<script>alert(1)</script>')).not.toContain('<script>')
    expect(escapeHtml('a "quoted" name')).not.toContain('"')
    expect(escapeHtml("it's")).not.toContain("'")
  })

  it('escapes ampersands before anything else, so entities are not doubled', () => {
    expect(escapeHtml('Cà phê & bánh')).toBe('Cà phê &amp; bánh')
  })

  it('leaves ordinary Vietnamese text untouched', () => {
    expect(escapeHtml('Thác Dải Yếm')).toBe('Thác Dải Yếm')
  })
})

describe('markerHtml', () => {
  const CLOSE = LABEL_ZOOM

  it('carries the place name as a visible label when zoomed in', () => {
    expect(markerHtml(place(), false, CLOSE)).toContain('Thác Dải Yếm')
  })

  it('carries the category colour', () => {
    expect(markerHtml(place({ category: 'Food' }), false, CLOSE)).toContain(
      categoryStyle('Food').color,
    )
  })

  it('marks the selected pin so it can be lifted above the rest', () => {
    expect(markerHtml(place(), true, CLOSE)).toContain('selected')
    expect(markerHtml(place(), false, CLOSE)).not.toContain('selected')
  })

  it('never interpolates a name unescaped', () => {
    // Names come from user input and from the geocoder, and Leaflet's DivIcon
    // takes raw HTML — so this is the boundary that has to hold.
    const html = markerHtml(place({ name: '<img src=x onerror=alert(1)>' }), false, CLOSE)

    expect(html).not.toContain('<img')
    expect(html).toContain('&lt;img')
  })

  it('reflects status through opacity', () => {
    expect(markerHtml(place({ status: 'Confirmed' }), false, CLOSE)).toContain('opacity:1')
    expect(markerHtml(place({ status: 'Idea' }), false, CLOSE)).not.toContain('opacity:1')
  })
})

describe('markerHtml — labels when the map is busy', () => {
  it('drops the label when zoomed out', () => {
    // Forty always-on labels tile into rows of white strips that cover the map
    // and each other; you can read neither the names nor the terrain.
    const html = markerHtml(place(), false, LABEL_ZOOM - 1)

    expect(html).not.toContain('Thác Dải Yếm')
    expect(html).toContain('place-pin-dot')
  })

  it('keeps the label for the selected place at any zoom', () => {
    // Whatever you just picked has to be identifiable, and it is exactly one
    // label, so it cannot cause the pile-up.
    expect(markerHtml(place(), true, LABEL_ZOOM - 5)).toContain('Thác Dải Yếm')
  })

  it('still carries colour and status when the label is dropped', () => {
    const html = markerHtml(place({ category: 'Food', status: 'Confirmed' }), false, LABEL_ZOOM - 1)

    expect(html).toContain(categoryStyle('Food').color)
    expect(html).toContain('opacity:1')
  })
})
