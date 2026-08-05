import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { ButtonBusy, Spinner } from './Spinner'

describe('Spinner', () => {
  it('is hidden from assistive tech', () => {
    // A screen reader should hear what is being waited for, not a description
    // of a rotating circle.
    const { container } = render(<Spinner />)

    expect(container.querySelector('.spinner')).toHaveAttribute('aria-hidden', 'true')
  })

  it('announces the wait when it stands for a whole panel', () => {
    render(<Spinner block label="Đang tải chuyến đi…" />)

    expect(screen.getByRole('status')).toHaveTextContent('Đang tải chuyến đi…')
  })

  it('says nothing at all when it is only decoration beside other text', () => {
    const { container } = render(<Spinner />)

    expect(container).not.toHaveTextContent(/\w/)
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })
})

describe('ButtonBusy', () => {
  it('keeps the label visible while the action is in flight', () => {
    // A button that empties out while it works loses the only clue about what
    // is happening.
    render(
      <button type="button">
        <ButtonBusy>Đang lưu…</ButtonBusy>
      </button>,
    )

    expect(screen.getByRole('button')).toHaveTextContent('Đang lưu…')
  })
})
