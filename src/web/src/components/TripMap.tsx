import { useEffect, useMemo, useRef, useState } from 'react'
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
// Imported here rather than in main.tsx so it travels in the map's own chunk.
// In the entry bundle it was 15KB of CSS every visitor paid for before the
// first paint, including the ones who never open the map.
import 'leaflet/dist/leaflet.css'
import type { PlaceResponse } from '../api/api-types'
import { formatDuration, formatMoney } from '../api/money'
import { allCategoryStyles, categoryStyle, LABEL_ZOOM, markerHtml } from '../map/placeMarkers'
import { IconTarget } from './icons'

/**
 * Each place gets its own icon, because the label is part of it.
 *
 * A DivIcon rather than Leaflet's default image pin: the marker has to carry a
 * colour, a glyph and the place name, and building that from HTML is far
 * simpler than generating five tinted PNGs. It also means the label scales and
 * truncates with CSS instead of being baked into an image.
 */
function iconFor(place: PlaceResponse, selected: boolean, zoom: number): L.DivIcon {
  return new L.DivIcon({
    className: 'place-pin-wrapper',
    html: markerHtml(place, selected, zoom),
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
  /**
   * Where to look while the trip has no places — the geocoded destination.
   * Null until it resolves, and ignored once there is anything to frame.
   */
  destinationCenter?: [number, number] | null
}

const INITIAL_ZOOM = 11

export function TripMap({
  places,
  currency,
  currencyExponent,
  selectedPlaceId,
  onSelectPlace,
  draftLocation,
  onPickLocation,
  routePoints,
  destinationCenter,
}: TripMapProps) {
  const [zoom, setZoom] = useState(INITIAL_ZOOM)

  // Whether labels are drawn at all, rather than the zoom itself: markers only
  // need to be rebuilt when this flips, not on every step of a pinch.
  const labelZoom = zoom >= LABEL_ZOOM

  const center = useMemo<[number, number]>(() => {
    if (places.length === 0) {
      // The trip's own destination first; Mộc Châu only while that is still
      // being looked up, or when the geocoder has never heard of it.
      return destinationCenter ?? FALLBACK_CENTER
    }

    const sum = places.reduce(
      (acc, place) => ({ lat: acc.lat + place.lat, lng: acc.lng + place.lng }),
      { lat: 0, lng: 0 },
    )

    return [sum.lat / places.length, sum.lng / places.length]
  }, [places, destinationCenter])

  return (
    <MapContainer
      center={center}
      zoom={INITIAL_ZOOM}
      scrollWheelZoom
      className="trip-map"
      aria-label="Bản đồ các địa điểm"
      // Out of the tab order, along with every marker: with forty places the
      // map put forty stops between the header and the first thing in the list,
      // and the list is the accessible view of the same data.
      keyboard={false}
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

      <MapFocus
        places={places}
        selectedPlaceId={selectedPlaceId ?? null}
        destinationCenter={destinationCenter ?? null}
      />
      <WatchZoom onChange={setZoom} />
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
          // The zoom is in the key because the icon's HTML depends on it:
          // Leaflet caches a DivIcon's markup, so a marker only redraws when
          // React gives it a new identity.
          key={`${place.id}-${place.category}-${place.status}-${place.id === selectedPlaceId}-${labelZoom}`}
          position={[place.lat, place.lng]}
          icon={iconFor(place, place.id === selectedPlaceId, zoom)}
          keyboard={false}
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

      {/* A key for nothing is noise; the legend only means something once
          there is at least one pin to read it against. */}
      {places.length > 0 && (
        <div className="map-overlay">
          <FitAll places={places} />
          <MapLegend places={places} />
        </div>
      )}
    </MapContainer>
  )
}

/**
 * Back to the whole trip.
 *
 * Panning and zooming are one-way without this: the map framed everything on
 * arrival and then had no way back, so losing your place meant reloading or
 * hunting for the pins by hand. Only offered when there is more than one place,
 * because framing a single pin is what the map is already doing.
 */
function FitAll({ places }: { places: PlaceResponse[] }) {
  const map = useMap()
  const button = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    // Leaflet listens for clicks natively on the container, and React's
    // synthetic stopPropagation does not reach it — so without this, pressing
    // the button also dropped a draft pin wherever it happens to sit.
    if (button.current) {
      L.DomEvent.disableClickPropagation(button.current)
    }
  }, [])

  if (places.length < 2) {
    return null
  }

  return (
    <button
      ref={button}
      type="button"
      className="map-action"
      onClick={() => {
        const bounds = L.latLngBounds(
          places.map((place) => [place.lat, place.lng] as [number, number]),
        )
        map.fitBounds(bounds, { padding: [40, 40], maxZoom: 14 })
      }}
    >
      <IconTarget />
      Xem toàn bộ
    </button>
  )
}

/**
 * What the colours mean. Rendered outside the map panes so it does not pan
 * with the tiles.
 */
function MapLegend({ places }: { places: PlaceResponse[] }) {
  // Only the categories actually on this map. Listing all five for a trip of
  // three restaurants explains four colours that are not there.
  const present = new Set(places.map((place) => place.category))
  const shown = allCategoryStyles().filter((style) => present.has(style.category))

  return (
    <div className="map-legend" aria-label="Chú giải bản đồ">
      {shown.map((style) => (
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

/**
 * Reports the zoom upward, so the markers can decide whether to carry a name.
 * Only `zoomend` — reacting to every frame of a pinch would rebuild forty
 * markers per gesture.
 */
function WatchZoom({ onChange }: { onChange: (zoom: number) => void }) {
  const map = useMapEvents({
    zoomend: () => onChange(map.getZoom()),
  })

  return null
}


/**
 * Decides what the map is looking at, and keeps it sized to its container.
 *
 * One component rather than two, because these are the same decision. Framing
 * the whole trip and flying to one place are alternatives, and as separate
 * effects they fought: selecting a place flew the map, and then the resize
 * that revealed the pane refitted it to every place and undid the fly.
 *
 * Leaflet measures its container once, at construction, and never notices it
 * changing. Below 1024px the map shares the wishlist tab with the list, so it
 * is built inside a `display: none` parent, measures zero, and — when finally
 * revealed — paints a single tile into the corner of a blank area, framed to a
 * viewport that does not exist. A window resize and a phone rotating do the
 * same thing, so a ResizeObserver re-runs the whole decision rather than only
 * the measurement.
 */
function MapFocus({
  places,
  selectedPlaceId,
  destinationCenter,
}: {
  places: PlaceResponse[]
  selectedPlaceId: string | null
  destinationCenter: [number, number] | null
}) {
  const map = useMap()

  useEffect(() => {
    function focus() {
      // A zero-sized map produces NaN for every derived coordinate, and both
      // flyTo and fitBounds throw "Invalid LatLng object" on it, taking the
      // render down with them. Nothing to do until the pane is on screen.
      const size = map.getSize()
      if (size.x === 0 || size.y === 0) {
        return
      }

      const selected = places.find((place) => place.id === selectedPlaceId)
      if (selected) {
        const target = L.latLng(selected.lat, selected.lng)

        // pad() shrinks the bounds, so a pin hard against the edge — half of
        // it behind the legend or the tab bar — still counts as out of view.
        // Not moving for something already in front of you matters: the pan is
        // the feedback, and an unnecessary one is only disorientation.
        if (!map.getBounds().pad(-0.15).contains(target)) {
          map.flyTo(target, Math.max(map.getZoom(), 14), { duration: 0.6 })
        }
        return
      }

      if (places.length > 0) {
        const bounds = L.latLngBounds(
          places.map((place) => [place.lat, place.lng] as [number, number]),
        )
        map.fitBounds(bounds, { padding: [40, 40], maxZoom: 14 })
        return
      }

      // MapContainer reads `center` once, at mount, and the destination is
      // geocoded after that — so an empty trip would sit on the fallback
      // forever unless the move is made imperatively.
      if (destinationCenter) {
        map.setView(destinationCenter, INITIAL_ZOOM, { animate: false })
      }
    }

    focus()

    const observer = new ResizeObserver(() => {
      // No animation: this is a correction, not a movement the user made.
      map.invalidateSize({ animate: false })
      focus()
    })

    observer.observe(map.getContainer())
    return () => observer.disconnect()
  }, [map, places, selectedPlaceId, destinationCenter])

  return null
}
