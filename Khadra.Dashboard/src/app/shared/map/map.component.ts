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
import { IconComponent } from '../icon/icon.component';
import { I18nService } from '../../core/i18n/i18n.service';

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
  imports: [IconComponent],
})
export class MapComponent implements OnDestroy {
  protected readonly t = inject(I18nService).t;
  private readonly host = inject(ElementRef<HTMLElement>);

  readonly latitude = input.required<number>();
  readonly longitude = input.required<number>();

  /** Whether the reader can move the pin. Read-only by default: most maps here only show a place. */
  readonly editable = input(false);

  /**
   * Whether a place has actually been CHOSEN yet.
   *
   * False draws the map with no pin at all, centred on wherever latitude/longitude point — a camera
   * position, not an answer. It exists because the alternative was worse: the applicant's map was
   * rendered only once valid coordinates existed, so the one tool for choosing a location appeared
   * only after the location had been chosen. With no cities to seed a centre from, the sole way
   * through the form was typing raw decimal degrees by hand.
   *
   * A pin drawn at the fallback centre would be worse still, because it would look like an answer
   * nobody gave, and the applicant could submit it without noticing.
   */
  readonly hasPin = input(true);
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
  /** Set when the tile server refuses or fails; the map still shows the pin and the coordinates. */
  protected readonly tilesFailed = signal(false);

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
      // The pin joins the map the moment a place is chosen, and leaves if the choice is cleared.
      this.showPin(this.hasPin());
      this.drawRadius(this.hasPin() ? at : null, radius);
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
    const tiles = L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
      attribution:
        '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
      maxZoom: 19,
    }).addTo(map);

    // A map that cannot fetch its tiles must say so. OSM's servers throttle by client, and a
    // throttled map is a half-drawn picture that looks like a bug in this console rather than what
    // it is; the coordinates and the pin underneath are still true and still readable.
    tiles.on('tileerror', () => this.tilesFailed.set(true));
    tiles.on('tileload', () => this.tilesFailed.set(false));

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
    });

    marker.on('dragend', () => this.publish(marker.getLatLng()));
    map.on('click', (event: L.LeafletMouseEvent) => {
      if (!this.editable()) return;
      marker.setLatLng(event.latlng);
      // The first click on an unpinned map is what CHOOSES the place, so the pin has to appear as
      // well as move. Without this the applicant clicks, the coordinates fill in beside the map,
      // and the map itself still shows nothing.
      this.showPin(true);
      this.publish(event.latlng);
    });

    this.map = map;
    this.marker = marker;
    this.showPin(this.hasPin());
    this.drawRadius(this.hasPin() ? at : null, this.radiusKm());
    this.ready.set(true);

    // Leaflet measures its container once and caches that size. Two different things can go wrong,
    // and the first version only handled the second:
    //
    //  1. The map is BUILT before the browser has laid the container out, so Leaflet reads 0×0 and
    //     draws one tile where it thinks the middle is. The box has a fixed height in CSS, so it is
    //     already the right size on the first layout and NEVER changes — meaning a ResizeObserver
    //     fires once with the same 0×0 and then stays silent forever. The map is wrong for good.
    //     That is the strip-of-tiles-in-an-empty-box this shipped with.
    //  2. The container changes width later — a window resize, a column reflowing.
    //
    // `settle()` covers the first, the observer covers the second.
    this.settle(map);

    // The host, not the canvas: the canvas is what Leaflet resizes, and observing it would be
    // watching this component's own output.
    this.resize = new ResizeObserver(() => map.invalidateSize());
    this.resize.observe(this.host.nativeElement);
  }

  /**
   * Re-measures until the container has a real size and Leaflet agrees with it.
   *
   * Bounded by frames rather than looping forever: a map inside a collapsed section legitimately has
   * no size, and this must not spin behind it.
   */
  private settle(map: L.Map, framesLeft = 20): void {
    if (this.map !== map) return; // Destroyed, or rebuilt, while we were waiting.

    const box = this.canvas().nativeElement;
    const width = box.clientWidth;
    const height = box.clientHeight;
    const size = map.getSize();

    if (width > 0 && height > 0) {
      if (size.x !== width || size.y !== height) {
        map.invalidateSize({ animate: false });
        // Re-centre: invalidateSize keeps the top-left corner, which leaves the pin off-centre when
        // the correction is large — and the whole point of this map is where the pin is.
        map.setView(L.latLng(this.latitude(), this.longitude()), this.zoom(), { animate: false });
      } else {
        return; // Leaflet and the DOM agree; nothing left to fix.
      }
    }

    if (framesLeft > 0) {
      requestAnimationFrame(() => this.settle(map, framesLeft - 1));
    }
  }

  /**
   * Adds or removes the pin without destroying it, so its drag handlers survive.
   *
   * Leaflet has no "hidden marker": a marker is either on the map or it is not. Toggling opacity
   * instead would leave an invisible thing that still answers clicks and drags.
   */
  private showPin(visible: boolean): void {
    const map = this.map;
    const marker = this.marker;
    if (!map || !marker) return;
    if (visible && !map.hasLayer(marker)) marker.addTo(map);
    else if (!visible && map.hasLayer(marker)) marker.remove();
  }

  private drawRadius(at: L.LatLng | null, radiusKm: number | null): void {
    if (!this.map) return;
    // No pin means no place to draw a radius around, whatever the radius says.
    if (at === null || radiusKm === null || !Number.isFinite(radiusKm) || radiusKm <= 0) {
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
