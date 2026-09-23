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
import type * as Leaflet from 'leaflet';
import { I18nService } from '../../core/i18n/i18n.service';

export interface MapPoint {
  readonly latitude: number;
  readonly longitude: number;
}

/**
 * A real map of a real place: an office's pin, optionally its delivery radius, and — when `pickable` —
 * a point the customer chooses by tapping.
 *
 * Leaflet is loaded only in the browser and only when a map is shown: it touches `window` on import,
 * which the server renderer does not have, and most pages never need it. Raster tiles, because the
 * page's CSP allows images from https but fetches only from its own origin. The pin is a drawn circle,
 * not Leaflet's image marker, so there is no icon file for a bundler to lose.
 */
@Component({
  selector: 'kh-map',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './map.component.html',
})
export class MapComponent implements OnDestroy {
  protected readonly i18n = inject(I18nService);

  readonly latitude = input.required<number>();
  readonly longitude = input.required<number>();
  readonly radiusKm = input<number | null>(null);
  readonly pickable = input(false);
  readonly picked = input<MapPoint | null>(null);
  readonly label = input('');
  readonly pick = output<MapPoint>();

  private readonly canvas = viewChild.required<ElementRef<HTMLDivElement>>('canvas');
  protected readonly ready = signal(false);

  private leaflet: typeof Leaflet | null = null;
  private map: Leaflet.Map | null = null;
  private office: Leaflet.CircleMarker | null = null;
  private ring: Leaflet.Circle | null = null;
  private choice: Leaflet.CircleMarker | null = null;

  constructor() {
    afterNextRender(async () => {
      this.leaflet = await import('leaflet');
      this.build();
    });

    effect(() => {
      const point = { latitude: this.latitude(), longitude: this.longitude() };
      const radius = this.radiusKm();
      if (!this.ready()) return;
      this.office?.setLatLng([point.latitude, point.longitude]);
      this.ring?.remove();
      this.ring = null;
      if (radius && this.leaflet && this.map) {
        this.ring = this.leaflet
          .circle([point.latitude, point.longitude], { radius: radius * 1000, color: '#15803d', weight: 1, fillOpacity: 0.06 })
          .addTo(this.map);
      }
    });

    effect(() => {
      const chosen = this.picked();
      if (!this.ready() || !this.leaflet || !this.map) return;
      if (!chosen) {
        this.choice?.remove();
        this.choice = null;
        return;
      }
      if (this.choice) this.choice.setLatLng([chosen.latitude, chosen.longitude]);
      else
        this.choice = this.leaflet
          .circleMarker([chosen.latitude, chosen.longitude], { radius: 9, color: '#111827', weight: 3, fillColor: '#ffffff', fillOpacity: 1 })
          .addTo(this.map);
    });
  }

  private build(): void {
    const L = this.leaflet;
    if (!L) return;
    const center: [number, number] = [this.latitude(), this.longitude()];
    this.map = L.map(this.canvas().nativeElement, { scrollWheelZoom: false, attributionControl: true }).setView(center, 13);
    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 19,
      attribution: '&copy; OpenStreetMap contributors',
    }).addTo(this.map);
    this.office = L.circleMarker(center, { radius: 10, color: '#ffffff', weight: 3, fillColor: '#15803d', fillOpacity: 1 })
      .addTo(this.map)
      .bindTooltip(this.label() || this.i18n.t('office.location'));
    if (this.pickable()) {
      this.map.on('click', (event: Leaflet.LeafletMouseEvent) =>
        this.pick.emit({ latitude: event.latlng.lat, longitude: event.latlng.lng }),
      );
    }
    this.ready.set(true);
    const radius = this.radiusKm();
    if (radius) {
      this.ring = L.circle(center, { radius: radius * 1000, color: '#15803d', weight: 1, fillOpacity: 0.06 }).addTo(this.map);
      this.map.fitBounds(this.ring.getBounds(), { padding: [12, 12] });
    }
  }

  ngOnDestroy(): void {
    this.map?.remove();
    this.map = null;
  }
}
