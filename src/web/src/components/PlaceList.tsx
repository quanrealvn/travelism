import type {
  MemberResponse,
  PlaceReferenceRequest,
  PlaceResponse,
  PlaceStatus,
} from '../api/api-types'
import { formatDuration, formatMoney } from '../api/money'
import { placeCategoryLabel, timeSlotsLabel } from '../api/labels'
import { PlaceDetail } from './PlaceDetail'
import { IconClose, IconExternal } from './icons'
import { Spinner } from './Spinner'

interface PlaceListProps {
  places: PlaceResponse[]
  members: MemberResponse[]
  myMemberId: string
  currency: string
  currencyExponent: number
  selectedPlaceId: string | null
  deletingPlaceId: string | null
  busyPlaceId: string | null
  tripUnderway: boolean
  onSelect: (placeId: string) => void
  onDelete: (placeId: string) => void
  onToggleLike: (placeId: string, liked: boolean) => void
  onChangeStatus: (placeId: string, status: PlaceStatus) => void
  onSaveDetail: (
    placeId: string,
    description: string | null,
    references: PlaceReferenceRequest[],
  ) => void
}

/**
 * The wishlist reads as the decision it represents: what nobody has backed yet,
 * what someone wants, and what the group has actually agreed on.
 */
const GROUPS: { status: PlaceStatus; title: string; hint: string }[] = [
  { status: 'Confirmed', title: 'Đã chốt', hint: 'Cả nhóm đều thích' },
  { status: 'Shortlist', title: 'Đang cân nhắc', hint: 'Có người thích' },
  { status: 'Idea', title: 'Ý tưởng', hint: 'Chưa ai thích' },
  { status: 'Visited', title: 'Đã đi', hint: '' },
  { status: 'Skipped', title: 'Đã bỏ qua', hint: '' },
]

export function PlaceList(props: PlaceListProps) {
  const { places, members } = props

  if (places.length === 0) {
    return (
      <p className="empty-state">
        Chưa có địa điểm nào. Thêm địa điểm đầu tiên để bắt đầu lên kế hoạch.
      </p>
    )
  }

  return (
    <div className="wishlist">
      {GROUPS.map((group) => {
        const inGroup = places.filter((place) => place.status === group.status)
        if (inGroup.length === 0) {
          return null
        }

        return (
          <section key={group.status} className="wishlist-group">
            <h3 className={`group-title group-${group.status.toLowerCase()}`}>
              {group.title} <span className="group-count">{inGroup.length}</span>
              {group.hint && <span className="group-hint">{group.hint}</span>}
            </h3>

            <ul className="place-list" aria-label={group.title}>
              {inGroup.map((place) => (
                <PlaceRow
                  key={place.id}
                  place={place}
                  memberCount={members.length}
                  {...props}
                />
              ))}
            </ul>
          </section>
        )
      })}
    </div>
  )
}

function PlaceRow({
  place,
  memberCount,
  members,
  myMemberId,
  currency,
  currencyExponent,
  selectedPlaceId,
  deletingPlaceId,
  busyPlaceId,
  tripUnderway,
  onSelect,
  onDelete,
  onToggleLike,
  onChangeStatus,
  onSaveDetail,
}: PlaceListProps & { place: PlaceResponse; memberCount: number }) {
  const likedByMe = place.likedByMemberIds.includes(myMemberId)
  const likeCount = place.likedByMemberIds.length
  const busy = busyPlaceId === place.id
  const statusActions = availableStatusActions(place, tripUnderway)

  const likedNames = place.likedByMemberIds
    .map((id) => members.find((member) => member.id === id)?.displayName)
    .filter((name): name is string => Boolean(name))

  return (
    <li
      className={place.id === selectedPlaceId ? 'place has-detail selected' : 'place has-detail'}
      data-testid={`place-${place.id}`}
    >
      <button
        type="button"
        className={likedByMe ? 'place-like liked' : 'place-like'}
        onClick={() => onToggleLike(place.id, likedByMe)}
        disabled={busy}
        aria-pressed={likedByMe}
        aria-label={likedByMe ? `Bỏ thích ${place.name}` : `Thích ${place.name}`}
        title={likedNames.length > 0 ? `Thích bởi: ${likedNames.join(', ')}` : 'Chưa ai thích'}
      >
        {likedByMe ? '♥' : '♡'}
        <span className="place-like-count">
          {likeCount}/{memberCount}
        </span>
      </button>

      <button type="button" className="place-main" onClick={() => onSelect(place.id)}>
        <span className="place-name">{place.name}</span>
        <span className="place-meta">
          {placeCategoryLabel(place.category)} · {formatDuration(place.estimatedDurationMinutes)} ·{' '}
          {formatMoney(place.estimatedCost, currency, currencyExponent)}
        </span>
        <span className="place-slots">{timeSlotsLabel(place.timeSlots)}</span>
        {place.openHoursText && <span className="place-hours">{place.openHoursText}</span>}
        {place.skipReason && <span className="place-skip-reason">Lý do: {place.skipReason}</span>}
      </button>

      {/*
        Open-elsewhere and delete live at the card's top-right, not in the
        action row: most places offer no status action at all, and a row
        containing nothing but two right-aligned icons reads as a layout bug.
      */}
      <div className="place-tools">
        <a
          className="place-external"
          href={`https://www.google.com/maps/search/?api=1&query=${place.lat},${place.lng}`}
          target="_blank"
          rel="noreferrer noopener"
          aria-label={`Mở ${place.name} trong Google Maps`}
          title="Mở trong Google Maps để xem ảnh và đánh giá"
        >
          <IconExternal />
        </a>

        <button
          type="button"
          className="place-delete"
          onClick={() => onDelete(place.id)}
          disabled={deletingPlaceId === place.id}
          aria-label={`Xoá ${place.name}`}
        >
          {deletingPlaceId === place.id ? <Spinner /> : <IconClose />}
        </button>
      </div>

      {/*
        Detail before actions. With the order reversed the row of buttons sat
        between the place's own meta line and its description, splitting one
        piece of content in half with controls.
      */}
      <PlaceDetail
        place={place}
        saving={busy}
        onSave={(description, references) => onSaveDetail(place.id, description, references)}
      />

      {statusActions.length > 0 && (
        <div className="place-actions">
          {statusActions.map((action) => (
            <button
              key={action.status}
              type="button"
              className="place-status-action"
              onClick={() => onChangeStatus(place.id, action.status)}
              disabled={busy}
              title={action.title}
            >
              {action.label}
            </button>
          ))}
        </div>
      )}
    </li>
  )
}

interface StatusAction {
  status: PlaceStatus
  label: string
  title: string
}

/**
 * Only the transitions spec §4 permits from the current status are offered.
 * mirror of server rule — the server re-checks every edge and answers 409
 * INVALID_STATUS_TRANSITION; hiding the impossible ones just avoids offering a
 * button that could only fail.
 *
 * Returns the list rather than rendering it, so the card can leave the whole
 * action row out when there is nothing to put in it.
 */
function availableStatusActions(place: PlaceResponse, tripUnderway: boolean): StatusAction[] {
  const actions: StatusAction[] = []

  if (place.status === 'Shortlist') {
    actions.push({
      status: 'Confirmed',
      label: 'Chốt',
      title: 'Chốt luôn, không cần đợi mọi người thích',
    })
  }

  if (place.status === 'Confirmed') {
    actions.push({ status: 'Shortlist', label: 'Bỏ chốt', title: 'Đưa lại vào danh sách cân nhắc' })

    if (tripUnderway) {
      actions.push({ status: 'Visited', label: 'Đã đi', title: 'Đánh dấu đã đến nơi này' })
      actions.push({ status: 'Skipped', label: 'Bỏ qua', title: 'Đánh dấu đã bỏ qua' })
    }
  }

  if (place.status === 'Visited') {
    actions.push({ status: 'Skipped', label: 'Sửa: bỏ qua', title: 'Sửa lại thành đã bỏ qua' })
  }

  if (place.status === 'Skipped') {
    actions.push({ status: 'Visited', label: 'Sửa: đã đi', title: 'Sửa lại thành đã đi' })
  }

  return actions
}
