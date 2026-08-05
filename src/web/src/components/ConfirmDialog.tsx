import type { ReactNode } from 'react'
import { Sheet } from './Sheet'
import { ButtonBusy } from './Spinner'

interface ConfirmDialogProps {
  title: string
  /** What is about to happen, in enough detail to decide. */
  children: ReactNode
  confirmLabel: string
  cancelLabel?: string
  /** Colours the confirm button as destructive rather than as the happy path. */
  destructive?: boolean
  pending?: boolean
  onConfirm: () => void
  onCancel: () => void
}

/**
 * A question with two answers, in the same sheet the rest of the app uses.
 *
 * `window.confirm` would have been shorter, but it renders in the browser's
 * language rather than the app's, cannot say which days a place is scheduled
 * on, and looks like a phishing prompt on a phone.
 *
 * Cancel is the primary-looking button and sits first, because these dialogs
 * only appear in front of something irreversible — leaving is the safe answer
 * and should be the easy one.
 */
export function ConfirmDialog({
  title,
  children,
  confirmLabel,
  cancelLabel = 'Huỷ',
  destructive = false,
  pending = false,
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
  return (
    <Sheet title={title} onClose={onCancel}>
      <div className="confirm-body">{children}</div>

      <div className="confirm-actions">
        <button type="button" className="button-primary" onClick={onCancel} disabled={pending}>
          {cancelLabel}
        </button>
        <button
          type="button"
          className={destructive ? 'button-danger' : undefined}
          onClick={onConfirm}
          disabled={pending}
        >
          {pending ? <ButtonBusy>Đang xoá…</ButtonBusy> : confirmLabel}
        </button>
      </div>
    </Sheet>
  )
}
