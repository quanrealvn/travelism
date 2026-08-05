import { useEffect, useState } from 'react'

/**
 * The breakpoint below which the wishlist shows the map and the list one at a
 * time rather than side by side. Kept in step with the 64rem query in
 * styles.css — this is the one place layout is a behavioural question and not
 * only a visual one, because on a narrow screen selecting a place has to bring
 * the map into view before flying to it.
 */
const NARROW = '(max-width: 63.999rem)'

export function useNarrowScreen(): boolean {
  const [narrow, setNarrow] = useState(
    () => typeof window !== 'undefined' && window.matchMedia(NARROW).matches,
  )

  useEffect(() => {
    const query = window.matchMedia(NARROW)
    const update = (event: MediaQueryListEvent) => setNarrow(event.matches)

    // Re-read on mount as well as on change: a resize between the initial
    // render and this effect would otherwise leave the state stale.
    setNarrow(query.matches)
    query.addEventListener('change', update)
    return () => query.removeEventListener('change', update)
  }, [])

  return narrow
}
