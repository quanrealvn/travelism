import { useState } from 'react'
import type { ActivityResponse, TripResponse } from '../api/api-types'
import { formatMoney } from '../api/money'
import { formatDateLabel } from '../itinerary/tripDates'
import { ActivityFeed } from './ActivityFeed'
import { Sheet } from './Sheet'
import { IconCheck, IconCopy } from './icons'

interface TripSheetProps {
  trip: TripResponse
  myMemberId: string
  syncStatus: 'live' | 'connecting' | 'offline'
  /**
   * The change log. It lives here rather than in a tab of its own: it is a
   * reference you consult when something surprised you, not a destination.
   */
  activity: ActivityResponse[]
  activityLoading: boolean
  onClose: () => void
}

const SYNC_TEXT = {
  live: 'Đang đồng bộ trực tiếp',
  connecting: 'Đang kết nối lại…',
  offline: 'Ngoại tuyến — thay đổi sẽ gửi khi có mạng',
} as const

/**
 * Everything about the trip that is read once and then remembered: the dates,
 * the budget, the invite code, who is coming.
 *
 * This used to live permanently in the header, where it cost a fifth of a phone
 * screen to display facts nobody rereads. Behind a tap it stays one gesture
 * away without taxing every other screen.
 */
export function TripSheet({
  trip,
  myMemberId,
  syncStatus,
  activity,
  activityLoading,
  onClose,
}: TripSheetProps) {
  const [copied, setCopied] = useState(false)

  async function copyInvite() {
    try {
      await navigator.clipboard.writeText(trip.inviteCode)
      setCopied(true)
      window.setTimeout(() => setCopied(false), 2000)
    } catch {
      // Clipboard access can be refused outright; the code is on screen and
      // selectable either way, so there is nothing to recover from.
    }
  }

  return (
    <Sheet title="Chuyến đi" onClose={onClose}>
      <dl className="trip-facts">
        <div className="fact">
          <dt className="fact-label">Điểm đến</dt>
          <dd className="fact-value">{trip.destination}</dd>
        </div>

        <div className="fact">
          <dt className="fact-label">Thời gian</dt>
          <dd className="fact-value">
            {formatDateLabel(trip.startDate)} – {formatDateLabel(trip.endDate)}
          </dd>
        </div>

        {trip.budgetAmount !== null && (
          <div className="fact">
            <dt className="fact-label">Ngân sách</dt>
            <dd className="fact-value">
              {formatMoney(trip.budgetAmount, trip.currency, trip.currencyExponent)}
            </dd>
          </div>
        )}

        <div className="fact">
          <dt className="fact-label">Múi giờ</dt>
          <dd className="fact-value">{trip.timeZoneId}</dd>
        </div>

        <div className="fact">
          <dt className="fact-label">Đồng bộ</dt>
          <dd className="fact-value">
            <span className={`sync sync-${syncStatus}`}>{SYNC_TEXT[syncStatus]}</span>
          </dd>
        </div>
      </dl>

      <h3 className="section-title">Mời bạn cùng đi</h3>
      <div className="invite-row">
        <code className="invite-code">{trip.inviteCode}</code>
        <button
          type="button"
          className="icon-button"
          onClick={copyInvite}
          aria-label={copied ? 'Đã chép mã mời' : 'Chép mã mời'}
        >
          {copied ? <IconCheck /> : <IconCopy />}
        </button>
      </div>

      <h3 className="section-title">Cùng đi ({trip.members.length})</h3>
      <ul className="member-chips">
        {trip.members.map((member) => (
          <li
            key={member.id}
            className={member.id === myMemberId ? 'member-chip is-me' : 'member-chip'}
          >
            <span className="avatar" aria-hidden="true">
              {[...member.displayName][0]?.toUpperCase() ?? '?'}
            </span>
            {member.displayName}
            {member.id === myMemberId && ' (bạn)'}
          </li>
        ))}
      </ul>

      <h3 className="section-title">Hoạt động gần đây</h3>
      <ActivityFeed entries={activity} members={trip.members} loading={activityLoading} />
    </Sheet>
  )
}
