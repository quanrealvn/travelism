import { useState } from 'react'
import type { FormEvent } from 'react'
import { ALL_CATEGORIES, ALL_TIME_SLOTS } from '../api/api-types'
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
}: PlaceFormProps) {
  const [form, setForm] = useState(EMPTY)
  const [localError, setLocalError] = useState<string | null>(null)
  const [pickedAddress, setPickedAddress] = useState<string | null>(null)
  const [manualCoords, setManualCoords] = useState(false)

  function applySearchResult(result: GeocodeResultResponse) {
    setForm((current) => ({
      ...current,
      // The typed name is only replaced when the field is still untouched, so
      // picking a location never overwrites a name the user chose themselves.
      name: current.name.trim() === '' ? result.name : current.name,
      lat: String(result.lat),
      lng: String(result.lng),
    }))
    setPickedAddress(result.displayName)
    setLocalError(null)
  }

  function toggleSlot(slot: TimeSlot) {
    setForm((current) => ({
      ...current,
      timeSlots: current.timeSlots.includes(slot)
        ? current.timeSlots.filter((s) => s !== slot)
        : [...current.timeSlots, slot],
    }))
  }

  function reset() {
    setForm(EMPTY)
    setPickedAddress(null)
    setManualCoords(false)
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

    reset()
  }

  const hasCoordinates = form.lat.trim() !== '' && form.lng.trim() !== ''

  return (
    <form className="place-form" onSubmit={handleSubmit} aria-label="Thêm địa điểm">
      <h2>Thêm địa điểm</h2>

      <PlaceSearch tripId={tripId} onPick={applySearchResult} />

      {hasCoordinates && (
        <p className="picked-location" data-testid="picked-location">
          📍 {pickedAddress ?? `${form.lat}, ${form.lng}`}
          <button
            type="button"
            className="link-button"
            onClick={() => {
              setForm((current) => ({ ...current, lat: '', lng: '' }))
              setPickedAddress(null)
            }}
          >
            đổi
          </button>
        </p>
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
              onChange={(e) => setForm({ ...form, lat: e.target.value })}
              inputMode="decimal"
              placeholder="20.8386"
            />
          </label>
          <label>
            Kinh độ (lng)
            <input
              value={form.lng}
              onChange={(e) => setForm({ ...form, lng: e.target.value })}
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
              {category}
            </option>
          ))}
        </select>
      </label>

      <fieldset>
        <legend>Buổi phù hợp</legend>
        {ALL_TIME_SLOTS.map((slot) => (
          <label key={slot} className="checkbox">
            <input
              type="checkbox"
              checked={form.timeSlots.includes(slot)}
              onChange={() => toggleSlot(slot)}
            />
            {slot}
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
        {pending ? 'Đang lưu…' : 'Thêm vào wishlist'}
      </button>
    </form>
  )
}
