import { useState } from 'react'
import {
  DndContext,
  DragOverlay,
  PointerSensor,
  useDraggable,
  useDroppable,
  useSensor,
  useSensors,
} from '@dnd-kit/core'
import type { DragEndEvent, DragStartEvent } from '@dnd-kit/core'
import type {
  FeasibilityFindingResponse,
  IsoDate,
  ItineraryItemResponse,
  PlaceResponse,
} from '../api/api-types'
import { formatDuration, formatMoney } from '../api/money'
import { formatDayLabel, formatTime } from '../itinerary/tripDates'
import { FeasibilityBadges } from './FeasibilityBadges'
import { worstLevel } from '../itinerary/feasibilityText'
import { IconClose, IconPlus } from './icons'

interface ItineraryBoardProps {
  days: IsoDate[]
  items: ItineraryItemResponse[]
  confirmedPlaces: PlaceResponse[]
  currency: string
  currencyExponent: number
  movingItemId: string | null
  /** Findings for the selected day only — feasibility is a per-day question. */
  findings: FeasibilityFindingResponse[]
  selectedDate: IsoDate | null
  onSelectDate: (date: IsoDate) => void
  onMoveItem: (itemId: string, toDate: IsoDate) => void
  onSchedulePlace: (placeId: string, date: IsoDate) => void
  onRemoveItem: (itemId: string) => void
  onSetTime: (itemId: string, startTime: string | null) => void
}

/**
 * Drag payloads are tagged so a drop can tell a move from a first scheduling.
 * Both carry placeId, so a column can decide whether it would end up with the
 * same place twice without having to look the dragged item up elsewhere.
 */
type DragData =
  | { kind: 'item'; itemId: string; placeId: string; fromDate: IsoDate }
  | { kind: 'place'; placeId: string }

/**
 * The day-by-day plan. Confirmed places can be dragged in from the left, and
 * scheduled items dragged between days.
 */
export function ItineraryBoard({
  days,
  items,
  confirmedPlaces,
  currency,
  currencyExponent,
  movingItemId,
  findings,
  selectedDate,
  onSelectDate,
  onMoveItem,
  onSchedulePlace,
  onRemoveItem,
  onSetTime,
}: ItineraryBoardProps) {
  const [dragging, setDragging] = useState<DragData | null>(null)

  // A small activation distance so tapping a card to edit it is not read as a
  // drag — important on touch, where every tap moves a pixel or two.
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 6 } }))

  const confirmedPlaceIds = new Set(confirmedPlaces.map((place) => place.id))

  const scheduledPlaceIdsByDate = new Map<IsoDate, Set<string>>()
  for (const item of items) {
    const set = scheduledPlaceIdsByDate.get(item.date) ?? new Set<string>()
    set.add(item.placeId)
    scheduledPlaceIdsByDate.set(item.date, set)
  }

  function handleDragStart(event: DragStartEvent) {
    setDragging((event.active.data.current as DragData | undefined) ?? null)
  }

  function handleDragEnd(event: DragEndEvent) {
    setDragging(null)

    const toDate = event.over?.id
    const data = event.active.data.current as DragData | undefined
    if (typeof toDate !== 'string' || !data) {
      return
    }

    if (data.kind === 'item') {
      if (data.fromDate !== toDate) {
        onMoveItem(data.itemId, toDate)
      }
      return
    }

    onSchedulePlace(data.placeId, toDate)
  }

  return (
    <DndContext
      sensors={sensors}
      onDragStart={handleDragStart}
      onDragEnd={handleDragEnd}
      onDragCancel={() => setDragging(null)}
    >
      <div className="itinerary">
        <aside className="itinerary-pool">
          <h3>Đã chốt</h3>
          {confirmedPlaces.length === 0 ? (
            <p className="empty-state small">
              Chưa có địa điểm nào được chốt. Thích một địa điểm để cả nhóm cùng
              đồng ý, rồi thêm vào ngày.
            </p>
          ) : (
            <ul className="pool-list">
              {confirmedPlaces.map((place) => (
                <DraggablePlace
                  key={place.id}
                  place={place}
                  currency={currency}
                  currencyExponent={currencyExponent}
                  targetDate={selectedDate}
                  // mirror of server rule (spec §6): one place per date. The
                  // card stays — it can still be dragged to another day — but
                  // the button that would add it where it already is does not.
                  alreadyOnTargetDate={
                    selectedDate !== null &&
                    (scheduledPlaceIdsByDate.get(selectedDate)?.has(place.id) ?? false)
                  }
                  onAdd={onSchedulePlace}
                />
              ))}
            </ul>
          )}
        </aside>

        <div className="itinerary-days">
          {days.map((date) => (
            <DayColumn
              key={date}
              date={date}
              items={items.filter((item) => item.date === date)}
              currency={currency}
              currencyExponent={currencyExponent}
              movingItemId={movingItemId}
              confirmedPlaceIds={confirmedPlaceIds}
              // Feasibility is fetched for the selected day, so other columns
              // show no badges rather than stale ones from another day.
              findings={selectedDate === date ? findings : []}
              selected={selectedDate === date}
              onSelect={() => onSelectDate(date)}
              onRemoveItem={onRemoveItem}
              onSetTime={onSetTime}
              alreadyHere={scheduledPlaceIdsByDate.get(date) ?? new Set<string>()}
              dragging={dragging}
            />
          ))}
        </div>
      </div>

      <DragOverlay>
        {dragging && <div className="drag-ghost">Đang kéo…</div>}
      </DragOverlay>
    </DndContext>
  )
}

function DraggablePlace({
  place,
  currency,
  currencyExponent,
  targetDate,
  alreadyOnTargetDate,
  onAdd,
}: {
  place: PlaceResponse
  currency: string
  currencyExponent: number
  targetDate: IsoDate | null
  alreadyOnTargetDate: boolean
  onAdd: (placeId: string, date: IsoDate) => void
}) {
  const { attributes, listeners, setNodeRef, isDragging } = useDraggable({
    id: `place:${place.id}`,
    data: { kind: 'place', placeId: place.id } satisfies DragData,
  })

  return (
    <li
      ref={setNodeRef}
      className={isDragging ? 'pool-item dragging' : 'pool-item'}
      {...listeners}
      {...attributes}
      data-testid={`pool-${place.id}`}
    >
      <span className="pool-name">{place.name}</span>
      <span className="pool-meta">
        {formatDuration(place.estimatedDurationMinutes)} ·{' '}
        {formatMoney(place.estimatedCost, currency, currencyExponent)}
      </span>

      {/*
        Dragging a card across a column is a mouse gesture; on a phone there is
        nowhere to drag to, because only the selected day is on screen. This
        button adds the place to that day in one tap. The pointer sensor needs
        6px of movement before it claims the gesture, so a tap still reaches
        the click handler.
      */}
      {targetDate !== null &&
        (alreadyOnTargetDate ? (
          <span className="pool-done" title={`Đã có trong ${formatDayLabel(targetDate)}`}>
            ✓
          </span>
        ) : (
          <button
            type="button"
            className="pool-add"
            onClick={() => onAdd(place.id, targetDate)}
            aria-label={`Thêm ${place.name} vào ${formatDayLabel(targetDate)}`}
          >
            <IconPlus />
          </button>
        ))}
    </li>
  )
}

function DayColumn({
  date,
  items,
  currency,
  currencyExponent,
  movingItemId,
  confirmedPlaceIds,
  findings,
  selected,
  onSelect,
  onRemoveItem,
  onSetTime,
  alreadyHere,
  dragging,
}: {
  date: IsoDate
  items: ItineraryItemResponse[]
  currency: string
  currencyExponent: number
  movingItemId: string | null
  confirmedPlaceIds: Set<string>
  findings: FeasibilityFindingResponse[]
  selected: boolean
  onSelect: () => void
  onRemoveItem: (itemId: string) => void
  onSetTime: (itemId: string, startTime: string | null) => void
  alreadyHere: Set<string>
  dragging: DragData | null
}) {
  const { setNodeRef, isOver } = useDroppable({ id: date })

  // mirror of server rule (spec §6): a place may appear at most once per date.
  // Marking the column warns before the drop; the server still decides, and a
  // refused move is still rolled back.
  const draggingWithinThisDay = dragging?.kind === 'item' && dragging.fromDate === date
  const wouldDuplicate =
    dragging !== null && !draggingWithinThisDay && alreadyHere.has(dragging.placeId)

  const className = [
    'day-column',
    selected ? 'selected' : '',
    isOver ? 'over' : '',
    wouldDuplicate ? 'would-duplicate' : '',
  ]
    .filter(Boolean)
    .join(' ')

  const dayCost = items.reduce(
    (total, item) => total + (item.actualCost ?? item.estimatedCost ?? 0),
    0,
  )

  const dayLevel = worstLevel(findings)

  return (
    <section ref={setNodeRef} className={className} data-testid={`day-${date}`}>
      <header>
        <button type="button" className="day-heading" onClick={onSelect}>
          {formatDayLabel(date)}
        </button>
        <span className="day-summary">
          {items.length} điểm · {formatMoney(dayCost, currency, currencyExponent)}
          {dayLevel && (
            <span className={`day-verdict day-verdict-${dayLevel}`} data-testid={`verdict-${date}`}>
              {dayLevel === 'error' ? 'không kịp' : dayLevel === 'warning' ? 'cần xem lại' : 'ổn'}
            </span>
          )}
        </span>
      </header>

      {items.length === 0 ? (
        <p className="day-empty">Kéo địa điểm vào đây</p>
      ) : (
        <ul className="day-items">
          {items.map((item) => (
            <ItemCard
              key={item.id}
              item={item}
              currency={currency}
              currencyExponent={currencyExponent}
              busy={movingItemId === item.id}
              unconfirmed={!confirmedPlaceIds.has(item.placeId)}
              findings={findings.filter((f) => f.itineraryItemId === item.id)}
              onRemove={() => onRemoveItem(item.id)}
              onSetTime={onSetTime}
            />
          ))}
        </ul>
      )}
    </section>
  )
}

function ItemCard({
  item,
  currency,
  currencyExponent,
  busy,
  unconfirmed,
  findings,
  onRemove,
  onSetTime,
}: {
  item: ItineraryItemResponse
  currency: string
  currencyExponent: number
  busy: boolean
  unconfirmed: boolean
  findings: FeasibilityFindingResponse[]
  onRemove: () => void
  onSetTime: (itemId: string, startTime: string | null) => void
}) {
  const { attributes, listeners, setNodeRef, isDragging } = useDraggable({
    id: `item:${item.id}`,
    data: {
      kind: 'item',
      itemId: item.id,
      placeId: item.placeId,
      fromDate: item.date,
    } satisfies DragData,
  })

  return (
    <li
      ref={setNodeRef}
      className={`item-card${isDragging ? ' dragging' : ''}${busy ? ' busy' : ''}`}
      data-testid={`item-${item.id}`}
    >
      <span className="item-grip" {...listeners} {...attributes} aria-label={`Kéo ${item.placeName}`}>
        ⠿
      </span>

      <div className="item-body">
        <span className="item-name">
          {item.placeName}
          {/*
            The itinerary is built from confirmed places, but nothing keeps it
            consistent when one leaves that set: un-confirming a scheduled place
            left the stop on the plan with no sign that the group had changed
            its mind about it.
          */}
          {unconfirmed && (
            <span className="item-unconfirmed" title="Địa điểm này không còn được chốt">
              chưa chốt lại
            </span>
          )}
        </span>
        <span className="item-meta">
          {formatDuration(item.estimatedDurationMinutes)} ·{' '}
          {formatMoney(item.actualCost ?? item.estimatedCost, currency, currencyExponent)}
        </span>
        {item.note && <span className="item-note">{item.note}</span>}
        <FeasibilityBadges findings={findings} />
      </div>

      <input
        type="time"
        className="item-time"
        value={formatTime(item.startTime) ?? ''}
        onChange={(e) => onSetTime(item.id, e.target.value === '' ? null : `${e.target.value}:00`)}
        aria-label={`Giờ bắt đầu cho ${item.placeName}`}
      />

      <button type="button" className="item-remove" onClick={onRemove} aria-label={`Bỏ ${item.placeName}`}>
        <IconClose />
      </button>
    </li>
  )
}
