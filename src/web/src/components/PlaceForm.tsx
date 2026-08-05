import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { ALL_CATEGORIES, ALL_TIME_SLOTS } from '../api/api-types'
import { placeCategoryLabel, timeSlotLabel } from '../api/labels'
import { ButtonBusy } from './Spinner'
import type {
  CreatePlaceRequest,
  GeocodeResultResponse,
  PlaceCategory,
  TimeSlot,
} from '../api/api-types'
import { parseMoney } from '../api/money'
import { PlaceSearch } from './PlaceSearch'

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
  estimatedDurationMinutes: '90',
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

    onSubmit({
      name: form.name,
      lat,
      lng,
      category: form.category,
      timeSlots: form.timeSlots,
      estimatedDurationMinutes: Number(form.estimatedDurationMinutes),
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

      <label>
        Loại
        <select
          value={form.category}
          onChange={(e) => setForm({ ...form, category: e.target.value as PlaceCategory })}
        >
          {ALL_CATEGORIES.map((category) => (
            <option key={category} value={category}>
              {placeCategoryLabel(category)}
            </option>
          ))}
        </select>
      </label>

      <fieldset className="choice-set">
        <legend>Buổi phù hợp</legend>
        {ALL_TIME_SLOTS.map((slot) => (
          <label key={slot} className="checkbox">
            <input
              type="checkbox"
              checked={form.timeSlots.includes(slot)}
              onChange={() => toggleSlot(slot)}
            />
            {timeSlotLabel(slot)}
          </label>
        ))}
      </fieldset>
      {fieldErrors.timeSlots && <p className="field-error">{fieldErrors.timeSlots}</p>}

      <div className="row">
        <label>
          Thời lượng (phút)
          <input
            value={form.estimatedDurationMinutes}
            onChange={(e) => setForm({ ...form, estimatedDurationMinutes: e.target.value })}
            inputMode="numeric"
            required
          />
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
        Giờ mở cửa
        <input
          value={form.openHoursText}
          onChange={(e) => setForm({ ...form, openHoursText: e.target.value })}
          maxLength={200}
          placeholder="08:00 - 17:00"
        />
      </label>

      {(localError ?? submitError) && (
        <p className="form-error" role="alert">
          {localError ?? submitError}
        </p>
      )}

      <button type="submit" disabled={pending}>
        {pending ? <ButtonBusy>Đang lưu…</ButtonBusy> : 'Thêm vào wishlist'}
      </button>
    </form>
  )
}
