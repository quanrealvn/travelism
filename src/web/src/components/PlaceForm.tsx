import { useEffect, useState } from 'react'
import type { CSSProperties, FormEvent } from 'react'
import { ALL_CATEGORIES, ALL_TIME_SLOTS } from '../api/api-types'
import { placeCategoryLabel, timeSlotLabel } from '../api/labels'
import { categoryStyle } from '../map/placeMarkers'
import { ButtonBusy } from './Spinner'
import type {
  CreatePlaceRequest,
  GeocodeResultResponse,
  PlaceCategory,
  TimeSlot,
} from '../api/api-types'
import { parseMoney } from '../api/money'
import { hoursToMinutes } from '../places/duration'
import { PlaceSearch } from './PlaceSearch'

/*
 * A glyph and a colour per part of the day. Emoji rather than drawn icons here
 * because these are the one set in the app where the meaning *is* the picture —
 * a sun at noon and a moon at night need no legend in any language.
 */
const TIME_SLOT_GLYPH: Record<TimeSlot, string> = {
  Morning: '🌅',
  Noon: '☀️',
  Afternoon: '🌤️',
  Evening: '🌙',
}

const TIME_SLOT_COLOUR: Record<TimeSlot, string> = {
  Morning: '#c2410c',
  Noon: '#a16207',
  Afternoon: '#0369a1',
  Evening: '#4338ca',
}

interface PlaceFormProps {
  tripId: string
  currencyExponent: number
  pending: boolean
  fieldErrors: Record<string, string>
  submitError: string | null
  onSubmit: (body: CreatePlaceRequest) => void
  /** A location picked by clicking the map; overrides whatever is in the form. */
  mapPick?: { lat: number; lng: number } | null
  /** Reports the current location so the map can show a pin for it. */
  onLocationChange?: (location: { lat: number; lng: number } | null) => void
}

const EMPTY = {
  name: '',
  lat: '',
  lng: '',
  category: 'Sight' as PlaceCategory,
  timeSlots: ['Morning'] as TimeSlot[],
  durationHours: '1,5',
  estimatedCost: '',
  openHoursText: '',
}

export function PlaceForm({
  tripId,
  currencyExponent,
  pending,
  fieldErrors,
  submitError,
  onSubmit,
  mapPick,
  onLocationChange,
}: PlaceFormProps) {
  const [form, setForm] = useState(EMPTY)
  const [localError, setLocalError] = useState<string | null>(null)
  const [pickedAddress, setPickedAddress] = useState<string | null>(null)
  const [manualCoords, setManualCoords] = useState(false)

  // A click on the map wins over whatever coordinates the form held: the user
  // just pointed at the spot they mean.
  useEffect(() => {
    if (!mapPick) {
      return
    }

    setForm((current) => ({
      ...current,
      lat: String(mapPick.lat),
      lng: String(mapPick.lng),
    }))
    setPickedAddress(null)
    setLocalError(null)
  }, [mapPick])

  function applySearchResult(result: GeocodeResultResponse) {
    setForm((current) => ({
      ...current,
      // The typed name is only replaced when the field is still untouched, so
      // picking a location never overwrites a name the user chose themselves.
      // A pasted link may carry no name at all, which must not blank the field.
      name: current.name.trim() === '' && result.name !== '' ? result.name : current.name,
      lat: String(result.lat),
      lng: String(result.lng),
    }))
    setPickedAddress(result.displayName)
    setLocalError(null)
    onLocationChange?.({ lat: result.lat, lng: result.lng })
  }

  function toggleSlot(slot: TimeSlot) {
    setForm((current) => ({
      ...current,
      timeSlots: current.timeSlots.includes(slot)
        ? current.timeSlots.filter((s) => s !== slot)
        : [...current.timeSlots, slot],
    }))
  }

  function clearLocation() {
    setForm((current) => ({ ...current, lat: '', lng: '' }))
    setPickedAddress(null)
    onLocationChange?.(null)
  }

  function setManualCoordinate(axis: 'lat' | 'lng', value: string) {
    const next = { ...form, [axis]: value }
    setForm(next)

    const lat = Number(next.lat)
    const lng = Number(next.lng)
    const usable =
      next.lat.trim() !== '' && next.lng.trim() !== '' && Number.isFinite(lat) && Number.isFinite(lng)

    // Half-typed coordinates ("20.") are not a location yet, so the map pin
    // only moves once both axes parse.
    onLocationChange?.(usable ? { lat, lng } : null)
  }

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setLocalError(null)

    const lat = Number(form.lat)
    const lng = Number(form.lng)
    const cost = parseMoney(form.estimatedCost, currencyExponent)

    if (form.lat.trim() === '' || form.lng.trim() === '') {
      setLocalError('Chọn một địa điểm từ kết quả tìm kiếm, hoặc nhập toạ độ thủ công.')
      return
    }

    // mirror of server rule: at least one time slot (spec §3). Mirrored only so
    // the user is not made to round-trip for something obvious; the server
    // still rejects it independently.
    if (form.timeSlots.length === 0) {
      setLocalError('Chọn ít nhất một buổi trong ngày.')
      return
    }

    if (!Number.isFinite(lat) || !Number.isFinite(lng)) {
      setLocalError('Toạ độ phải là số.')
      return
    }

    if (Number.isNaN(cost)) {
      setLocalError('Chi phí ước tính không hợp lệ.')
      return
    }

    const durationMinutes = hoursToMinutes(form.durationHours)
    if (Number.isNaN(durationMinutes)) {
      setLocalError('Thời lượng phải là số giờ lớn hơn 0. Ví dụ: 1,5')
      return
    }

    onSubmit({
      name: form.name,
      lat,
      lng,
      category: form.category,
      timeSlots: form.timeSlots,
      estimatedDurationMinutes: durationMinutes,
      estimatedCost: cost,
      openHoursText: form.openHoursText.trim() === '' ? null : form.openHoursText,
    })

    // Deliberately not cleared here. The submit handler cannot know whether the
    // server accepted it, and clearing on the way out threw away everything the
    // user typed the moment anything was rejected — including the searched
    // location, so they had to find the place again to fix a duration. On
    // success the sheet closes and unmounts this form, which clears it anyway.
  }

  const hasCoordinates = form.lat.trim() !== '' && form.lng.trim() !== ''

  return (
    // No heading of its own: the sheet this opens in is already titled "Thêm
    // địa điểm", and printing it twice in a row reads as a rendering fault.
    // The aria-label keeps the form named for anyone not seeing that title.
    <form className="place-form" onSubmit={handleSubmit} aria-label="Thêm địa điểm">
      <PlaceSearch tripId={tripId} onPick={applySearchResult} />

      {hasCoordinates ? (
        <p className="picked-location" data-testid="picked-location">
          📍 {pickedAddress ?? `${form.lat}, ${form.lng}`}
          <button type="button" className="link-button" onClick={clearLocation}>
            đổi
          </button>
        </p>
      ) : (
        <p className="search-hint">Không tìm thấy? Bấm thẳng lên bản đồ để chọn vị trí.</p>
      )}

      <button
        type="button"
        className="link-button"
        onClick={() => setManualCoords((current) => !current)}
      >
        {manualCoords ? 'Ẩn nhập toạ độ thủ công' : 'Nhập toạ độ thủ công'}
      </button>

      {manualCoords && (
        <div className="row">
          <label>
            Vĩ độ (lat)
            <input
              value={form.lat}
              onChange={(e) => setManualCoordinate('lat', e.target.value)}
              inputMode="decimal"
              placeholder="20.8386"
            />
          </label>
          <label>
            Kinh độ (lng)
            <input
              value={form.lng}
              onChange={(e) => setManualCoordinate('lng', e.target.value)}
              inputMode="decimal"
              placeholder="104.6383"
            />
          </label>
        </div>
      )}
      {fieldErrors.lat && <p className="field-error">{fieldErrors.lat}</p>}
      {fieldErrors.lng && <p className="field-error">{fieldErrors.lng}</p>}

      <label>
        Tên
        <input
          value={form.name}
          onChange={(e) => setForm({ ...form, name: e.target.value })}
          required
          maxLength={120}
          placeholder="Tên hiển thị trong wishlist"
        />
      </label>
      {fieldErrors.name && <p className="field-error">{fieldErrors.name}</p>}

      {/*
        Tiles rather than a <select>. A native dropdown cannot carry the
        category's own icon or colour, so the one place you choose what a place
        *is* was the one place the app's category language disappeared — and on
        a phone it opened a full-screen wheel to pick one of five things.
      */}
      <fieldset className="picker">
        <legend>Loại</legend>
        <div className="picker-grid">
          {ALL_CATEGORIES.map((category) => {
            const style = categoryStyle(category)
            const selected = form.category === category
            return (
              <label
                key={category}
                className={selected ? 'picker-option is-selected' : 'picker-option'}
                style={{ '--option-colour': style.color } as CSSProperties}
              >
                <input
                  type="radio"
                  name="category"
                  value={category}
                  checked={selected}
                  onChange={() => setForm({ ...form, category })}
                />
                <span className="picker-icon" aria-hidden="true">
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.75}>
                    <path d={style.path} strokeLinecap="round" strokeLinejoin="round" />
                  </svg>
                </span>
                {placeCategoryLabel(category)}
              </label>
            )
          })}
        </div>
      </fieldset>

      <fieldset className="picker">
        <legend>Buổi phù hợp</legend>
        <div className="picker-grid">
          {ALL_TIME_SLOTS.map((slot) => {
            const selected = form.timeSlots.includes(slot)
            return (
              <label
                key={slot}
                className={selected ? 'picker-option is-selected' : 'picker-option'}
                style={{ '--option-colour': TIME_SLOT_COLOUR[slot] } as CSSProperties}
              >
                <input type="checkbox" checked={selected} onChange={() => toggleSlot(slot)} />
                <span className="picker-icon" aria-hidden="true">
                  {TIME_SLOT_GLYPH[slot]}
                </span>
                {timeSlotLabel(slot)}
              </label>
            )
          })}
        </div>
      </fieldset>
      {fieldErrors.timeSlots && <p className="field-error">{fieldErrors.timeSlots}</p>}

      <div className="row">
        <label>
          Thời lượng (giờ)
          {/*
            Hours, because that is the unit people plan in — "2 tiếng ở thác",
            never "120 phút". The API stores minutes, so the conversion happens
            at this edge; decimals are accepted so half an hour needs no second
            field.
          */}
          <input
            value={form.durationHours}
            onChange={(e) => setForm({ ...form, durationHours: e.target.value })}
            inputMode="decimal"
            placeholder="1,5"
            required
          />
          <span className="field-hint">1,5 = 1 giờ 30 phút</span>
        </label>
        <label>
          Chi phí ước tính
          <input
            value={form.estimatedCost}
            onChange={(e) => setForm({ ...form, estimatedCost: e.target.value })}
            inputMode="numeric"
            placeholder="50000"
          />
        </label>
      </div>
      {fieldErrors.estimatedDurationMinutes && (
        <p className="field-error">{fieldErrors.estimatedDurationMinutes}</p>
      )}

      <label>
        {/*
          Was "Giờ mở cửa". The field is free text either way, and what people
          actually wrote in it was when *they* meant to turn up, not when the
          place unlocks its doors.
        */}
        Giờ có mặt dự kiến
        <input
          value={form.openHoursText}
          onChange={(e) => setForm({ ...form, openHoursText: e.target.value })}
          maxLength={200}
          placeholder="Khoảng 8h sáng"
        />
      </label>

      {(localError ?? submitError) && (
        <p className="form-error" role="alert">
          {localError ?? submitError}
        </p>
      )}

      {/* Was a bare submit sitting at its natural width against the left edge
          of a full-width sheet. It is the one action on this form, so it gets
          the primary treatment and the whole width. */}
      <button type="submit" className="button-primary form-submit" disabled={pending}>
        {pending ? <ButtonBusy>Đang lưu…</ButtonBusy> : 'Thêm vào wishlist'}
      </button>
    </form>
  )
}
