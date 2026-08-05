import type { IsoDate, WeatherResponse } from '../api/api-types'
import { formatDayLabel } from '../itinerary/tripDates'
import { formatTemp, weatherText } from '../itinerary/weatherText'

interface WeatherStripProps {
  weather: WeatherResponse | null | undefined
  days: IsoDate[]
  selectedDate: IsoDate | null
  onSelectDate: (date: IsoDate) => void
}

/**
 * Forecast per trip day, aligned with the itinerary columns.
 *
 * A day with no forecast renders as a blank card rather than being omitted, so
 * the strip stays lined up with the days below it.
 */
export function WeatherStrip({ weather, days, selectedDate, onSelectDate }: WeatherStripProps) {
  if (!weather) {
    return null
  }

  const byDate = new Map(weather.days.map((day) => [day.date, day]))

  return (
    <div className="weather-strip">
      {weather.stale && (
        <p className="weather-stale" role="status">
          Dự báo cũ (không kết nối được dịch vụ thời tiết).
        </p>
      )}

      <ul>
        {days.map((date) => {
          const day = byDate.get(date)
          const { icon, label } = weatherText(day?.weatherCode ?? null)

          return (
            <li key={date}>
              <button
                type="button"
                className={date === selectedDate ? 'weather-day selected' : 'weather-day'}
                onClick={() => onSelectDate(date)}
                data-testid={`weather-${date}`}
              >
                <span className="weather-date">{formatDayLabel(date)}</span>
                <span className="weather-icon" aria-hidden="true">
                  {icon}
                </span>
                <span className="weather-label">{label}</span>
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
