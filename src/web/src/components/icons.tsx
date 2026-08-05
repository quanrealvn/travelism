/**
 * Inline icons.
 *
 * Drawn here rather than pulled from a package: the app needs nine glyphs, and
 * nine glyphs are not worth a dependency, a bundle, or a network fetch. All of
 * them share a 24-unit box and a 1.75 stroke so they sit on the same optical
 * weight as the surrounding text.
 *
 * Every icon is decorative — the label or aria-label beside it carries the
 * meaning — so each is hidden from assistive tech.
 */

interface IconProps {
  className?: string
}

function svgProps(className?: string) {
  return {
    className,
    viewBox: '0 0 24 24',
    fill: 'none',
    stroke: 'currentColor',
    strokeWidth: 1.75,
    strokeLinecap: 'round' as const,
    strokeLinejoin: 'round' as const,
    'aria-hidden': true,
    focusable: false,
  }
}

/** Wishlist: a pin, because a wishlist is a set of places. */
export function IconPin({ className }: IconProps) {
  return (
    <svg {...svgProps(className)}>
      <path d="M20 10c0 4.5-8 12-8 12s-8-7.5-8-12a8 8 0 1 1 16 0Z" />
      <circle cx="12" cy="10" r="3" />
    </svg>
  )
}

/** Itinerary: a calendar, because the plan is a set of days. */
export function IconCalendar({ className }: IconProps) {
  return (
    <svg {...svgProps(className)}>
      <rect x="3" y="5" width="18" height="16" rx="2" />
      <path d="M3 10h18M8 3v4M16 3v4" />
    </svg>
  )
}

/** Money: a wallet. */
export function IconWallet({ className }: IconProps) {
  return (
    <svg {...svgProps(className)}>
      <path d="M3 8a2 2 0 0 1 2-2h13a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2Z" />
      <path d="M3 8V7a2 2 0 0 1 2-2h11" />
      <circle cx="16.5" cy="12.5" r="1.25" />
    </svg>
  )
}

/** Activity: a heartbeat line — what changed, and when. */
export function IconPulse({ className }: IconProps) {
  return (
    <svg {...svgProps(className)}>
      <path d="M3 12h4l2.5-7 5 14L17 12h4" />
    </svg>
  )
}

export function IconPlus({ className }: IconProps) {
  return (
    <svg {...svgProps(className)}>
      <path d="M12 5v14M5 12h14" />
    </svg>
  )
}

export function IconClose({ className }: IconProps) {
  return (
    <svg {...svgProps(className)}>
      <path d="M6 6l12 12M18 6 6 18" />
    </svg>
  )
}

/** Opens something outside the app. */
export function IconExternal({ className }: IconProps) {
  return (
    <svg {...svgProps(className)}>
      <path d="M14 4h6v6" />
      <path d="M20 4 11 13" />
      <path d="M18 14v4a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4" />
    </svg>
  )
}

/** The trip's own details: dates, budget, invite code, who is coming. */
export function IconInfo({ className }: IconProps) {
  return (
    <svg {...svgProps(className)}>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 11v5" />
      <circle cx="12" cy="7.75" r="0.9" fill="currentColor" stroke="none" />
    </svg>
  )
}

export function IconCopy({ className }: IconProps) {
  return (
    <svg {...svgProps(className)}>
      <rect x="9" y="9" width="12" height="12" rx="2" />
      <path d="M6 15H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h8a2 2 0 0 1 2 2v1" />
    </svg>
  )
}

export function IconPencil({ className }: IconProps) {
  return (
    <svg {...svgProps(className)}>
      <path d="M4 20h4L19 9a2.1 2.1 0 0 0-3-3L5 17v3Z" />
      <path d="m14.5 6.5 3 3" />
    </svg>
  )
}

/** A reference somebody saved to explain why a place is on the list. */
export function IconLink({ className }: IconProps) {
  return (
    <svg {...svgProps(className)}>
      <path d="M10 13a4 4 0 0 0 5.7.4l3-3a4 4 0 0 0-5.7-5.7l-1.5 1.5" />
      <path d="M14 11a4 4 0 0 0-5.7-.4l-3 3a4 4 0 1 0 5.7 5.7l1.5-1.5" />
    </svg>
  )
}

export function IconCheck({ className }: IconProps) {
  return (
    <svg {...svgProps(className)}>
      <path d="m5 13 4.5 4.5L19 7" />
    </svg>
  )
}
