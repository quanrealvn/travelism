import { useEffect, useRef } from 'react'
import type { ReactNode } from 'react'
import { IconClose } from './icons'

interface SheetProps {
  title: string
  onClose: () => void
  children: ReactNode
}

/**
 * A bottom sheet on a phone, a centred dialog from a tablet up.
 *
 * Long forms used to sit permanently expanded in the page, pushing the content
 * somebody actually came for below the fold. Putting them here means the page
 * shows the plan, and the form appears only when it is asked for.
 *
 * Escape closes it, focus moves inside on open and returns to whatever opened
 * it on close, and the page behind does not scroll while it is up.
 */
export function Sheet({ title, onClose, children }: SheetProps) {
  const panel = useRef<HTMLDivElement>(null)

  useEffect(() => {
    // Remembered before focus moves, so it can be handed back on close.
    const opener = document.activeElement as HTMLElement | null

    function handleKey(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        event.stopPropagation()
        onClose()
      }
    }

    document.addEventListener('keydown', handleKey)

    // Without this the page behind scrolls under the sheet, which reads as the
    // sheet itself being broken.
    const previousOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'

    panel.current?.querySelector<HTMLElement>(
      'input, select, textarea, button, [href]',
    )?.focus()

    return () => {
      document.removeEventListener('keydown', handleKey)
      document.body.style.overflow = previousOverflow
      opener?.focus()
    }
  }, [onClose])

  return (
    <div className="sheet">
      <button
        type="button"
        className="sheet-backdrop"
        onClick={onClose}
        // The visible close button below is the labelled way out; this exists
        // for the "tap outside to dismiss" gesture people already expect.
        aria-label={`Đóng ${title}`}
        tabIndex={-1}
      />

      <div
        className="sheet-panel"
        ref={panel}
        role="dialog"
        aria-modal="true"
        aria-label={title}
      >
        <header className="sheet-header">
          <h2>{title}</h2>
          <button type="button" className="icon-button" onClick={onClose} aria-label="Đóng">
            <IconClose />
          </button>
        </header>

        <div className="sheet-body">{children}</div>
      </div>
    </div>
  )
}
