import type { CSSProperties } from 'react'
import type {
  MemberResponse,
  PlaceReferenceRequest,
  PlaceResponse,
  PlaceStatus,
} from '../api/api-types'
import { formatDuration, formatMoney } from '../api/money'
import { timeSlotsLabel } from '../api/labels'
import { categoryStyle } from '../map/placeMarkers'
import { PlaceDetail } from './PlaceDetail'
import {
  IconCheckCircle,
  IconClock,
  IconClose,
  IconExternal,
  IconFlag,
  IconPin,
  IconSkip,
  IconSparkle,
} from './icons'
import { Spinner } from './Spinner'

interface PlaceListProps {
  places: PlaceResponse[]
  members: MemberResponse[]
  myMemberId: string
  currency: string
  currencyExponent: number
  selectedPlaceId: string | null
  /**
   * True when picking a place will swap the list out for the map. The card then
   * says so, because a tap that replaces the whole screen should not be a
   * surprise.
   */
  showsOnMap: boolean
  /** Switches the wishlist to the map pane. Only meaningful when showsOnMap. */
  onShowOnMap: () => void
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
const GROUPS: {
  status: PlaceStatus
  title: string
  hint: string
  Icon: (props: { className?: string }) => JSX.Element
}[] = [
  { status: 'Confirmed', title: 'Đã chốt', hint: 'Cả nhóm đều thích', Icon: IconCheckCircle },
  { status: 'Shortlist', title: 'Đang cân nhắc', hint: 'Có người thích', Icon: IconClock },
  { status: 'Idea', title: 'Ý tưởng', hint: 'Chưa ai thích', Icon: IconSparkle },
  { status: 'Visited', title: 'Đã đi', hint: '', Icon: IconFlag },
  { status: 'Skipped', title: 'Đã bỏ qua', hint: '', Icon: IconSkip },
]

export function PlaceList(props: PlaceListProps) {
  const { places, members } = props

  if (places.length === 0) {
    return (
      <div className="empty-state">
        <p>Chưa có địa điểm nào.</p>
        {/*
          Naming the three ways in. Pasting a Google Maps link is the app's
          actual answer to "OpenStreetMap doesn't have it", and it was only
          discoverable by opening the form and reading a hint inside it.
        */}
        <p className="empty-state-hint">
          Tìm theo tên, dán link Google Maps, hoặc bấm thẳng lên bản đồ.
        </p>
      </div>
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
            {/*
              A pill rather than a heading with a bar: icon, name and count as
              one object, the way a dashboard states a status. The icon also
              means the group is not distinguished by colour alone.
            */}
            <h3 className={`group-title group-${group.status.toLowerCase()}`}>
              <span className="group-pill">
                <group.Icon />
                {group.title}
                <span className="group-count">{inGroup.length}</span>
              </span>
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
  showsOnMap,
  onShowOnMap,
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

  // "đợi Linh" says what has to happen next; "1/2" makes you work it out.
  const waitingOn = members
    .filter((member) => !place.likedByMemberIds.includes(member.id))
    .map((member) => member.displayName)

  const style = categoryStyle(place.category)

  const open = place.id === selectedPlaceId

  /*
   * Collapsed to a single row until it is the one being looked at.
   *
   * Every card used to show every affordance at once — description, links, an
   * edit button, a vote, status actions, an external link and a delete — which
   * cost about 250px each for three lines of content, so six places ran to ten
   * screens of scrolling. A wishlist is mostly read, and the thing you read is
   * the name, the category and what it costs.
   */
  return (
    <li
      className={open ? 'place is-open' : 'place'}
      data-testid={`place-${place.id}`}
    >
      <button
        type="button"
        className="place-head"
        onClick={() => onSelect(place.id)}
        aria-expanded={open}
      >
        {/*
          The card's left rail is the category, in the category's own colour
          and icon. It used to be the like button, filled rose — which is the
          colour this app uses for "food", so a waterfall and a tea hill both
          carried a food-coloured tile. The rail is the scannable index down a
          long list, and it was spending the category palette on something else.
        */}
        <span className="place-category" style={{ '--tile-colour': style.color } as CSSProperties}>
          <svg
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth={1.75}
            strokeLinecap="round"
            strokeLinejoin="round"
            aria-hidden="true"
            focusable="false"
          >
            <path d={style.path} />
          </svg>
          <span className="visually-hidden">{style.label}</span>
        </span>

        <span className="place-head-text">
          <span className="place-name">{place.name}</span>
          {/* One line. The category is the tile beside it, so repeating it
              here was what pushed the price onto a row of its own. */}
          <span className="place-meta">
            {formatDuration(place.estimatedDurationMinutes)} ·{' '}
            {formatMoney(place.estimatedCost, currency, currencyExponent)} ·{' '}
            {timeSlotsLabel(place.timeSlots)}
          </span>
        </span>
      </button>

      {/*
        Agreeing is the mechanism this whole app runs on, so the vote stays on
        the collapsed row — it is the one thing you do without opening a card.
      */}
      <button
        type="button"
        className={likedByMe ? 'place-like liked' : 'place-like'}
        onClick={() => onToggleLike(place.id, likedByMe)}
        disabled={busy}
        aria-pressed={likedByMe}
        aria-label={likedByMe ? `Bỏ thích ${place.name}` : `Thích ${place.name}`}
        title={likedNames.length > 0 ? `Thích bởi: ${likedNames.join(', ')}` : 'Chưa ai thích'}
      >
        <span aria-hidden="true">{likedByMe ? '♥' : '♡'}</span>
        {likeCount}/{memberCount}
      </button>

      {open && (
        <div className="place-body">
          {place.openHoursText && <p className="place-hours">{place.openHoursText}</p>}
          {place.skipReason && <p className="place-skip-reason">Lý do: {place.skipReason}</p>}
          {waitingOn.length > 0 && likeCount > 0 && (
            <p className="place-waiting">Đang đợi {waitingOn.join(', ')}</p>
          )}

          <PlaceDetail
            place={place}
            saving={busy}
            onSave={(description, references) => onSaveDetail(place.id, description, references)}
          />

          <div className="place-actions">
            {statusActions.map((action) => (
              <button
                key={action.status}
                type="button"
                className={`place-status-action tone-${action.tone}`}
                onClick={() => onChangeStatus(place.id, action.status)}
                disabled={busy}
                title={action.title}
              >
                <action.Icon />
                {action.label}
              </button>
            ))}

            {showsOnMap && (
              <button type="button" className="place-status-action tone-quiet" onClick={onShowOnMap}>
                <IconPin />
                Bản đồ
              </button>
            )}

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

            {/*
              Destructive, and it used to sit at identical weight 8px from a
              benign action on every one of forty cards. Behind one tap now,
              and rose when approached — which is what this palette reserves
              for danger.
            */}
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
        </div>
      )}
    </li>
  )
}

interface StatusAction {
  status: PlaceStatus
  label: string
  title: string
  Icon: (props: { className?: string }) => JSX.Element
  /** "go" moves the place forward; "quiet" walks it back or aside. */
  tone: 'go' | 'quiet'
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
      // Not "without waiting", which framed the ordinary way of deciding as a
      // shortcut past the rules. Past three people, unanimity is the exception.
      title: 'Chốt cho cả nhóm',
      Icon: IconCheckCircle,
      tone: 'go',
    })
  }

  if (place.status === 'Confirmed') {
    actions.push({
      status: 'Shortlist',
      label: 'Bỏ chốt',
      title: 'Đưa lại vào danh sách cân nhắc',
      Icon: IconClock,
      tone: 'quiet',
    })

    if (tripUnderway) {
      actions.push({
        status: 'Visited',
        label: 'Đã đi',
        title: 'Đánh dấu đã đến nơi này',
        Icon: IconFlag,
        tone: 'go',
      })
      actions.push({
        status: 'Skipped',
        label: 'Bỏ qua',
        title: 'Đánh dấu đã bỏ qua',
        Icon: IconSkip,
        tone: 'quiet',
      })
    }
  }

  if (place.status === 'Visited') {
    actions.push({
      status: 'Skipped',
      label: 'Sửa: bỏ qua',
      title: 'Sửa lại thành đã bỏ qua',
      Icon: IconSkip,
      tone: 'quiet',
    })
  }

  if (place.status === 'Skipped') {
    actions.push({
      status: 'Visited',
      label: 'Sửa: đã đi',
      title: 'Sửa lại thành đã đi',
      Icon: IconFlag,
      tone: 'quiet',
    })
  }

  return actions
}
