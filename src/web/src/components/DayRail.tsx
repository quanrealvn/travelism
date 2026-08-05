import type { IsoDate, WeatherResponse } from '../api/api-types'
import { formatDayLabel } from '../itinerary/tripDates'
import { formatTemp, weatherText } from '../itinerary/weatherText'

interface DayRailProps {
  weather: WeatherResponse | null | undefined
  days: IsoDate[]
  selectedDate: IsoDate | null
  onSelectDate: (date: IsoDate) => void
}

/**
 * The trip's days, and the forecast for each.
 *
 * This is the day switcher, not a weather widget — on a phone only the selected
 * day's column is on screen, so this rail is the only way to reach the others.
 * It therefore renders whenever the trip has days, and the forecast decorates
 * it when there is one. (It was previously a WeatherStrip that returned null
 * with no forecast, which would have stranded a phone on day one every time the
 * weather service was unreachable.)
 *
 * A day with no forecast still gets a card, so the rail stays lined up with the
 * columns below it.
 */
export function DayRail({ weather, days, selectedDate, onSelectDate }: DayRailProps) {
  if (days.length === 0) {
    return null
  }

  const byDate = new Map((weather?.days ?? []).map((day) => [day.date, day]))

  return (
    <div className="day-rail">
      {weather?.stale && (
        <p className="weather-stale" role="status">
          Dự báo cũ (không kết nối được dịch vụ thời tiết).
        </p>
      )}

      <ul aria-label="Ngày trong chuyến đi">
        {days.map((date) => {
          const day = byDate.get(date)
          const { icon, label } = weatherText(day?.weatherCode ?? null)

          return (
            <li key={date}>
              <button
                type="button"
                className={date === selectedDate ? 'day-chip selected' : 'day-chip'}
                aria-pressed={date === selectedDate}
                onClick={() => onSelectDate(date)}
                data-testid={`weather-${date}`}
              >
                <span className="weather-date">{formatDayLabel(date)}</span>

                {/* Without a forecast the icon would be a permanent question
                    mark on every card, which reads as an error rather than as
                    "we simply do not know". */}
                {weather && (
                  <>
                    <span className="weather-icon" aria-hidden="true">
                      {icon}
                    </span>
                    <span className="weather-label">{label}</span>
                  </>
                )}

                {day && (
                  <span className="weather-temp">
                    {formatTemp(day.maxTempC)} / {formatTemp(day.minTempC)}
                  </span>
                )}
                {day && day.precipitationMm !== null && day.precipitationMm > 0 && (
                  <span className="weather-rain">{day.precipitationMm.toFixed(1)} mm</span>
                )}
              </button>
            </li>
          )
        })}
      </ul>
    </div>
  )
}
