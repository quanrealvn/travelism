import { useState } from 'react'
import type { PlaceReferenceRequest, PlaceResponse } from '../api/api-types'
import { IconClose, IconLink, IconPencil, IconPlus } from './icons'
import { ButtonBusy } from './Spinner'

interface PlaceDetailProps {
  place: PlaceResponse
  saving: boolean
  onSave: (description: string | null, references: PlaceReferenceRequest[]) => void
}

/**
 * The "why is this on the list" panel: a note and the links behind it.
 *
 * Read-only until you press edit, because a wishlist is mostly read — putting
 * every place into a form permanently would bury the actual list in inputs.
 */
export function PlaceDetail({ place, saving, onSave }: PlaceDetailProps) {
  const [editing, setEditing] = useState(false)

  if (editing) {
    return (
      <PlaceDetailEditor
        place={place}
        saving={saving}
        onCancel={() => setEditing(false)}
        onSave={(description, references) => {
          onSave(description, references)
          setEditing(false)
        }}
      />
    )
  }

  const hasDetail = Boolean(place.description) || place.references.length > 0

  return (
    <div className="place-detail">
      {place.description && <p className="detail-description">{place.description}</p>}

      {/*
        The edit trigger rides in the same chip row as the links rather than
        sitting on a line of its own. Repeated down a wishlist it was the
        loudest recurring element on the screen, which is the wrong weight for
        something you touch once per place.
      */}
      <ul className="detail-links">
        {place.references.map((reference) => (
          <li key={reference.id}>
            <a
              href={reference.url}
              target="_blank"
              // noreferrer implies noopener, but both are stated: this opens
              // a page nobody on the trip controls.
              rel="noreferrer noopener"
              title={reference.url}
            >
              <IconLink />
              {reference.displayName}
            </a>
          </li>
        ))}

        <li>
          <button type="button" className="detail-edit" onClick={() => setEditing(true)}>
            <IconPencil />
            {hasDetail ? 'Sửa mô tả' : 'Thêm mô tả'}
          </button>
        </li>
      </ul>
    </div>
  )
}

interface EditorRow {
  url: string
  label: string
}

function PlaceDetailEditor({
  place,
  saving,
  onCancel,
  onSave,
}: {
  place: PlaceResponse
  saving: boolean
  onCancel: () => void
  onSave: (description: string | null, references: PlaceReferenceRequest[]) => void
}) {
  const [description, setDescription] = useState(place.description ?? '')
  const [rows, setRows] = useState<EditorRow[]>(
    place.references.length > 0
      ? place.references.map((r) => ({ url: r.url, label: r.label ?? '' }))
      : [{ url: '', label: '' }],
  )

  function updateRow(index: number, patch: Partial<EditorRow>) {
    setRows((current) => current.map((row, i) => (i === index ? { ...row, ...patch } : row)))
  }

  function submit() {
    // Blank rows are dropped rather than flagged — an empty row is somebody who
    // changed their mind. The server drops them too.
    const references = rows
      .filter((row) => row.url.trim() !== '')
      .map((row) => ({
        url: row.url.trim(),
        label: row.label.trim() === '' ? null : row.label.trim(),
      }))

    onSave(description.trim() === '' ? null : description.trim(), references)
  }

  return (
    <div className="place-detail editing">
      <label>
        Mô tả
        <textarea
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          rows={3}
          maxLength={2000}
          placeholder="Vì sao nên đi? Cần lưu ý gì?"
          aria-label={`Mô tả cho ${place.name}`}
        />
      </label>

      <span className="detail-links-label">Link tham khảo</span>
      {rows.map((row, index) => (
        <div className="detail-link-row" key={index}>
          <input
            value={row.url}
            onChange={(e) => updateRow(index, { url: e.target.value })}
            placeholder="https://…"
            aria-label={`Link ${index + 1}`}
          />
          <input
            value={row.label}
            onChange={(e) => updateRow(index, { label: e.target.value })}
            placeholder="Tên hiển thị (không bắt buộc)"
            aria-label={`Tên link ${index + 1}`}
          />
          <button
            type="button"
            className="place-delete"
            onClick={() => setRows((current) => current.filter((_, i) => i !== index))}
            aria-label={`Bỏ link ${index + 1}`}
          >
            <IconClose />
          </button>
        </div>
      ))}

      {rows.length < 10 && (
        <button
          type="button"
          className="detail-edit"
          onClick={() => setRows((current) => [...current, { url: '', label: '' }])}
        >
          <IconPlus />
          Thêm link
        </button>
      )}

      <div className="detail-actions">
        <button type="button" onClick={submit} disabled={saving}>
          {saving ? <ButtonBusy>Đang lưu…</ButtonBusy> : 'Lưu'}
        </button>
        <button type="button" className="link-button" onClick={onCancel}>
          Huỷ
        </button>
      </div>
    </div>
  )
}
