interface SpinnerProps {
  /** What is being waited for, read out to assistive tech. */
  label?: string
  /** Fills its container and centres itself, for a whole panel that is loading. */
  block?: boolean
}

/**
 * The one spinner in the app.
 *
 * An ellipsis or the bare word "Đang tải…" does not read as motion, so on a
 * slow connection — which is most of a trip, in the places worth going — the
 * app looked frozen rather than busy. A spinner says the difference.
 *
 * It is `aria-hidden` and paired with live text rather than being announced
 * itself: a screen reader should hear "Đang tải chuyến đi", not a description
 * of a rotating circle.
 */
export function Spinner({ label, block = false }: SpinnerProps) {
  const spinner = (
    <span className="spinner" aria-hidden="true">
      <svg viewBox="0 0 24 24" focusable="false">
        <circle className="spinner-track" cx="12" cy="12" r="9" />
        <circle className="spinner-head" cx="12" cy="12" r="9" />
      </svg>
    </span>
  )

  if (!block) {
    return spinner
  }

  return (
    <p className="loading" role="status">
      {spinner}
      {label && <span>{label}</span>}
    </p>
  )
}

/**
 * A button's label while its action is in flight.
 *
 * The button keeps its size — swapping "Lưu" for "Đang lưu…" resizes it under
 * the finger that just pressed it, and on a list of them the whole row jumps.
 */
export function ButtonBusy({ children }: { children: React.ReactNode }) {
  return (
    <>
      <Spinner />
      <span>{children}</span>
    </>
  )
}
