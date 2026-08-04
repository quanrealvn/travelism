import { useEffect, useMemo } from 'react'
import { MapContainer, Marker, Popup, TileLayer, useMap, useMapEvents } from 'react-leaflet'
import L from 'leaflet'
import markerIconUrl from 'leaflet/dist/images/marker-icon.png'
import markerIconRetinaUrl from 'leaflet/dist/images/marker-icon-2x.png'
import markerShadowUrl from 'leaflet/dist/images/marker-shadow.png'
import type { PlaceResponse } from '../api/api-types'
import { formatDuration, formatMoney } from '../api/money'

/**
 * Leaflet resolves its default marker images relative to its own CSS, which a
 * bundler rewrites — so out of the box every pin is a broken image.
 *
 * These must be real `import` statements. Writing
 * `new URL('leaflet/dist/images/marker-icon.png', import.meta.url)` looks
 * equivalent but is not: Vite only rewrites that form for relative paths, so a
 * bare package specifier is left alone, the images are never emitted, and the
 * URLs 404 at runtime with no error in the console.
 */
const markerIcon = new L.Icon({
  iconUrl: markerIconUrl,
  iconRetinaUrl: markerIconRetinaUrl,
  shadowUrl: markerShadowUrl,
  iconSize: [25, 41],
  iconAnchor: [12, 41],
  popupAnchor: [1, -34],
  shadowSize: [41, 41],
})

/**
 * A hollow marker for the not-yet-saved location, visibly different from the
 * places already on the wishlist.
 */
const draftIcon = new L.DivIcon({
  className: 'draft-marker',
  html: '<span aria-hidden="true">📍</span>',
  iconSize: [28, 28],
  iconAnchor: [14, 28],
})

/** Mộc Châu, used only until the trip has its first place. */
const FALLBACK_CENTER: [number, number] = [20.8386, 104.6383]

export interface LatLng {
  lat: number
  lng: number
}

interface TripMapProps {
  places: PlaceResponse[]
  currency: string
  currencyExponent: number
  selectedPlaceId?: string | null
  onSelectPlace?: (placeId: string) => void
  /** The location being composed in the add-place form, if any. */
  draftLocation?: LatLng | null
  /** Clicking the map picks a location — the escape hatch when search cannot find a place. */
  onPickLocation?: (location: LatLng) => void
}

export function TripMap({
  places,
  currency,
  currencyExponent,
  selectedPlaceId,
  onSelectPlace,
  draftLocation,
  onPickLocation,
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
      {onPickLocation && <ClickToPick onPick={onPickLocation} />}

      {draftLocation && (
        <Marker position={[draftLocation.lat, draftLocation.lng]} icon={draftIcon}>
          <Popup>Địa điểm đang chọn</Popup>
        </Marker>
      )}

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

/**
 * Turns a map click into a chosen location. Not every place in Vietnam is in
 * OpenStreetMap — a new homestay or a roadside quán may simply not be there —
 * so pointing at it on the map has to work regardless of what search can find.
 */
function ClickToPick({ onPick }: { onPick: (location: LatLng) => void }) {
  useMapEvents({
    click: (event) => onPick({ lat: event.latlng.lat, lng: event.latlng.lng }),
  })

  return null
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
