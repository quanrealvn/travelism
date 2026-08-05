import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, screen, waitFor } from '@testing-library/react'
import { PlaceSearch } from './PlaceSearch'
import { renderWithQuery } from '../test/renderWithQuery'
import type { GeocodeResultResponse } from '../api/api-types'

const RESULTS: GeocodeResultResponse[] = [
  {
    name: 'Thác Dải Yếm',
    displayName: 'Thác Dải Yếm, Mộc Châu, Sơn La, Việt Nam',
    lat: 20.8333,
    lng: 104.6667,
    kind: 'waterfall',
    distanceKm: 2.4,
  },
  {
    name: 'Đồi chè trái tim',
    displayName: 'Đồi chè trái tim, Mộc Châu, Sơn La, Việt Nam',
    lat: 20.85,
    lng: 104.65,
    kind: null,
    distanceKm: null,
  },
]

function mockFetch(handler: (url: string) => { ok: boolean; status: number; body: unknown }) {
  const spy = vi.fn().mockImplementation((url: string) => {
    const { ok, status, body } = handler(url)
    return Promise.resolve({ ok, status, json: async () => body } as Response)
  })
  vi.stubGlobal('fetch', spy)
  return spy
}

const searchBox = () => screen.getByLabelText(/tìm địa điểm theo tên hoặc dán link/i)

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('PlaceSearch', () => {
  it('does not search until the query is long enough', async () => {
    const fetchSpy = mockFetch(() => ({ ok: true, status: 200, body: RESULTS }))
    const { user } = renderWithQuery(<PlaceSearch tripId="t1" onPick={vi.fn()} />)

    await user.type(searchBox(), 'a')

    // Deliberately wait past the 400ms debounce window: a single character
    // matches nearly everything, so the request must not leave the browser even
    // once the user has stopped typing. Wrapped in act() because the debounce
    // timer itself settles component state.
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 600))
    })

    expect(fetchSpy).not.toHaveBeenCalled()
  })

  it('searches once the user pauses, and lists the matches', async () => {
    const fetchSpy = mockFetch(() => ({ ok: true, status: 200, body: RESULTS }))
    const { user } = renderWithQuery(<PlaceSearch tripId="t1" onPick={vi.fn()} />)

    await user.type(searchBox(), 'thac')

    expect(await screen.findByText('Thác Dải Yếm')).toBeInTheDocument()
    expect(screen.getByText('Đồi chè trái tim')).toBeInTheDocument()
    expect(screen.getByText('waterfall')).toBeInTheDocument()

    // Debounced: four keystrokes must not become four requests.
    expect(fetchSpy).toHaveBeenCalledTimes(1)
    expect(fetchSpy.mock.calls[0]?.[0]).toContain('/trips/t1/places/search?q=thac')
  })

  it('hands the chosen result — coordinates included — to the caller', async () => {
    mockFetch(() => ({ ok: true, status: 200, body: RESULTS }))
    const onPick = vi.fn()
    const { user } = renderWithQuery(<PlaceSearch tripId="t1" onPick={onPick} />)

    await user.type(searchBox(), 'thac')
    await user.click(await screen.findByText('Thác Dải Yếm'))

    expect(onPick).toHaveBeenCalledWith(RESULTS[0])
  })

  it('clears the results once a pick is made', async () => {
    mockFetch(() => ({ ok: true, status: 200, body: RESULTS }))
    const { user } = renderWithQuery(<PlaceSearch tripId="t1" onPick={vi.fn()} />)

    await user.type(searchBox(), 'thac')
    await user.click(await screen.findByText('Thác Dải Yếm'))

    await waitFor(() => {
      expect(screen.queryByLabelText('Kết quả tìm kiếm')).not.toBeInTheDocument()
    })
  })

  it('offers the working alternatives when the geocoder is down', async () => {
    mockFetch(() => ({
      ok: false,
      status: 502,
      body: { status: 502, code: 'GEOCODING_UNAVAILABLE', detail: 'upstream down' },
    }))
    const { user } = renderWithQuery(<PlaceSearch tripId="t1" onPick={vi.fn()} />)

    await user.type(searchBox(), 'thac')

    // An outage must not be a dead end: pasting a link and clicking the map
    // both still work.
    //
    // Found by its text rather than by role: the busy indicator is a status
    // too, so a role query would race it and sometimes assert against
    // "Đang tìm…".
    const message = (await screen.findByText(/không tìm được lúc này/i)).closest('p')
    expect(message).toHaveTextContent(/dán link/i)
    expect(message).toHaveTextContent(/bấm lên bản đồ/i)
  })

  it('reports an empty result set with advice that actually helps', async () => {
    mockFetch(() => ({ ok: true, status: 200, body: [] }))
    const { user } = renderWithQuery(<PlaceSearch tripId="t1" onPick={vi.fn()} />)

    await user.type(searchBox(), 'Tiểu khu 32 thị trấn Nông Trường')

    // Naming the failed query and pointing at what does work — a shorter name,
    // a pasted link, or the map — beats a bare "no results".
    const hint = (await screen.findByText(/không tìm thấy/i)).closest('p')
    expect(hint).toHaveTextContent(/không tìm thấy/i)
    expect(hint).toHaveTextContent(/tên ngắn hơn/i)
    expect(hint).toHaveTextContent(/dán link/i)
    expect(hint).toHaveTextContent(/bấm thẳng lên bản đồ/i)
    expect(screen.queryByLabelText('Kết quả tìm kiếm')).not.toBeInTheDocument()
  })

  it('shows how far each match is from the trip', async () => {
    mockFetch(() => ({ ok: true, status: 200, body: RESULTS }))
    const { user } = renderWithQuery(<PlaceSearch tripId="t1" onPick={vi.fn()} />)

    await user.type(searchBox(), 'thac')

    expect(await screen.findByText('2.4 km')).toBeInTheDocument()
  })

  it('flags a match too far away to be the place that was meant', async () => {
    // Searching a Vietnamese name can return a confident match on another
    // continent; the distance is what makes that visibly wrong.
    mockFetch(() => ({
      ok: true,
      status: 200,
      body: [
        {
          name: 'Tiểu khu 32',
          displayName: '32, 高雄市, 臺灣',
          lat: 22.6273,
          lng: 120.3014,
          kind: 'road',
          distanceKm: 1632,
        },
      ],
    }))
    const { user } = renderWithQuery(<PlaceSearch tripId="t1" onPick={vi.fn()} />)

    await user.type(searchBox(), 'tieu khu 32')

    expect(await screen.findByText(/xa chuyến đi/i)).toBeInTheDocument()
    expect(screen.getByText('1.632 km')).toBeInTheDocument()
  })

  it('resolves a pasted map link instead of searching for it as a name', async () => {
    const fetchSpy = mockFetch(() => ({
      ok: true,
      status: 200,
      body: {
        name: 'Thác Dải Yếm',
        // A link has no address, so the server sends the coordinates here.
        displayName: '20.817975, 104.591686',
        lat: 20.817975,
        lng: 104.591686,
        kind: 'link',
        distanceKm: 2.1,
      },
    }))
    const onPick = vi.fn()
    const { user } = renderWithQuery(<PlaceSearch tripId="t1" onPick={onPick} />)

    await user.click(searchBox())
    await user.paste('https://maps.app.goo.gl/AbCdEf123')

    await user.click(await screen.findByText('Thác Dải Yếm'))

    expect(onPick).toHaveBeenCalledWith(
      expect.objectContaining({ lat: 20.817975, lng: 104.591686 }),
    )
    // The paste must go to the resolver, not the name search.
    expect(fetchSpy.mock.calls[0]?.[0]).toBe('/trips/t1/places/resolve-link')
  })

  it('sends a pasted coordinate pair to the resolver too', async () => {
    const fetchSpy = mockFetch(() => ({
      ok: true,
      status: 200,
      body: {
        name: '',
        displayName: '20.8386, 104.6383',
        lat: 20.8386,
        lng: 104.6383,
        kind: 'link',
        distanceKm: null,
      },
    }))
    const { user } = renderWithQuery(<PlaceSearch tripId="t1" onPick={vi.fn()} />)

    await user.click(searchBox())
    await user.paste('20.8386, 104.6383')

    // A location with no name still needs something clickable.
    expect(await screen.findByText(/vị trí từ link/i)).toBeInTheDocument()
    expect(fetchSpy.mock.calls[0]?.[0]).toBe('/trips/t1/places/resolve-link')
  })

  it('explains a link that carries no location', async () => {
    mockFetch(() => ({
      ok: false,
      status: 422,
      body: { status: 422, code: 'LINK_NOT_RECOGNISED', detail: 'no location' },
    }))
    const { user } = renderWithQuery(<PlaceSearch tripId="t1" onPick={vi.fn()} />)

    await user.click(searchBox())
    await user.paste('https://maps.app.goo.gl/broken')

    expect(await screen.findByText(/không chứa vị trí/i)).toBeInTheDocument()
  })

  it('tells the user that pasting a link is an option', () => {
    mockFetch(() => ({ ok: true, status: 200, body: [] }))
    renderWithQuery(<PlaceSearch tripId="t1" onPick={vi.fn()} />)

    expect(screen.getByText(/dán link vào đây/i)).toBeInTheDocument()
  })

  it('does not flag a nearby match', async () => {
    mockFetch(() => ({ ok: true, status: 200, body: RESULTS }))
    const { user } = renderWithQuery(<PlaceSearch tripId="t1" onPick={vi.fn()} />)

    await user.type(searchBox(), 'thac')
    await screen.findByText('Thác Dải Yếm')

    expect(screen.queryByText(/xa chuyến đi/i)).not.toBeInTheDocument()
  })
})
