import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnDestroy,
  afterNextRender,
  effect,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import * as L from 'leaflet';

/** A point on the map, in the order the API and the domain use. */
export interface MapPoint {
  readonly latitude: number;
  readonly longitude: number;
}

/**
 * One place on a real map.
 *
 * This replaces a decorative circle-on-a-gradient that had been standing in for a map since the
 * console was built: it drew the same ring in the same spot whatever the coordinates were, so a
 * dealer in Aqaba and a dealer in Irbid saw an identical picture and neither could tell whether the
 * numbers underneath were right. The dealer profile said as much in its own hint — "a map picker is
 * not wired yet".
 *
 * Raster tiles, deliberately. The BFF sends `img-src 'self' data: https:` but `connect-src 'self'`
 * (Khadra.Bff/Program.cs), so tiles fetched as images load and a vector basemap — which pulls its
 * style and glyphs over fetch() — would be blocked with nothing on screen to say why. The same rule
 * is why there is no address search here: geocoding is an XHR to a third party, and it needs a
 * server-side proxy before this screen can offer it.
 *
 * Leaflet is imperative and owns its DOM subtree, so it is driven from effects rather than the
 * template, and every input change is pushed into the instance by hand.
 */
@Component({
  selector: 'kh-map',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './map.component.html',
})
export class MapComponent implements OnDestroy {
  private readonly host = inject(ElementRef<HTMLElement>);

  readonly latitude = input.required<number>();
  readonly longitude = input.required<number>();

  /** Whether the reader can move the pin. Read-only by default: most maps here only show a place. */
  readonly editable = input(false);
  readonly zoom = input(14);
  /** Kilometres. Draws the delivery radius around the pin, to scale, when set. */
  readonly radiusKm = input<number | null>(null);
  readonly label = input('Location');

  /**
   * How tall to draw it. An input rather than a class on the host, because the size class has to
   * land on the element the height rule targets and a host class does not reach inside.
   */
  readonly size = input<'short' | 'default' | 'tall'>('default');

  /** Fires only on a deliberate move — dragging the pin or clicking the map, never on a redraw. */
  readonly moved = output<MapPoint>();

  private readonly canvas = viewChild.required<ElementRef<HTMLDivElement>>('canvas');

  private map: L.Map | null = null;
  private marker: L.Marker | null = null;
  private ring: L.Circle | null = null;
  private resize: ResizeObserver | null = null;

  /** Set while this component is writing its own coordinates back, so the effect does not fight it. */
  private selfMove = false;

  protected readonly ready = signal(false);

  constructor() {
    afterNextRender(() => this.build());

    // Coordinates typed into the fields beside the map move the pin, and the pin moves them back.
    effect(() => {
      const point = { latitude: this.latitude(), longitude: this.longitude() };
      const editable = this.editable();
      const zoom = this.zoom();
      const radius = this.radiusKm();
      if (!this.map || this.selfMove) return;
      if (!Number.isFinite(point.latitude) || !Number.isFinite(point.longitude)) return;

      const at = L.latLng(point.latitude, point.longitude);
      this.map.setView(at, zoom, { animate: false });
      this.marker?.setLatLng(at);
      this.marker?.dragging?.[editable ? 'enable' : 'disable']();
      this.drawRadius(at, radius);
    });
  }

  ngOnDestroy(): void {
    this.resize?.disconnect();
    this.resize = null;
    this.map?.remove();
    this.map = null;
  }

  /** Recentres on the pin — after typing coordinates, the map may have been panned away from it. */
  protected recentre(): void {
    const at = L.latLng(this.latitude(), this.longitude());
    this.map?.setView(at, this.zoom());
  }

  private build(): void {
    const at = L.latLng(this.latitude(), this.longitude());

    const map = L.map(this.canvas().nativeElement, {
      center: at,
      zoom: this.zoom(),
      attributionControl: true,
      // A map inside a scrolling form must not swallow the page scroll. Ctrl+wheel still zooms,
      // which is what every embedded map does and what a reader expects.
      scrollWheelZoom: false,
      zoomControl: true,
    });

    // OpenStreetMap's own tiles, which need no key. CARTO's dark basemap was the first choice
    // because the console is dark, but basemaps.cartocdn.com now answers an unkeyed request with a
    // 200 and an "API KEY REQUIRED" watermark baked into the image — a working request and a broken
    // map, which is the worst way for this to fail. The dark look is done in CSS instead, over these
    // tiles (see `.kh-map .leaflet-tile-pane`).
    //
    // OSM's tile policy covers a console at this scale and requires the attribution below. Moving to
    // a keyed provider or self-hosted tiles before real traffic is in docs/pre-launch-checklist.md.
    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
      attribution:
        '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
      maxZoom: 19,
    }).addTo(map);

    // A div icon rather than Leaflet's default PNG: the bundler rewrites the image paths that the
    // default icon looks for, which is the classic "marker is a broken image" bug, and this way the
    // pin is drawn from the same tokens as the rest of the console.
    const marker = L.marker(at, {
      draggable: this.editable(),
      keyboard: this.editable(),
      title: this.label(),
      alt: this.label(),
      icon: L.divIcon({
        className: 'kh-map-pin',
        html: '<span class="kh-map-pin-dot"></span><span class="kh-map-pin-ring"></span>',
        iconSize: [22, 22],
        iconAnchor: [11, 11],
      }),
    }).addTo(map);

    marker.on('dragend', () => this.publish(marker.getLatLng()));
    map.on('click', (event: L.LeafletMouseEvent) => {
      if (!this.editable()) return;
      marker.setLatLng(event.latlng);
      this.publish(event.latlng);
    });

    this.map = map;
    this.marker = marker;
    this.drawRadius(at, this.radiusKm());
    this.ready.set(true);

    // Leaflet measures its container once and caches that size, so a map built while its section is
    // still laying out — or one whose column changes width later — renders tiles into a strip and
    // leaves the rest blank. A one-shot timeout only covered the first case.
    // The host, not the canvas: the canvas is what Leaflet resizes, and observing it would be
    // watching this component's own output.
    this.resize = new ResizeObserver(() => map.invalidateSize());
    this.resize.observe(this.host.nativeElement);
  }

  private drawRadius(at: L.LatLng, radiusKm: number | null): void {
    if (!this.map) return;
    if (radiusKm === null || !Number.isFinite(radiusKm) || radiusKm <= 0) {
      this.ring?.remove();
      this.ring = null;
      return;
    }
    const metres = radiusKm * 1000;
    if (this.ring) {
      this.ring.setLatLng(at).setRadius(metres);
      return;
    }
    this.ring = L.circle(at, {
      radius: metres,
      className: 'kh-map-radius',
      interactive: false,
    }).addTo(this.map);
  }

  /** Rounded to six decimals — about 11cm, past which the extra digits are noise. */
  private publish(at: L.LatLng): void {
    const point = {
      latitude: Number(at.lat.toFixed(6)),
      longitude: Number(at.lng.toFixed(6)),
    };
    this.selfMove = true;
    this.moved.emit(point);
    // Cleared after the caller's signal write has settled, so the effect above sees the new value
    // as already applied rather than snapping the pin back to where it was.
    queueMicrotask(() => (this.selfMove = false));
  }
}
