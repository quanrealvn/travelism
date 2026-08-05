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

/**
 * Vietnamese for the fields the server can reject.
 *
 * The client mirrors the rules it knows about, so these only appear where the
 * server is the sole validator — and there the whole app switched to English
 * mid-sentence.
 */
const FIELD_LABEL: Record<string, string> = {
  name: 'Tên',
  destination: 'Điểm đến',
  startDate: 'Ngày bắt đầu',
  endDate: 'Ngày kết thúc',
  displayName: 'Tên hiển thị',
  ownerDisplayName: 'Tên hiển thị',
  inviteCode: 'Mã mời',
  lat: 'Vĩ độ',
  lng: 'Kinh độ',
  category: 'Loại',
  timeSlots: 'Buổi phù hợp',
  estimatedDurationMinutes: 'Thời lượng',
  estimatedCost: 'Chi phí ước tính',
  openHoursText: 'Giờ mở cửa',
  description: 'Mô tả',
  references: 'Link tham khảo',
  title: 'Nội dung',
  amount: 'Số tiền',
  date: 'Ngày',
  budgetAmount: 'Ngân sách',
  startTime: 'Giờ bắt đầu',
}

const FIELD_ERROR: Record<string, (field: string) => string> = {
  REQUIRED: (field) => `${field} không được để trống.`,
  TOO_LONG: (field) => `${field} quá dài.`,
  OUT_OF_RANGE: (field) => `${field} nằm ngoài khoảng cho phép.`,
  INVALID: (field) => `${field} không hợp lệ.`,
  INVALID_FORMAT: (field) => `${field} sai định dạng.`,
}

/**
 * A server field error, in Vietnamese where we know the code.
 *
 * Falls back to the server's English message rather than to something vague:
 * an untranslated sentence that says what is wrong beats a translated one that
 * does not. A new code shows up in the wrong language, which is visible, rather
 * than silently becoming "không hợp lệ".
 */
export function fieldErrorText(field: string, code: string, fallback: string): string {
  const label = FIELD_LABEL[field] ?? field
  return FIELD_ERROR[code]?.(label) ?? fallback
}

/** Whole-request failures, keyed by the server's stable code. */
const PROBLEM_TEXT: Record<string, string> = {
  VALIDATION_FAILED: 'Có thông tin chưa hợp lệ.',
  MALFORMED_JSON: 'Dữ liệu gửi lên không đọc được.',
  NOT_FOUND: 'Không tìm thấy.',
  FORBIDDEN: 'Bạn không có quyền với chuyến đi này.',
  UNAUTHENTICATED: 'Phiên đăng nhập đã hết hạn. Tải lại trang nhé.',
  NAME_TAKEN: 'Tên này đã có người dùng trong chuyến đi.',
  TRIP_FULL: 'Chuyến đi đã đủ người.',
  DEVICE_TRIP_LIMIT:
    'Thiết bị này đang giữ tối đa số chuyến đi. Bỏ bớt một chuyến rồi thử lại.',
  DUPLICATE_PLACE_ON_DATE: 'Địa điểm này đã có trong ngày đó rồi.',
  DATE_OUT_OF_RANGE: 'Ngày đó nằm ngoài chuyến đi.',
  INVALID_STATUS_TRANSITION: 'Không đổi được trạng thái này.',
  GEOCODING_UNAVAILABLE: 'Không tìm được lúc này. Thử dán link Google Maps.',
  LINK_NOT_RECOGNISED: 'Link này không chứa vị trí.',
  WEATHER_UNAVAILABLE: 'Chưa có dự báo thời tiết cho những ngày này.',
  INTERNAL_ERROR: 'Có lỗi xảy ra. Thử lại nhé.',
}

export function problemText(code: string, fallback: string): string {
  return PROBLEM_TEXT[code] ?? fallback
}

/**
 * A timezone as a person would name it.
 *
 * "Asia/Ho_Chi_Minh" is an IANA identifier — correct, and not a thing to show
 * somebody as a fact about their holiday. The offset is computed rather than
 * written down, so it stays right if a zone ever changes.
 */
export function timeZoneLabel(timeZoneId: string): string {
  const named: Record<string, string> = {
    'Asia/Ho_Chi_Minh': 'Việt Nam',
    'Asia/Bangkok': 'Thái Lan',
    'Asia/Vientiane': 'Lào',
    'Asia/Phnom_Penh': 'Campuchia',
  }

  const name = named[timeZoneId] ?? timeZoneId.split('/').pop()?.replace(/_/g, ' ') ?? timeZoneId

  try {
    const offset = new Intl.DateTimeFormat('vi-VN', {
      timeZone: timeZoneId,
      timeZoneName: 'shortOffset',
    })
      .formatToParts(new Date())
      .find((part) => part.type === 'timeZoneName')?.value

    return offset ? `${name} (${offset})` : name
  } catch {
    // An identifier this browser does not know. The name alone still reads.
    return name
  }
}
