import type { ActivityResponse, MemberResponse } from '../api/api-types'
import { Spinner } from './Spinner'

interface ActivityFeedProps {
  entries: ActivityResponse[]
  members: MemberResponse[]
  loading: boolean
}

/**
 * The trip's audit trail. Append-only on the server, so this is a plain list —
 * there is nothing here to edit.
 */
export function ActivityFeed({ entries, members, loading }: ActivityFeedProps) {
  const nameOf = (memberId: string) =>
    members.find((member) => member.id === memberId)?.displayName ?? 'Ai đó'

  if (loading) {
    return (
      <p className="search-hint inline-busy" role="status">
        <Spinner />
        Đang tải…
      </p>
    )
  }

  if (entries.length === 0) {
    return <p className="empty-state small">Chưa có hoạt động nào.</p>
  }

  return (
    <ul className="activity-feed" aria-label="Hoạt động">
      {entries.map((entry) => (
        <li key={entry.id}>
          <span className="activity-who">{nameOf(entry.memberId)}</span>
          <span className="activity-what">{entry.summaryText}</span>
          <time className="activity-when" dateTime={entry.at}>
            {formatWhen(entry.at)}
          </time>
        </li>
      ))}
    </ul>
  )
}

/**
 * Relative time, which is what matters for "did this just change?".
 * The instant itself is UTC from the server; only the phrasing is local.
 */
function formatWhen(at: string): string {
  const minutes = Math.round((Date.now() - new Date(at).getTime()) / 60_000)

  if (minutes < 1) return 'vừa xong'
  if (minutes < 60) return `${minutes} phút trước`

  const hours = Math.round(minutes / 60)
  if (hours < 24) return `${hours} giờ trước`

  const days = Math.round(hours / 24)
  return `${days} ngày trước`
}
