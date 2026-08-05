import type { PlaceCategory, PlaceResponse, PlaceStatus } from '../api/api-types'
import { placeCategoryLabel } from '../api/labels'

export interface CategoryStyle {
  /** Marker fill. Chosen to stay legible on OpenStreetMap's light tiles. */
  color: string
  /** A glyph inside the pin, so colour is never the only signal. */
  glyph: string
  label: string
}

/**
 * Colour and glyph per category.
 *
 * Category rather than status is the map's axis: on a map you want to see
 * "the food is all on this side, the sights are up the valley". Status is
 * already the organising idea of the wishlist, and using it here too would
 * mean two colour scales competing for the same pixels.
 *
 * Every entry carries a glyph as well as a colour — about 1 in 12 men cannot
 * reliably separate the red and green ones, and a map that only encodes
 * meaning in hue is unreadable for them.
 */
const CATEGORY_STYLES: Record<PlaceCategory, CategoryStyle> = {
  Food: { color: '#d1495b', glyph: '🍜', label: placeCategoryLabel('Food') },
  Sight: { color: '#1f6f5c', glyph: '⛰', label: placeCategoryLabel('Sight') },
  Photo: { color: '#7b4fb5', glyph: '📷', label: placeCategoryLabel('Photo') },
  Rest: { color: '#2b6cb0', glyph: '🛏', label: placeCategoryLabel('Rest') },
  Other: { color: '#6b7280', glyph: '📍', label: placeCategoryLabel('Other') },
}

export function categoryStyle(category: PlaceCategory): CategoryStyle {
  return CATEGORY_STYLES[category] ?? CATEGORY_STYLES.Other
}

export function allCategoryStyles(): (CategoryStyle & { category: PlaceCategory })[] {
  return (Object.keys(CATEGORY_STYLES) as PlaceCategory[]).map((category) => ({
    category,
    ...CATEGORY_STYLES[category],
  }))
}

/**
 * How settled a place is, expressed as opacity rather than a second colour.
 * A confirmed stop should read as solid; an idea nobody has backed yet should
 * recede without becoming invisible.
 */
export function statusOpacity(status: PlaceStatus): number {
  switch (status) {
    case 'Confirmed':
      return 1
    case 'Shortlist':
      return 0.85
    case 'Visited':
    case 'Skipped':
      return 0.55
    default:
      return 0.7
  }
}

/**
 * Escapes text for interpolation into marker HTML.
 *
 * Leaflet's DivIcon takes a raw HTML string, so a place name is untrusted
 * markup the moment it is placed in one. Names come from user input and from
 * the geocoder, and a stray quote would break the element even without malice.
 */
export function escapeHtml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;')
}

/** Builds the marker's inner HTML: a coloured pin plus the place name. */
export function markerHtml(place: PlaceResponse, selected: boolean): string {
  const style = categoryStyle(place.category)
  const classes = ['place-pin', selected ? 'selected' : ''].filter(Boolean).join(' ')

  return (
    `<span class="${classes}" style="--pin-color:${style.color};` +
    `opacity:${statusOpacity(place.status)}">` +
    `<span class="place-pin-dot" aria-hidden="true">${style.glyph}</span>` +
    `<span class="place-pin-label">${escapeHtml(place.name)}</span>` +
    `</span>`
  )
}
