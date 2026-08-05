import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PlaceDetail } from './PlaceDetail'
import type { PlaceResponse } from '../api/api-types'

function place(overrides: Partial<PlaceResponse> = {}): PlaceResponse {
  return {
    id: 'p1',
    tripId: 't1',
    name: 'Thác Dải Yếm',
    lat: 20.8333,
    lng: 104.6667,
    category: 'Sight',
    timeSlots: ['Morning'],
    estimatedDurationMinutes: 90,
    estimatedCost: 50_000,
    openHoursText: null,
    description: null,
    references: [],
    status: 'Idea',
    skipReason: null,
    isDeleted: false,
    likedByMemberIds: [],
    createdAt: '2026-03-01T00:00:00+00:00',
    updatedAt: '2026-03-01T00:00:00+00:00',
    updatedByMemberId: 'm1',
    ...overrides,
  }
}

function renderDetail(overrides: Partial<PlaceResponse> = {}) {
  const onSave = vi.fn()
  render(<PlaceDetail place={place(overrides)} saving={false} onSave={onSave} />)
  return onSave
}

describe('PlaceDetail — reading', () => {
  it('shows the description', () => {
    renderDetail({ description: 'Đi buổi sáng thì mát' })

    expect(screen.getByText('Đi buổi sáng thì mát')).toBeInTheDocument()
  })

  it('shows each link by its display name, not its raw URL', () => {
    renderDetail({
      references: [
        {
          id: 'r1',
          url: 'https://vnexpress.net/a-very-long-article-slug-nobody-wants-to-read',
          label: null,
          displayName: 'vnexpress.net',
        },
      ],
    })

    const link = screen.getByRole('link')
    expect(link).toHaveTextContent('vnexpress.net')
    expect(link).toHaveAttribute('href', expect.stringContaining('vnexpress.net'))
  })

  it('opens links in a new tab without handing over the opener', () => {
    // These point at pages nobody on the trip controls.
    renderDetail({
      references: [{ id: 'r1', url: 'https://example.com', label: 'Nguồn', displayName: 'Nguồn' }],
    })

    const link = screen.getByRole('link')
    expect(link).toHaveAttribute('target', '_blank')
    expect(link).toHaveAttribute('rel', expect.stringContaining('noopener'))
    expect(link).toHaveAttribute('rel', expect.stringContaining('noreferrer'))
  })

  it('invites you to add detail when there is none', () => {
    renderDetail()

    expect(screen.getByRole('button', { name: /thêm mô tả/i })).toBeInTheDocument()
  })

  it('offers to edit when detail already exists', () => {
    renderDetail({ description: 'Có rồi' })

    expect(screen.getByRole('button', { name: /sửa mô tả/i })).toBeInTheDocument()
  })
})

describe('PlaceDetail — editing', () => {
  it('saves a description', async () => {
    const onSave = renderDetail()

    await userEvent.click(screen.getByRole('button', { name: /thêm mô tả/i }))
    await userEvent.type(screen.getByLabelText(/mô tả cho/i), 'Nhớ mang áo mưa')
    await userEvent.click(screen.getByRole('button', { name: /^lưu$/i }))

    expect(onSave).toHaveBeenCalledWith('Nhớ mang áo mưa', [])
  })

  it('saves a link with its label', async () => {
    const onSave = renderDetail()

    await userEvent.click(screen.getByRole('button', { name: /thêm mô tả/i }))
    await userEvent.type(screen.getByLabelText('Link 1'), 'https://example.com/a')
    await userEvent.type(screen.getByLabelText('Tên link 1'), 'Bài viết')
    await userEvent.click(screen.getByRole('button', { name: /^lưu$/i }))

    expect(onSave).toHaveBeenCalledWith(null, [{ url: 'https://example.com/a', label: 'Bài viết' }])
  })

  it('sends a null label when none was given', async () => {
    const onSave = renderDetail()

    await userEvent.click(screen.getByRole('button', { name: /thêm mô tả/i }))
    await userEvent.type(screen.getByLabelText('Link 1'), 'https://example.com/a')
    await userEvent.click(screen.getByRole('button', { name: /^lưu$/i }))

    expect(onSave).toHaveBeenCalledWith(null, [{ url: 'https://example.com/a', label: null }])
  })

  it('drops a blank link row rather than sending it', async () => {
    // An empty row is somebody who changed their mind, not an error.
    const onSave = renderDetail()

    await userEvent.click(screen.getByRole('button', { name: /thêm mô tả/i }))
    await userEvent.click(screen.getByRole('button', { name: /thêm link/i }))
    await userEvent.type(screen.getByLabelText('Link 1'), 'https://example.com/a')
    await userEvent.click(screen.getByRole('button', { name: /^lưu$/i }))

    expect(onSave).toHaveBeenCalledWith(null, [{ url: 'https://example.com/a', label: null }])
  })

  it('starts from the existing detail rather than blank', async () => {
    const onSave = renderDetail({
      description: 'Ghi chú cũ',
      references: [{ id: 'r1', url: 'https://old.example.com', label: 'Cũ', displayName: 'Cũ' }],
    })

    await userEvent.click(screen.getByRole('button', { name: /sửa mô tả/i }))

    expect(screen.getByLabelText(/mô tả cho/i)).toHaveValue('Ghi chú cũ')
    expect(screen.getByLabelText('Link 1')).toHaveValue('https://old.example.com')

    await userEvent.click(screen.getByRole('button', { name: /^lưu$/i }))
    expect(onSave).toHaveBeenCalledWith('Ghi chú cũ', [
      { url: 'https://old.example.com', label: 'Cũ' },
    ])
  })

  it('can remove a link', async () => {
    const onSave = renderDetail({
      references: [{ id: 'r1', url: 'https://gone.example.com', label: 'Gone', displayName: 'Gone' }],
    })

    await userEvent.click(screen.getByRole('button', { name: /sửa mô tả/i }))
    await userEvent.click(screen.getByRole('button', { name: /bỏ link 1/i }))
    await userEvent.click(screen.getByRole('button', { name: /^lưu$/i }))

    expect(onSave).toHaveBeenCalledWith(null, [])
  })

  it('discards the edit on cancel', async () => {
    const onSave = renderDetail({ description: 'Giữ nguyên' })

    await userEvent.click(screen.getByRole('button', { name: /sửa mô tả/i }))
    await userEvent.clear(screen.getByLabelText(/mô tả cho/i))
    await userEvent.type(screen.getByLabelText(/mô tả cho/i), 'Bỏ đi')
    await userEvent.click(screen.getByRole('button', { name: /huỷ/i }))

    expect(onSave).not.toHaveBeenCalled()
    expect(screen.getByText('Giữ nguyên')).toBeInTheDocument()
  })

  it('stops offering more rows at the limit', async () => {
    renderDetail({
      references: Array.from({ length: 10 }, (_, i) => ({
        id: `r${i}`,
        url: `https://example.com/${i}`,
        label: null,
        displayName: 'example.com',
      })),
    })

    await userEvent.click(screen.getByRole('button', { name: /sửa mô tả/i }))

    expect(screen.queryByRole('button', { name: /thêm link/i })).not.toBeInTheDocument()
  })
})
