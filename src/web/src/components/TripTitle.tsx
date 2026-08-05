import { useEffect, useRef, useState } from 'react'
import { ApiError } from '../api/client'
import { IconCheck, IconClose, IconPencil } from './icons'
import { Spinner } from './Spinner'

/** Mirrors Trip.NameMaxLength, so the server never has to refuse on length. */
const NAME_MAX = 80

interface TripTitleProps {
  name: string
  /** Resolves when the rename is saved; rejects to keep the field open. */
  onRename: (name: string) => Promise<unknown>
}

/**
 * The trip name, renamed in place.
 *
 * A trip gets its name in the first thirty seconds of existing — before anyone
 * has picked dates, let alone decided what the trip is. "Chuyến đi mới" then
 * follows it forever, because the only way to change it was an API call.
 *
 * Editing happens where the name is rather than behind a settings screen: the
 * heading is a button, it becomes an input, and Enter or blur commits. A failed
 * save keeps the field open with what you typed still in it — the one thing a
 * rename must never do is swallow the new name and show the old one.
 */
export function TripTitle({ name, onRename }: TripTitleProps) {
  const [editing, setEditing] = useState(false)
  const [draft, setDraft] = useState(name)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const inputRef = useRef<HTMLInputElement>(null)
  // Escape closes on keydown, which fires before the blur it causes. Without
  // this the blur handler would then save the very edit that was cancelled.
  const cancelled = useRef(false)

  useEffect(() => {
    if (editing) {
      inputRef.current?.focus()
      inputRef.current?.select()
    }
  }, [editing])

  function open() {
    setDraft(name)
    setError(null)
    cancelled.current = false
    setEditing(true)
  }

  function cancel() {
    cancelled.current = true
    setEditing(false)
    setError(null)
  }

  async function commit() {
    if (cancelled.current || pending) {
      return
    }

    const next = draft.trim()
    // Nothing to save, and an empty name is a mistake rather than an intent —
    // both just put the old name back.
    if (next === '' || next === name) {
      setEditing(false)
      return
    }

    setPending(true)
    setError(null)
    try {
      await onRename(next)
      setEditing(false)
    } catch (cause) {
      setError(
        cause instanceof ApiError
          ? (Object.values(cause.fieldErrors())[0] ?? cause.text)
          : 'Không đổi được tên chuyến đi.',
      )
      inputRef.current?.focus()
    } finally {
      setPending(false)
    }
  }

  if (!editing) {
    return (
      <h1 className="trip-title">
        <button
          type="button"
          className="trip-title-button"
          onClick={open}
          title="Đổi tên chuyến đi"
        >
          <span className="trip-title-text">{name}</span>
          {/* Always in the DOM, not only on hover: a control that appears on
              hover does not exist on a touch screen. */}
          <IconPencil className="trip-title-pencil" />
          <span className="visually-hidden">Đổi tên chuyến đi</span>
        </button>
      </h1>
    )
  }

  return (
    <h1 className="trip-title is-editing">
      <form
        className="trip-rename"
        onSubmit={(event) => {
          event.preventDefault()
          void commit()
        }}
      >
        <input
          ref={inputRef}
          className="trip-rename-input"
          value={draft}
          maxLength={NAME_MAX}
          disabled={pending}
          aria-label="Tên chuyến đi"
          aria-invalid={error !== null}
          onChange={(event) => setDraft(event.target.value)}
          onBlur={() => void commit()}
          onKeyDown={(event) => {
            if (event.key === 'Escape') {
              event.preventDefault()
              cancel()
            }
          }}
        />
        <span className="trip-rename-actions">
          {pending ? (
            <Spinner />
          ) : (
            <>
              {/* mousedown, not click: blur lands first and would close the
                  form out from under the button before click ever fired. */}
              <button
                type="submit"
                className="icon-button icon-button-sm"
                aria-label="Lưu tên"
                onMouseDown={(event) => {
                  event.preventDefault()
                  void commit()
                }}
              >
                <IconCheck />
              </button>
              <button
                type="button"
                className="icon-button icon-button-sm"
                aria-label="Huỷ"
                onMouseDown={(event) => {
                  event.preventDefault()
                  cancel()
                }}
              >
                <IconClose />
              </button>
            </>
          )}
        </span>
      </form>
      {error && (
        <span className="trip-rename-error" role="alert">
          {error}
        </span>
      )}
    </h1>
  )
}
