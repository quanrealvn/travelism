/**
 * Where the wishlist sheet can rest over the map, below 1024px.
 *
 * Three stops rather than free positioning: a sheet you can leave anywhere is a
 * sheet you have to aim, and the three useful answers are "mostly map", "both",
 * and "mostly list". The heights themselves live in the stylesheet, keyed off
 * `data-snap` — layout belongs there, and the transition between stops is a CSS
 * concern the component should not be recomputing.
 */
export type SnapPoint = 'peek' | 'half' | 'full'

/** Smallest to largest, which is the order the grip cycles through. */
export const SNAP_ORDER: SnapPoint[] = ['peek', 'half', 'full']
