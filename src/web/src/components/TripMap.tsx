import { useEffect, useMemo } from 'react'
import { MapContainer, Marker, Popup, TileLayer, useMap } from 'react-leaflet'
import L from 'leaflet'
import type { PlaceResponse } from '../api/api-types'
import { formatDuration, formatMoney } from '../api/money'

/**
 * Leaflet ships its default marker icons as separate image files resolved
 * relative to the CSS. A bundler rewrites those paths and the markers silently
 * disappear, so the icon is pointed at the bundled assets explicitly.
 */
const markerIcon = new L.Icon({
  iconUrl: new URL('leaflet/dist/images/marker-icon.png', import.meta.url).href,
  iconRetinaUrl: new URL('leaflet/dist/images/marker-icon-2x.png', import.meta.url).href,
  shadowUrl: new URL('leaflet/dist/images/marker-shadow.png', import.meta.url).href,
  iconSize: [25, 41],
  iconAnchor: [12, 41],
  popupAnchor: [1, -34],
  shadowSize: [41, 41],
})

/** Mộc Châu, used only until the trip has its first place. */
const FALLBACK_CENTER: [number, number] = [20.8386, 104.6383]

interface TripMapProps {
  places: PlaceResponse[]
  currency: string
  currencyExponent: number
  selectedPlaceId?: string | null
  onSelectPlace?: (placeId: string) => void
}

export function TripMap({
  places,
  currency,
  currencyExponent,
  selectedPlaceId,
  onSelectPlace,
}: TripMapProps) {
  const center = useMemo<[number, number]>(() => {
    if (places.length === 0) {
      return FALLBACK_CENTER
    }

    const sum = places.reduce(
      (acc, place) => ({ lat: acc.lat + place.lat, lng: acc.lng + place.lng }),
      { lat: 0, lng: 0 },
    )

    return [sum.lat / places.length, sum.lng / places.length]
  }, [places])

  return (
    <MapContainer
      center={center}
      zoom={11}
      scrollWheelZoom
      className="trip-map"
      aria-label="Bản đồ các địa điểm"
    >
      <TileLayer
        attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
        url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
      />

      <FitToPlaces places={places} />

      {places.map((place) => (
        <Marker
          key={place.id}
          position={[place.lat, place.lng]}
          icon={markerIcon}
          eventHandlers={{ click: () => onSelectPlace?.(place.id) }}
          opacity={selectedPlaceId && selectedPlaceId !== place.id ? 0.6 : 1}
        >
          <Popup>
            <strong>{place.name}</strong>
            <br />
            {place.category} · {formatDuration(place.estimatedDurationMinutes)}
            <br />
            {formatMoney(place.estimatedCost, currency, currencyExponent)}
          </Popup>
        </Marker>
      ))}
    </MapContainer>
  )
}

/** Keeps every place in view as the wishlist grows. */
function FitToPlaces({ places }: { places: PlaceResponse[] }) {
  const map = useMap()

  useEffect(() => {
    if (places.length === 0) {
      return
    }

    const bounds = L.latLngBounds(places.map((place) => [place.lat, place.lng] as [number, number]))
    map.fitBounds(bounds, { padding: [40, 40], maxZoom: 14 })
  }, [map, places])

  return null
}
