import { useEffect, useMemo } from 'react'
import {
  AttributionControl,
  MapContainer,
  Marker,
  Polyline,
  Popup,
  TileLayer,
  useMap,
  useMapEvents,
} from 'react-leaflet'
import L from 'leaflet'
import type { PlaceResponse } from '../api/api-types'
import { formatDuration, formatMoney } from '../api/money'
import { allCategoryStyles, categoryStyle, markerHtml } from '../map/placeMarkers'

/**
 * Each place gets its own icon, because the label is part of it.
 *
 * A DivIcon rather than Leaflet's default image pin: the marker has to carry a
 * colour, a glyph and the place name, and building that from HTML is far
 * simpler than generating five tinted PNGs. It also means the label scales and
 * truncates with CSS instead of being baked into an image.
 */
function iconFor(place: PlaceResponse, selected: boolean): L.DivIcon {
  return new L.DivIcon({
    className: 'place-pin-wrapper',
    html: markerHtml(place, selected),
    // Sized generously and anchored at the dot's centre-bottom: the label
    // overflows the box horizontally, which is fine, but the pin point must sit
    // exactly on the coordinate.
    iconSize: [28, 28],
    iconAnchor: [14, 28],
    popupAnchor: [0, -28],
  })
}

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
  /**
   * The selected day's stops in visiting order. Drawn as a straight-line route:
   * it shows the shape and the back-tracking of a day, and is deliberately not
   * presented as road geometry, which we do not fetch.
   */
  routePoints?: LatLng[]
}

export function TripMap({
  places,
  currency,
  currencyExponent,
  selectedPlaceId,
  onSelectPlace,
  draftLocation,
  onPickLocation,
  routePoints,
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
      // Leaflet puts attribution bottom-right by default, which on a phone is
      // exactly where the floating action button sits. A map does not scroll,
      // so the notice was permanently unreachable rather than briefly covered
      // — and OpenStreetMap's licence requires it to be visible.
      attributionControl={false}
    >
      <AttributionControl position="bottomleft" prefix={false} />

      <TileLayer
        attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
        url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
      />

      <FitToPlaces places={places} />
      {onPickLocation && <ClickToPick onPick={onPickLocation} />}

      {routePoints && routePoints.length > 1 && (
        <Polyline
          positions={routePoints.map((point) => [point.lat, point.lng] as [number, number])}
          pathOptions={{ color: '#1f6f5c', weight: 3, opacity: 0.8, dashArray: '6 6' }}
        />
      )}

      {draftLocation && (
        <Marker position={[draftLocation.lat, draftLocation.lng]} icon={draftIcon}>
          <Popup>Địa điểm đang chọn</Popup>
        </Marker>
      )}

      {places.map((place) => (
        <Marker
          key={`${place.id}-${place.category}-${place.status}-${place.id === selectedPlaceId}`}
          position={[place.lat, place.lng]}
          icon={iconFor(place, place.id === selectedPlaceId)}
          // The selected pin is lifted above the others so its label is not
          // buried under a neighbour's.
          zIndexOffset={place.id === selectedPlaceId ? 1000 : 0}
          eventHandlers={{ click: () => onSelectPlace?.(place.id) }}
        >
          <Popup>
            <strong>{place.name}</strong>
            <br />
            {categoryStyle(place.category).label} ·{' '}
            {formatDuration(place.estimatedDurationMinutes)}
            <br />
            {formatMoney(place.estimatedCost, currency, currencyExponent)}
            <br />
            <span className="popup-status">{place.status}</span>
          </Popup>
        </Marker>
      ))}

      <MapLegend />
    </MapContainer>
  )
}

/**
 * What the colours mean. Rendered outside the map panes so it does not pan
 * with the tiles.
 */
function MapLegend() {
  return (
    <div className="map-legend" aria-label="Chú giải bản đồ">
      {allCategoryStyles().map((style) => (
        <span key={style.category} className="legend-item">
          <span className="legend-swatch" style={{ background: style.color }} aria-hidden="true" />
          {style.label}
        </span>
      ))}
    </div>
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
/**
 * Keeps the map filling its container and framing the trip.
 *
 * Leaflet measures its container once, at construction, and never notices it
 * changing. Below 1024px the map shares the wishlist tab with the list and
 * only one is shown at a time, so the map is built inside a `display: none`
 * parent, measures zero, and — when finally revealed — paints a single tile
 * into the corner of a blank area, framed to a viewport that does not exist.
 * A window resize and a phone rotating do the same thing.
 *
 * Sizing and framing are handled together because they answer to the same
 * event: re-measuring without re-framing leaves the map correctly sized on the
 * wrong part of the world.
 */
function FitToPlaces({ places }: { places: PlaceResponse[] }) {
  const map = useMap()

  useEffect(() => {
    function fit() {
      if (places.length === 0) {
        return
      }

      const bounds = L.latLngBounds(
        places.map((place) => [place.lat, place.lng] as [number, number]),
      )
      map.fitBounds(bounds, { padding: [40, 40], maxZoom: 14 })
    }

    fit()

    const container = map.getContainer()
    const observer = new ResizeObserver(() => {
      // No animation: this is a correction, not a movement the user made.
      map.invalidateSize({ animate: false })
      fit()
    })

    observer.observe(container)
    return () => observer.disconnect()
  }, [map, places])

  return null
}
