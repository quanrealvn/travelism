import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import { PlaceForm } from './PlaceForm'
import { renderWithQuery } from '../test/renderWithQuery'
import type { GeocodeResultResponse } from '../api/api-types'

const RESULT: GeocodeResultResponse = {
  name: 'Thác Dải Yếm',
  displayName: 'Thác Dải Yếm, Mộc Châu, Sơn La, Việt Nam',
  lat: 20.8333,
  lng: 104.6667,
  kind: 'waterfall',
}

function renderForm(onSubmit = vi.fn()) {
  const { user } = renderWithQuery(
    <PlaceForm
      tripId="t1"
      currencyExponent={0}
      pending={false}
      fieldErrors={{}}
      submitError={null}
      onSubmit={onSubmit}
    />,
  )
  return { onSubmit, user }
}

function mockSearch(body: GeocodeResultResponse[]) {
  vi.stubGlobal(
    'fetch',
    vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => body } as Response),
  )
}

const searchBox = () => screen.getByLabelText(/tìm địa điểm theo tên/i)
const nameBox = () => screen.getByLabelText(/^tên$/i)
const submit = () => screen.getByRole('button', { name: /thêm vào wishlist/i })

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('PlaceForm', () => {
  it('fills name and coordinates from a picked search result', async () => {
    mockSearch([RESULT])
    const { onSubmit, user } = renderForm()

    await user.type(searchBox(), 'thac')
    await user.click(await screen.findByText('Thác Dải Yếm'))

    // The chosen address is confirmed on screen so the user can see which of
    // several same-named places they picked.
    expect(screen.getByTestId('picked-location')).toHaveTextContent('Mộc Châu')

    await user.click(submit())

    expect(onSubmit).toHaveBeenCalledTimes(1)
    expect(onSubmit.mock.calls[0]?.[0]).toMatchObject({
      name: 'Thác Dải Yếm',
      lat: 20.8333,
      lng: 104.6667,
    })
  })

  it('does not overwrite a name the user typed themselves', async () => {
    mockSearch([RESULT])
    const { onSubmit, user } = renderForm()

    await user.type(nameBox(), 'Chỗ ăn trưa')
    await user.type(searchBox(), 'thac')
    await user.click(await screen.findByText('Thác Dải Yếm'))

    await user.click(submit())

    expect(onSubmit.mock.calls[0]?.[0]).toMatchObject({
      name: 'Chỗ ăn trưa',
      lat: 20.8333,
    })
  })

  it('refuses to submit without coordinates and says what to do', async () => {
    mockSearch([])
    const { onSubmit, user } = renderForm()

    await user.type(nameBox(), 'Somewhere')
    await user.click(submit())

    expect(onSubmit).not.toHaveBeenCalled()
    expect(screen.getByRole('alert')).toHaveTextContent(/chọn một địa điểm|nhập toạ độ/i)
  })

  it('still accepts coordinates typed by hand', async () => {
    // Search is a convenience: a place missing from OpenStreetMap, or an
    // outage, must not block adding it.
    mockSearch([])
    const { onSubmit, user } = renderForm()

    await user.click(screen.getByRole('button', { name: /nhập toạ độ thủ công/i }))
    await user.type(screen.getByLabelText(/vĩ độ/i), '20.5')
    await user.type(screen.getByLabelText(/kinh độ/i), '104.5')
    await user.type(nameBox(), 'Chỗ bí mật')

    await user.click(submit())

    expect(onSubmit.mock.calls[0]?.[0]).toMatchObject({
      name: 'Chỗ bí mật',
      lat: 20.5,
      lng: 104.5,
    })
  })

  it('rejects an unparseable cost before hitting the server', async () => {
    mockSearch([RESULT])
    const { onSubmit, user } = renderForm()

    await user.type(searchBox(), 'thac')
    await user.click(await screen.findByText('Thác Dải Yếm'))
    await user.type(screen.getByLabelText(/chi phí ước tính/i), 'rất nhiều')

    await user.click(submit())

    expect(onSubmit).not.toHaveBeenCalled()
    expect(screen.getByRole('alert')).toHaveTextContent(/chi phí/i)
  })

  it('refuses to submit with no time slot selected', async () => {
    mockSearch([RESULT])
    const { onSubmit, user } = renderForm()

    await user.type(searchBox(), 'thac')
    await user.click(await screen.findByText('Thác Dải Yếm'))
    // Morning is on by default; turning it off leaves none selected.
    await user.click(screen.getByLabelText('Morning'))

    await user.click(submit())

    expect(onSubmit).not.toHaveBeenCalled()
    expect(screen.getByRole('alert')).toHaveTextContent(/ít nhất một buổi/i)
  })
})
