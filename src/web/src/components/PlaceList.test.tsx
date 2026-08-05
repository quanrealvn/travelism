import { describe, expect, it, vi } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PlaceList } from './PlaceList'
import type { MemberResponse, PlaceResponse } from '../api/api-types'

const ME = 'member-1'
const OTHER = 'member-2'

const MEMBERS: MemberResponse[] = [
  { id: ME, displayName: 'Quan', role: 'Owner', createdAt: '2026-03-01T00:00:00+00:00' },
  { id: OTHER, displayName: 'Linh', role: 'Editor', createdAt: '2026-03-01T00:00:00+00:00' },
]

function place(overrides: Partial<PlaceResponse> = {}): PlaceResponse {
  return {
    id: 'p1',
    tripId: 't1',
    name: 'Thác Dải Yếm',
    lat: 20.8333,
    lng: 104.6667,
    category: 'Sight',
    timeSlots: ['Morning', 'Afternoon'],
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
    updatedByMemberId: ME,
    ...overrides,
  }
}

function renderList(places: PlaceResponse[], overrides: Partial<Parameters<typeof PlaceList>[0]> = {}) {
  const handlers = {
    onSelect: vi.fn(),
    onDelete: vi.fn(),
    onToggleLike: vi.fn(),
    onChangeStatus: vi.fn(),
    onSaveDetail: vi.fn(),
  }

  render(
    <PlaceList
      places={places}
      members={MEMBERS}
      myMemberId={ME}
      currency="VND"
      currencyExponent={0}
      selectedPlaceId={null}
      showsOnMap={false}
      onShowOnMap={vi.fn()}
      deletingPlaceId={null}
      busyPlaceId={null}
      tripUnderway={false}
      {...handlers}
      {...overrides}
    />,
  )

  return handlers
}

describe('PlaceList', () => {
  it('shows an empty state when there is nothing to plan yet', () => {
    renderList([])

    expect(screen.getByText(/chưa có địa điểm nào/i)).toBeInTheDocument()
  })

  it('renders cost using the trip currency exponent, not a hard-coded one', () => {
    renderList([place({ estimatedCost: 100_001 })])

    // 100_001 VND minor units is ₫100.001 — a two-decimal reading shows 1.000,01.
    expect(screen.getByText(/100\.001/)).toBeInTheDocument()
  })

  it('renders a missing cost as a dash rather than as zero', () => {
    renderList([place({ estimatedCost: null })])

    expect(screen.getByText(/—/)).toBeInTheDocument()
  })

  it('groups places by how far through the decision they are', () => {
    renderList([
      place({ id: 'a', name: 'Agreed', status: 'Confirmed', likedByMemberIds: [ME, OTHER] }),
      place({ id: 'b', name: 'Maybe', status: 'Shortlist', likedByMemberIds: [ME] }),
      place({ id: 'c', name: 'Raw', status: 'Idea' }),
    ])

    expect(within(screen.getByLabelText('Đã chốt')).getByText('Agreed')).toBeInTheDocument()
    expect(within(screen.getByLabelText('Đang cân nhắc')).getByText('Maybe')).toBeInTheDocument()
    expect(within(screen.getByLabelText('Ý tưởng')).getByText('Raw')).toBeInTheDocument()
  })

  it('hides groups that have nothing in them', () => {
    renderList([place({ status: 'Idea' })])

    expect(screen.queryByLabelText('Đã chốt')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Đã đi')).not.toBeInTheDocument()
  })

  it('shows the vote tally against the number of members', () => {
    renderList([place({ likedByMemberIds: [OTHER] })])

    expect(screen.getByText('1/2')).toBeInTheDocument()
  })

  it('marks my own vote as pressed and reports a toggle off', async () => {
    const handlers = renderList([place({ likedByMemberIds: [ME] })])

    const button = screen.getByRole('button', { name: /bỏ thích/i })
    expect(button).toHaveAttribute('aria-pressed', 'true')

    await userEvent.click(button)
    expect(handlers.onToggleLike).toHaveBeenCalledWith('p1', true)
  })

  it('reports a toggle on when I have not voted', async () => {
    const handlers = renderList([place({ likedByMemberIds: [OTHER] })])

    const button = screen.getByRole('button', { name: /^thích/i })
    expect(button).toHaveAttribute('aria-pressed', 'false')

    await userEvent.click(button)
    expect(handlers.onToggleLike).toHaveBeenCalledWith('p1', false)
  })

  it('names who liked a place so a stalled vote is explainable', () => {
    renderList([place({ likedByMemberIds: [OTHER] })])

    expect(screen.getByRole('button', { name: /^thích/i })).toHaveAttribute(
      'title',
      'Thích bởi: Linh',
    )
  })

  it('offers only the transitions the state machine allows from Shortlist', () => {
    renderList([place({ status: 'Shortlist', likedByMemberIds: [ME] })], { selectedPlaceId: 'p1' })

    expect(screen.getByRole('button', { name: 'Chốt' })).toBeInTheDocument()
    // Visiting is not reachable from Shortlist, nor before the trip starts.
    expect(screen.queryByRole('button', { name: 'Đã đi' })).not.toBeInTheDocument()
  })

  it('does not offer visited or skipped until the trip is under way', () => {
    renderList([place({ status: 'Confirmed', likedByMemberIds: [ME, OTHER] })], {
      selectedPlaceId: 'p1',
    })

    expect(screen.getByRole('button', { name: 'Bỏ chốt' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Đã đi' })).not.toBeInTheDocument()
  })

  it('offers visited and skipped once the trip is under way', async () => {
    const handlers = renderList([place({ status: 'Confirmed', likedByMemberIds: [ME, OTHER] })], {
      tripUnderway: true,
      selectedPlaceId: 'p1',
    })

    await userEvent.click(screen.getByRole('button', { name: 'Đã đi' }))
    expect(handlers.onChangeStatus).toHaveBeenCalledWith('p1', 'Visited')
  })

  it('offers the correction path between visited and skipped', () => {
    renderList([place({ status: 'Visited' })], { tripUnderway: true, selectedPlaceId: 'p1' })

    expect(screen.getByRole('button', { name: /sửa: bỏ qua/i })).toBeInTheDocument()
  })

  it('shows why a place was skipped', () => {
    renderList([place({ status: 'Skipped', skipReason: 'Trời mưa to' })], {
      selectedPlaceId: 'p1',
    })

    expect(screen.getByText(/trời mưa to/i)).toBeInTheDocument()
  })

  it('links out to Google Maps for photos and reviews', () => {
    renderList([place()], { selectedPlaceId: 'p1' })

    const link = screen.getByRole('link')
    expect(link).toHaveAttribute('href', expect.stringContaining('20.8333,104.6667'))
    expect(link).toHaveAttribute('rel', expect.stringContaining('noopener'))
  })

  it('disables the controls of the row currently mid-request', () => {
    renderList([place({ id: 'a', name: 'A' }), place({ id: 'b', name: 'B' })], {
      busyPlaceId: 'a',
    })

    expect(screen.getByRole('button', { name: /^thích A/i })).toBeDisabled()
    expect(screen.getByRole('button', { name: /^thích B/i })).toBeEnabled()
  })

  it('asks to delete the place whose button was pressed', async () => {
    const handlers = renderList([place({ id: 'a', name: 'A' }), place({ id: 'b', name: 'B' })], {
      selectedPlaceId: 'b',
    })

    await userEvent.click(screen.getByLabelText('Xoá B'))
    expect(handlers.onDelete).toHaveBeenCalledWith('b')
    expect(handlers.onDelete).toHaveBeenCalledTimes(1)
  })

  it('marks the open place and reports selection changes', async () => {
    const handlers = renderList([place({ id: 'a', name: 'A' }), place({ id: 'b', name: 'B' })], {
      selectedPlaceId: 'a',
    })

    expect(screen.getByTestId('place-a')).toHaveClass('is-open')
    expect(screen.getByTestId('place-b')).not.toHaveClass('is-open')

    await userEvent.click(screen.getByText('B'))
    expect(handlers.onSelect).toHaveBeenCalledWith('b')
  })
})

/*
 * A wishlist is mostly read. Showing every affordance on every card cost about
 * 250px each for three lines of content, so six places ran to ten screens.
 */
describe('PlaceList — a card opens', () => {
  it('shows only the name, the numbers and the vote when closed', () => {
    renderList([place({ description: 'Đi buổi sáng thì mát' })])

    expect(screen.getByText('Thác Dải Yếm')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /^thích/i })).toBeInTheDocument()
    expect(screen.queryByText('Đi buổi sáng thì mát')).not.toBeInTheDocument()
    expect(screen.queryByLabelText(/xoá/i)).not.toBeInTheDocument()
  })

  it('shows the detail and the actions when open', () => {
    renderList([place({ description: 'Đi buổi sáng thì mát' })], { selectedPlaceId: 'p1' })

    expect(screen.getByText('Đi buổi sáng thì mát')).toBeInTheDocument()
    expect(screen.getByLabelText(/xoá/i)).toBeInTheDocument()
  })

  it('says whether it is open, for anyone who cannot see the card', () => {
    renderList([place()], { selectedPlaceId: 'p1' })

    expect(screen.getByRole('button', { expanded: true })).toBeInTheDocument()
  })

  it('keeps the vote reachable without opening anything', async () => {
    // The one thing you do to a place without reading it.
    const handlers = renderList([place()])

    await userEvent.click(screen.getByRole('button', { name: /^thích/i }))

    expect(handlers.onToggleLike).toHaveBeenCalledWith('p1', false)
  })

  it('offers the map from inside the open card when the map is a separate pane', async () => {
    // Selecting used to swap the whole screen for the map, throwing away your
    // place in the list to show you a pin.
    const onShowOnMap = vi.fn()
    renderList([place()], { selectedPlaceId: 'p1', showsOnMap: true, onShowOnMap })

    await userEvent.click(screen.getByRole('button', { name: /bản đồ/i }))

    expect(onShowOnMap).toHaveBeenCalled()
  })

  it('does not offer the map when it is already beside the list', () => {
    renderList([place()], { selectedPlaceId: 'p1', showsOnMap: false })

    expect(screen.queryByRole('button', { name: /bản đồ/i })).not.toBeInTheDocument()
  })
})

describe('PlaceList vocabulary', () => {
  // The API's enum names are wire values, not words for a person. The map
  // legend already read "Tham quan" while the card beside it read "Sight".
  it('names the category in Vietnamese', () => {
    renderList([place({ category: 'Sight' })])

    const card = screen.getByTestId('place-p1')
    expect(card).toHaveTextContent('Tham quan')
    expect(card).not.toHaveTextContent('Sight')
  })

  it('names the time slots in Vietnamese', () => {
    renderList([place({ timeSlots: ['Morning', 'Evening'] })])

    const card = screen.getByTestId('place-p1')
    expect(card).toHaveTextContent('Sáng · Tối')
    expect(card).not.toHaveTextContent('Morning')
  })

  it.each([
    ['Food', 'Ăn uống'],
    ['Photo', 'Chụp ảnh'],
    ['Rest', 'Nghỉ ngơi'],
    ['Other', 'Khác'],
  ] as const)('translates %s to %s', (category, label) => {
    renderList([place({ category })])

    expect(screen.getByTestId('place-p1')).toHaveTextContent(label)
  })
})
