import type { PlaceCategory, PlaceResponse, PlaceStatus } from '../api/api-types'
import { placeCategoryLabel } from '../api/labels'

export interface CategoryStyle {
  /** Marker fill. Chosen to stay legible on OpenStreetMap's light tiles. */
  color: string
  /**
   * The icon's path data, drawn in a 24-unit box with a 1.75 stroke.
   *
   * Path data rather than emoji: emoji carry their own multi-colour artwork, so
   * a 🍜 on a rose tile and a stroked ⛰ on a mint tile sat in identical tiles
   * looking like two different design systems — and they were heavier than
   * every other icon in the app. Shared between React and the map's DivIcon,
   * which takes a raw HTML string.
   */
  path: string
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
  // A bowl with steam.
  Food: {
    color: '#be123c',
    path: 'M4 11h16a8 8 0 0 1-16 0ZM6 19h12M9.5 4.5c-1 1-1 2 0 3M14.5 4.5c-1 1-1 2 0 3',
    label: placeCategoryLabel('Food'),
  },
  // Peaks with a sun.
  Sight: {
    color: '#047857',
    path: 'm3 18 5.5-8 3.5 5 2.5-3.5L21 18Zm14-9.5a1.6 1.6 0 1 0 0-3.2 1.6 1.6 0 0 0 0 3.2Z',
    label: placeCategoryLabel('Sight'),
  },
  // A camera body with a lens.
  Photo: {
    color: '#6d28d9',
    path: 'M3 8.5A1.5 1.5 0 0 1 4.5 7h2L8 5h8l1.5 2h2A1.5 1.5 0 0 1 21 8.5v9A1.5 1.5 0 0 1 19.5 19h-15A1.5 1.5 0 0 1 3 17.5ZM12 15.5a3 3 0 1 0 0-6 3 3 0 0 0 0 6Z',
    label: placeCategoryLabel('Photo'),
  },
  // A bed.
  Rest: {
    color: '#1d4ed8',
    path: 'M3 18v-7m0 4h18m0 3v-6a2 2 0 0 0-2-2h-8v8M6.5 11.5a1.6 1.6 0 1 0 0-3.2 1.6 1.6 0 0 0 0 3.2Z',
    label: placeCategoryLabel('Rest'),
  },
  // A map pin.
  Other: {
    color: '#52525f',
    path: 'M20 10c0 4.5-8 12-8 12s-8-7.5-8-12a8 8 0 1 1 16 0Zm-8 3a3 3 0 1 0 0-6 3 3 0 0 0 0 6Z',
    label: placeCategoryLabel('Other'),
  },
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

/**
 * Builds the marker's inner HTML: a coloured pin, and a name when there is room
 * for one.
 *
 * Every pin carrying an always-on label works for six places and fails
 * completely for forty: the labels tile over each other into rows of white
 * strips, and you can read neither the names nor the map underneath. So a label
 * is drawn for the selected place always, and otherwise only while the map is
 * zoomed in far enough that the pins are genuinely apart.
 */
export const LABEL_ZOOM = 13

export function markerHtml(place: PlaceResponse, selected: boolean, zoom: number): string {
  const style = categoryStyle(place.category)
  const classes = ['place-pin', selected ? 'selected' : ''].filter(Boolean).join(' ')
  const label =
    selected || zoom >= LABEL_ZOOM
      ? `<span class="place-pin-label">${escapeHtml(place.name)}</span>`
      : ''

  return (
    `<span class="${classes}" style="--pin-color:${style.color};` +
    `opacity:${statusOpacity(place.status)}">` +
    `<span class="place-pin-dot" aria-hidden="true">${categorySvg(style)}</span>` +
    label +
    `</span>`
  )
}

/**
 * The category icon as an SVG string, for Leaflet's DivIcon.
 *
 * The path data is ours, not user input, so it is interpolated directly — the
 * place name beside it is the untrusted part and goes through escapeHtml.
 */
export function categorySvg(style: CategoryStyle): string {
  return (
    `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.75" ` +
    `stroke-linecap="round" stroke-linejoin="round" focusable="false">` +
    `<path d="${style.path}"/></svg>`
  )
}
