import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { ApiError } from '../api/client'
import { api } from '../api/client'
import type { TripSessionResponse } from '../api/api-types'

interface StartScreenProps {
  onReady: (session: TripSessionResponse) => void
}

/** Create a trip or join one with an invite code. Both issue the session cookie. */
export function StartScreen({ onReady }: StartScreenProps) {
  const [mode, setMode] = useState<'create' | 'join'>('create')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const [createForm, setCreateForm] = useState({
    name: 'Mộc Châu weekend',
    destination: 'Mộc Châu, Việt Nam',
    startDate: '',
    endDate: '',
    ownerDisplayName: '',
    accessCode: '',
  })

  const [joinForm, setJoinForm] = useState({ inviteCode: '', displayName: '' })

  /*
   * Whether this deployment lets just anyone start a trip.
   *
   * Asked rather than assumed: showing an access-code field on an open instance
   * invents a barrier that is not there, and hiding it on a closed one leaves
   * the form permanently refused with no way to satisfy it. Joining is never
   * gated — an invite link is the whole point.
   */
  const [needsCode, setNeedsCode] = useState(false)

  useEffect(() => {
    let cancelled = false
    void api
      .config()
      .then((config) => {
        if (!cancelled) {
          setNeedsCode(config.requiresAccessCode)
        }
      })
      // Open is the safe assumption: if it is wrong the server refuses, and
      // that refusal says exactly what is missing.
      .catch(() => undefined)

    return () => {
      cancelled = true
    }
  }, [])

  async function run(action: () => Promise<TripSessionResponse>) {
    setBusy(true)
    setError(null)
    try {
      onReady(await action())
    } catch (caught) {
      setError(
        caught instanceof ApiError
          ? describe(caught)
          : 'Không kết nối được máy chủ. Thử lại nhé.',
      )
    } finally {
      setBusy(false)
    }
  }

  function handleCreate(event: FormEvent) {
    event.preventDefault()
    void run(() => api.createTrip(createForm))
  }

  function handleJoin(event: FormEvent) {
    event.preventDefault()
    void run(() => api.joinTrip(joinForm))
  }

  return (
    <div className="start-screen">
      <h1>Travelism</h1>
      <p className="tagline">Lên kế hoạch chuyến đi cùng nhau.</p>

      <div className="tabs" role="tablist">
        <button
          type="button"
          role="tab"
          aria-selected={mode === 'create'}
          onClick={() => setMode('create')}
        >
          Tạo chuyến đi
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={mode === 'join'}
          onClick={() => setMode('join')}
        >
          Tham gia
        </button>
      </div>

      {mode === 'create' ? (
        <form onSubmit={handleCreate} aria-label="Tạo chuyến đi">
          <label>
            Tên chuyến đi
            <input
              value={createForm.name}
              onChange={(e) => setCreateForm({ ...createForm, name: e.target.value })}
              required
              maxLength={80}
            />
          </label>
          <label>
            Điểm đến
            <input
              value={createForm.destination}
              onChange={(e) => setCreateForm({ ...createForm, destination: e.target.value })}
              required
            />
          </label>
          <div className="row">
            <label>
              Ngày bắt đầu
              <input
                type="date"
                value={createForm.startDate}
                onChange={(e) => setCreateForm({ ...createForm, startDate: e.target.value })}
                required
              />
            </label>
            <label>
              Ngày kết thúc
              <input
                type="date"
                value={createForm.endDate}
                onChange={(e) => setCreateForm({ ...createForm, endDate: e.target.value })}
                required
              />
            </label>
          </div>
          <label>
            Tên của bạn
            <input
              value={createForm.ownerDisplayName}
              onChange={(e) =>
                setCreateForm({ ...createForm, ownerDisplayName: e.target.value })
              }
              required
              maxLength={40}
            />
          </label>
          {needsCode && (
            <label>
              Mã truy cập
              <input
                value={createForm.accessCode}
                onChange={(e) =>
                  setCreateForm({ ...createForm, accessCode: e.target.value })
                }
                required
                autoComplete="off"
              />
              <span className="field-hint">
                Chỉ cần khi tạo chuyến đi mới. Tham gia bằng mã mời thì không cần.
              </span>
            </label>
          )}
          <button type="submit" disabled={busy}>
            {busy ? 'Đang tạo…' : 'Tạo chuyến đi'}
          </button>
        </form>
      ) : (
        <form onSubmit={handleJoin} aria-label="Tham gia chuyến đi">
          <label>
            Mã mời
            <input
              value={joinForm.inviteCode}
              onChange={(e) => setJoinForm({ ...joinForm, inviteCode: e.target.value })}
              required
              maxLength={8}
              autoCapitalize="characters"
            />
          </label>
          <label>
            Tên của bạn
            <input
              value={joinForm.displayName}
              onChange={(e) => setJoinForm({ ...joinForm, displayName: e.target.value })}
              required
              maxLength={40}
            />
          </label>
          <button type="submit" disabled={busy}>
            {busy ? 'Đang tham gia…' : 'Tham gia'}
          </button>
        </form>
      )}

      {error && (
        <p className="form-error" role="alert">
          {error}
        </p>
      )}
    </div>
  )
}

/** Turns the server's stable error code into copy a traveller can act on. */
function describe(error: ApiError): string {
  switch (error.code) {
    case 'NOT_FOUND':
      return 'Mã mời không đúng. Kiểm tra lại giúp mình.'
    case 'NAME_TAKEN':
      return 'Tên này đã có người dùng trong chuyến đi. Chọn tên khác nhé.'
    case 'TRIP_FULL':
      return 'Chuyến đi đã đủ 10 thành viên.'
    case 'RATE_LIMITED':
      return 'Bạn thử quá nhiều lần. Đợi một phút rồi thử lại.'
    case 'INVALID_ACCESS_CODE':
      return 'Mã truy cập không đúng. Hỏi người đã mời bạn nhé.'
    case 'VALIDATION_FAILED':
      return Object.values(error.fieldErrors())[0] ?? 'Thông tin chưa hợp lệ.'
    default:
      return error.text
  }
}
