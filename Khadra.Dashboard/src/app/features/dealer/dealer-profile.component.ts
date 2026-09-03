import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { DayScheduleInput } from '../../core/models/dealer-console.api';
import { DealerProfile } from '../../core/models/dealers.api';
import { DealerConsoleService } from '../../core/services/dealer-console.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { IconComponent } from '../../shared/icon/icon.component';

const DAYS = [
  'Sunday',
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
  'Saturday',
] as const;

/**
 * The dealer page (spec 4.1, design `isProfile` and `isPublicPreview`).
 *
 * What customers see: name, description, logo and cover, where the dealership is, when it is open,
 * whether it delivers. The owner edits all of it here; staff read it. Two things the design shows
 * that this page will not fake: a star rating (reviews are not live, so the preview says "No reviews
 * yet") and an interactive map (the pin is the stored coordinates, entered as numbers until a map
 * provider is chosen).
 *
 * The business name locks once the dealership is approved, because that is the name the licence
 * was verified against. The API enforces it; the field explains it.
 */
@Component({
  selector: 'kh-dealer-profile',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dealer-profile.component.html',
  imports: [IconComponent],
})
export class DealerProfileComponent {
  private readonly service = inject(DealerConsoleService);
  private readonly ui = inject(ConsoleUiService);

  protected readonly resource = this.service.me;
  protected readonly dealer = computed(() => this.resource.value() ?? null);
  protected readonly days = DAYS;

  protected readonly businessName = signal('');
  protected readonly description = signal('');
  protected readonly latitude = signal('');
  protected readonly longitude = signal('');
  protected readonly hours = signal<readonly DayScheduleInput[]>([]);
  protected readonly busy = signal(false);
  protected readonly uploading = signal<'logo' | 'cover' | null>(null);
  protected readonly problem = signal<string | null>(null);
  protected readonly fieldErrors = signal<Readonly<Record<string, readonly string[]>>>({});

  constructor() {
    effect(() => {
      const d = this.dealer();
      if (d) this.reset(d);
    });
  }

  protected readonly isOwner = computed(() => !!this.dealer()?.isOwner);
  protected readonly nameLocked = computed(() => this.dealer()?.verificationStatus === 'Approved');

  protected readonly dirty = computed(() => {
    const d = this.dealer();
    if (!d) return false;
    return (
      this.businessName() !== d.businessName ||
      this.description() !== (d.description ?? '') ||
      Number(this.latitude()) !== d.latitude ||
      Number(this.longitude()) !== d.longitude ||
      JSON.stringify(this.hours()) !== JSON.stringify(fromProfile(d))
    );
  });

  protected readonly coordsInvalid = computed(() => {
    const lat = Number(this.latitude());
    const lng = Number(this.longitude());
    return (
      !(lat >= -90 && lat <= 90) ||
      !(lng >= -180 && lng <= 180) ||
      this.latitude() === '' ||
      this.longitude() === ''
    );
  });

  protected readonly hoursSummary = computed(() => {
    const open = this.hours().filter((h) => !h.isClosed);
    if (open.length === 0) return 'Closed all week';
    const first = open[0];
    const same = open.every((h) => h.opensAt === first.opensAt && h.closesAt === first.closesAt);
    return same
      ? `${open.length === 7 ? 'Every day' : `${open.length} days a week`} · ${first.opensAt}–${first.closesAt}`
      : `Open ${open.length} days a week · hours vary`;
  });

  protected readonly failure = computed(() => {
    const error = this.resource.error() as { status?: number } | undefined;
    if (!error) return null;
    return 'Your dealer page could not be loaded. Nothing has been changed.';
  });

  protected reset(d: DealerProfile): void {
    this.businessName.set(d.businessName);
    this.description.set(d.description ?? '');
    this.latitude.set(String(d.latitude));
    this.longitude.set(String(d.longitude));
    this.hours.set(fromProfile(d));
    this.problem.set(null);
    this.fieldErrors.set({});
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement).value;
  }

  protected setDay(day: string, patch: Partial<DayScheduleInput>): void {
    this.hours.update((all) =>
      all.map((h) => {
        if (h.day !== day) return h;
        const next = { ...h, ...patch };
        return next.isClosed
          ? { ...next, opensAt: null, closesAt: null }
          : { ...next, opensAt: next.opensAt ?? '09:00', closesAt: next.closesAt ?? '18:00' };
      }),
    );
  }

  protected copyToAll(day: string): void {
    const source = this.hours().find((h) => h.day === day);
    if (!source) return;
    this.hours.update((all) =>
      all.map((h) => ({
        ...h,
        isClosed: source.isClosed,
        opensAt: source.opensAt,
        closesAt: source.closesAt,
      })),
    );
  }

  protected async save(): Promise<void> {
    const d = this.dealer();
    if (!d || this.busy() || this.coordsInvalid()) return;
    this.busy.set(true);
    this.problem.set(null);
    this.fieldErrors.set({});
    try {
      await this.service.updateProfile({
        businessName: this.businessName().trim(),
        description: this.description().trim() || null,
        latitude: Number(this.latitude()),
        longitude: Number(this.longitude()),
        operatingHours: this.hours(),
      });
      this.service.refreshMe();
      this.ui.showToast('Dealer page saved', 'Customers see the new details straight away.');
    } catch (error) {
      const p = error as {
        error?: { code?: string; title?: string; errors?: Record<string, string[]> };
      };
      if (p.error?.errors) this.fieldErrors.set(p.error.errors);
      this.problem.set(
        p.error?.code === 'dealer.business_name_locked'
          ? 'The business name is locked: it is the name your licence was verified against. Ask the platform if it has to change.'
          : (p.error?.title ?? 'The service did not respond. Nothing has been changed.'),
      );
    } finally {
      this.busy.set(false);
    }
  }

  protected async upload(kind: 'logo' | 'cover', event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file || this.uploading()) return;
    this.uploading.set(kind);
    this.problem.set(null);
    try {
      await this.service.uploadBranding(kind, file);
      this.service.refreshMe();
      this.ui.showToast(
        kind === 'logo' ? 'Logo updated' : 'Cover updated',
        'It is live on your public page.',
      );
    } catch (error) {
      const p = error as { error?: { code?: string; title?: string } };
      this.problem.set(
        p.error?.code === 'dealer.invalid_branding_type'
          ? 'Use a JPEG, PNG or WebP image.'
          : (p.error?.title ?? 'The upload did not go through. Nothing has been changed.'),
      );
    } finally {
      this.uploading.set(null);
      input.value = '';
    }
  }

  protected initials(name: string): string {
    return name
      .split(' ')
      .slice(0, 2)
      .map((part) => part[0] ?? '')
      .join('');
  }

  protected fieldError(name: string): string | null {
    const errors = this.fieldErrors();
    const key = Object.keys(errors).find((k) => k.toLowerCase() === name.toLowerCase());
    return key ? (errors[key][0] ?? null) : null;
  }
}

function fromProfile(d: DealerProfile): readonly DayScheduleInput[] {
  return DAYS.map((day) => {
    const stored = d.operatingHours.find((h) => h.day === day);
    return stored
      ? { day, isClosed: stored.isClosed, opensAt: stored.opensAt, closesAt: stored.closesAt }
      : { day, isClosed: true, opensAt: null, closesAt: null };
  });
}
