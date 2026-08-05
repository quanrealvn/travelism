import type { ExpenseCategory, IsoDate, PlaceCategory, TimeSlot } from './api-types'

/**
 * Vietnamese labels for the domain's enums.
 *
 * The API speaks the enum names the spec defines, which are English. Those are
 * wire values, not words for a person to read — the map legend already said
 * "Ăn uống" while the card beside it said "Food", which is the kind of seam
 * that makes an app feel unfinished. Every user-facing rendering of a category
 * or a time slot goes through here.
 */

const PLACE_CATEGORY: Record<PlaceCategory, string> = {
  Food: 'Ăn uống',
  Sight: 'Tham quan',
  Photo: 'Chụp ảnh',
  Rest: 'Nghỉ ngơi',
  Other: 'Khác',
}

const TIME_SLOT: Record<TimeSlot, string> = {
  Morning: 'Sáng',
  Noon: 'Trưa',
  Afternoon: 'Chiều',
  Evening: 'Tối',
}

const EXPENSE_CATEGORY: Record<ExpenseCategory, string> = {
  Transport: 'Đi lại',
  Lodging: 'Chỗ ở',
  Food: 'Ăn uống',
  Tickets: 'Vé',
  Other: 'Khác',
}

/**
 * Falls back to the wire value rather than to a blank or to "Khác": if the
 * server ever adds a category this build has never heard of, showing its name
 * is more honest than silently filing it under Other.
 */
export function placeCategoryLabel(category: PlaceCategory): string {
  return PLACE_CATEGORY[category] ?? category
}

export function timeSlotLabel(slot: TimeSlot): string {
  return TIME_SLOT[slot] ?? slot
}

export function expenseCategoryLabel(category: ExpenseCategory): string {
  return EXPENSE_CATEGORY[category] ?? category
}

export function timeSlotsLabel(slots: TimeSlot[]): string {
  return slots.map(timeSlotLabel).join(' · ')
}

/** "11/08" — enough to place a day inside a trip nobody plans a year ahead. */
export function shortDate(date: IsoDate): string {
  const [, month, day] = date.split('-')
  return month && day ? `${day}/${month}` : date
}
