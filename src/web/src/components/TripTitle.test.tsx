import { describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { TripTitle } from './TripTitle'
import { ApiError } from '../api/client'

function renderTitle(
  onRename: (name: string) => Promise<unknown> = vi.fn().mockResolvedValue(undefined),
) {
  render(<TripTitle name="Chuyến đi mới" onRename={onRename} />)
  return onRename
}

const field = () => screen.getByRole('textbox', { name: /tên chuyến đi/i })

async function startEditing() {
  await userEvent.click(screen.getByRole('button', { name: /đổi tên chuyến đi/i }))
  return field()
}

describe('TripTitle', () => {
  it('shows the name as a control, not just a heading', () => {
    renderTitle()

    expect(screen.getByRole('button', { name: /đổi tên chuyến đi/i })).toHaveTextContent(
      'Chuyến đi mới',
    )
  })

  it('opens with the current name, ready to be replaced', async () => {
    renderTitle()

    expect(await startEditing()).toHaveValue('Chuyến đi mới')
  })

  it('saves on Enter', async () => {
    const onRename = renderTitle()

    const input = await startEditing()
    await userEvent.clear(input)
    await userEvent.type(input, 'Đà Lạt tháng 3{Enter}')

    expect(onRename).toHaveBeenCalledWith('Đà Lạt tháng 3')
  })

  it('saves on blur, because tapping away is how a phone commits', async () => {
    const onRename = renderTitle()

    const input = await startEditing()
    await userEvent.clear(input)
    await userEvent.type(input, 'Sa Pa')
    await userEvent.tab()

    await waitFor(() => expect(onRename).toHaveBeenCalledWith('Sa Pa'))
  })

  it('discards the edit on Escape, including the blur it causes', async () => {
    const onRename = renderTitle()

    const input = await startEditing()
    await userEvent.clear(input)
    await userEvent.type(input, 'Nha Trang{Escape}')

    expect(onRename).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: /đổi tên chuyến đi/i })).toHaveTextContent(
      'Chuyến đi mới',
    )
  })

  it('treats an emptied field as a mistake and puts the name back', async () => {
    const onRename = renderTitle()

    const input = await startEditing()
    await userEvent.clear(input)
    await userEvent.type(input, '{Enter}')

    expect(onRename).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: /đổi tên chuyến đi/i })).toBeInTheDocument()
  })

  it('does not spend a request on an unchanged name', async () => {
    const onRename = renderTitle()

    const input = await startEditing()
    await userEvent.type(input, '{Enter}')

    expect(onRename).not.toHaveBeenCalled()
  })

  it('trims, so a stray space does not become part of the name', async () => {
    const onRename = renderTitle()

    const input = await startEditing()
    await userEvent.clear(input)
    await userEvent.type(input, '  Hội An  {Enter}')

    expect(onRename).toHaveBeenCalledWith('Hội An')
  })

  it('keeps what you typed when the server refuses', async () => {
    // The one thing a rename must never do is swallow the new name and show
    // the old one back with no explanation.
    const onRename = vi.fn().mockRejectedValue(
      new ApiError(422, {
        status: 422,
        code: 'VALIDATION_FAILED',
        errors: [{ field: 'name', code: 'TOO_LONG', message: 'Tên quá dài.' }],
      }),
    )
    renderTitle(onRename)

    const input = await startEditing()
    await userEvent.clear(input)
    await userEvent.type(input, 'Một cái tên rất dài{Enter}')

    expect(await screen.findByRole('alert')).toHaveTextContent('Tên quá dài.')
    expect(field()).toHaveValue('Một cái tên rất dài')
  })
})
