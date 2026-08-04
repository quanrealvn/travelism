import { useEffect, useState } from 'react'

/**
 * Delays a search term so the lookup fires once the user pauses rather than on
 * every keystroke. Nominatim is a free shared service with a strict rate
 * policy, so this is politeness as much as performance.
 *
 * Two cases deliberately skip the delay:
 * - an unchanged value, which would otherwise schedule a timer that resolves to
 *   the state it already holds;
 * - clearing the box, where there is nothing to search for and holding stale
 *   results on screen for another 400ms would just look broken.
 */
export function useDebouncedSearchTerm(value: string, delayMs: number): string {
  const [debounced, setDebounced] = useState(value)

  useEffect(() => {
    if (value === debounced) {
      return
    }

    if (value === '') {
      setDebounced('')
      return
    }

    const timer = setTimeout(() => setDebounced(value), delayMs)
    return () => clearTimeout(timer)
  }, [value, delayMs, debounced])

  return debounced
}
