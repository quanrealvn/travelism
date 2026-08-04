import { render } from '@testing-library/react'
import type { RenderResult } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { UserEvent } from '@testing-library/user-event'
import type { ReactElement } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'

/**
 * Renders a component under a QueryClient created exactly once per call.
 *
 * Building the client inside an inline `wrapper` function instead would
 * construct a new one on every render of the wrapper, tearing down and
 * resubscribing every observer mid-test — which shows up as a storm of
 * "update not wrapped in act" warnings that have nothing to do with the
 * component being tested.
 */
export function renderWithQuery(ui: ReactElement): RenderResult & { user: UserEvent } {
  const client = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
        gcTime: 0,
        // No background refetching in tests: an update landing after the
        // assertions is noise, not behaviour worth asserting.
        refetchOnWindowFocus: false,
        refetchOnMount: false,
        refetchOnReconnect: false,
      },
    },
  })

  // userEvent.setup() rather than the direct userEvent.type() API: only the
  // configured instance routes interactions through Testing Library's act
  // wrapper, so without it every keystroke updates state outside act.
  const user = userEvent.setup()

  return {
    user,
    ...render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>),
  }
}
