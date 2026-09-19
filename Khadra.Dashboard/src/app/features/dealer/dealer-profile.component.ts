import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { DayScheduleInput } from '../../core/models/dealer-console.api';
import { DealerProfile } from '../../core/models/dealers.api';
import { DealerConsoleService } from '../../core/services/dealer-console.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { LookupsService } from '../../core/services/lookups.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { MapComponent } from '../../shared/map/map.component';
import { I18nService } from '../../core/i18n/i18n.service';
import { FormatService } from '../../core/i18n/format.service';
import { Language } from '../../core/i18n/language';
import {
  ProblemSnapshot,
  fieldMessage,
  serverSentence,
  snapshotProblem,
} from '../../core/i18n/problem';
import {
  ProfileForm,
  addressIncomplete,
  cityChoices,
  locationChanged,
  profileRequest,
} from './dealer-profile.presenter';

/** The API's own day names, sent back as `day` on save. Shown only through `FormatService.weekday`. */
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
 * What customers see: name, logo and cover, where the dealership is, when it is open, whether it
 * delivers. The owner edits all of it here; staff read it. One thing the design shows that this page
 * will not fake: a star rating, because reviews are not live, so the preview says "No reviews yet".
 *
 * What an office WRITES for its customers is not here. It moved to the customer page, where it sits
 * with the rest of the office's own prose and a switch for showing each part — and, more to the
 * point, where it has ONE writer: `PUT me/profile` no longer accepts `description`, so a box on this
 * form would have saved nothing while toasting that it had.
 *
 * The location is a real map now (`kh-map`), and the pin is draggable. The coordinate fields stay,
 * because they are the form's state and someone pasting a pair from their phone should not have to
 * hunt for the spot — the two are kept in step in both directions.
 *
 * The business name locks once the dealership is approved, because that is the name the licence
 * was verified against. The API enforces it; the field explains it.
 */
@Component({
  selector: 'kh-dealer-profile',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dealer-profile.component.html',
  imports: [IconComponent, MapComponent, RouterLink],
})
export class DealerProfileComponent {
  private readonly i18n = inject(I18nService);
  protected readonly t = inject(I18nService).t;
  protected readonly statusLabel = inject(I18nService).statusLabel;
  private readonly service = inject(DealerConsoleService);
  protected readonly fmt = inject(FormatService);
  private readonly ui = inject(ConsoleUiService);

  protected readonly resource = this.service.me;
  /** Guarded: `value()` throws in the error state, so nothing reads the resource directly. */
  private readonly data = loaded(this.resource);
  protected readonly dealer = computed(() => this.data() ?? null);
  protected readonly days = DAYS;

  protected readonly businessName = signal('');
  protected readonly latitude = signal('');
  protected readonly longitude = signal('');
  protected readonly hours = signal<readonly DayScheduleInput[]>([]);
  /** `''` while no city is chosen. Seeded from the office's own, and sent on every save. */
  protected readonly cityId = signal('');
  protected readonly area = signal('');
  protected readonly street = signal('');
  protected readonly busy = signal(false);

  private readonly lookups = inject(LookupsService);
  /**
   * The offered cities, or null until they have loaded — deliberately not `?? []`. An empty list
   * would read as "no city is offered", mark this office's city as retired, and leave the select
   * nothing to hold it with. See `cityChoices`.
   */
  private readonly cities = loaded(this.lookups.cities);

  protected readonly cityOptions = computed(() =>
    cityChoices(
      // `undefined` (a resource with no value yet) is folded into null — "not loaded" — and never into
      // `[]`. A loaded empty list stays empty, and means what it says.
      this.cities() ?? null,
      this.dealer()?.cityId ?? null,
      this.cityId(),
      this.i18n.lang(),
      this.t,
    ),
  );

  /** The office's own city is chosen, and the platform no longer offers it. */
  protected readonly keepingRetiredCity = computed(() =>
    this.cityOptions().options.some((option) => option.retired && option.selected),
  );

  /** Everything the form holds, as one value: what `save()` sends and `dirty` compares. */
  private readonly form = computed<ProfileForm>(() => ({
    businessName: this.businessName(),
    latitude: this.latitude(),
    longitude: this.longitude(),
    hours: this.hours(),
    cityId: this.cityId(),
    area: this.area(),
    street: this.street(),
  }));

  protected readonly addressIncomplete = computed(() => addressIncomplete(this.form()));
  protected readonly uploading = signal<'logo' | 'cover' | null>(null);
  /** What the last failed save or upload said, as facts; `problemText` chooses the words. */
  protected readonly problem = signal<Failure | null>(null);
  /** The last failed save, for its per-field messages. An upload leaves them standing. */
  private readonly fieldProblem = signal<ProblemSnapshot | null>(null);

  protected readonly problemText = computed(() => {
    const failure = this.problem();
    return failure ? describe(failure, this.t, this.i18n.lang()) : null;
  });

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
      Number(this.latitude()) !== d.latitude ||
      Number(this.longitude()) !== d.longitude ||
      JSON.stringify(this.hours()) !== JSON.stringify(fromProfile(d)) ||
      locationChanged(d, this.form())
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

  // The fields are strings because they are bound to text inputs; the map needs numbers. Only read
  // where `coordsInvalid()` is false, so these are always a pair the map can place.
  protected readonly latNumber = computed(() => Number(this.latitude()));
  protected readonly lngNumber = computed(() => Number(this.longitude()));

  /** Drawn to scale around the pin, so "30 km" is something the owner can see rather than trust. */
  protected readonly deliveryRadiusKm = computed(() => {
    const delivery = this.dealer()?.delivery;
    return delivery?.isEnabled ? delivery.radiusKm : null;
  });

  /** The pin was dragged. The fields are the form's state, so the move is written back to them. */
  protected moveTo(point: { latitude: number; longitude: number }): void {
    this.latitude.set(String(point.latitude));
    this.longitude.set(String(point.longitude));
  }

  protected readonly hoursSummary = computed(() => {
    const open = this.hours().filter((h) => !h.isClosed);
    if (open.length === 0) return this.t('dealerProfile.closedAllWeek');
    const first = open[0];
    const same = open.every((h) => h.opensAt === first.opensAt && h.closesAt === first.closesAt);
    if (!same) return this.t('dealerProfile.openDaysAWeekHoursVary', { count: open.length });
    const hours = { opens: first.opensAt ?? '', closes: first.closesAt ?? '' };
    return open.length === 7
      ? this.t('dealerProfile.everyDayHours', hours)
      : this.t('dealerProfile.daysAWeekHours', { count: open.length, ...hours });
  });

  protected readonly failure = computed(() => {
    const error = this.resource.error() as { status?: number } | undefined;
    if (!error) return null;
    return this.t('dealerProfile.yourDealerPageCould');
  });

  protected reset(d: DealerProfile): void {
    this.businessName.set(d.businessName);
    this.latitude.set(String(d.latitude));
    this.longitude.set(String(d.longitude));
    this.hours.set(fromProfile(d));
    this.cityId.set(d.cityId ?? '');
    this.area.set(d.address?.area ?? '');
    this.street.set(d.address?.street ?? '');
    this.problem.set(null);
    this.fieldProblem.set(null);
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
    if (!d || this.busy() || this.coordsInvalid() || this.addressIncomplete()) return;
    this.busy.set(true);
    this.problem.set(null);
    this.fieldProblem.set(null);
    try {
      await this.service.updateProfile(profileRequest(this.form()));
      this.service.refreshMe();
      this.ui.showToast(this.t('dealerProfile.dealerPageSaved'), this.t('dealerProfile.customersSeeTheNew'));
    } catch (error) {
      const snapshot = snapshotProblem(error);
      this.fieldProblem.set(snapshot);
      this.problem.set({ during: 'save', snapshot });
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
        kind === 'logo' ? this.t('dealerProfile.logoUpdated') : this.t('dealerProfile.coverUpdated'),
        this.t('dealerProfile.itIsLiveOn'),
      );
    } catch (error) {
      this.problem.set({ during: 'upload', snapshot: snapshotProblem(error) });
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
    return fieldMessage(this.fieldProblem(), name, this.i18n.lang(), this.t);
  }
}

/** A failed request, and which of the two things that can fail it was. */
interface Failure {
  readonly during: 'save' | 'upload';
  readonly snapshot: ProblemSnapshot;
}

/** Words a failure in the reader's language, at render time. The code mappings are the screen's own. */
function describe(failure: Failure, t: I18nService['t'], language: Language): string {
  const p = failure.snapshot;
  if (failure.during === 'upload') {
    return p.code === 'dealer.invalid_branding_type'
      ? t('vehicleWizard.useAJpegPng')
      : (serverSentence(p, language, t) ?? t('dealerProfile.theUploadDidNot'));
  }
  return p.code === 'dealer.business_name_locked'
    ? t('dealerProfile.theBusinessNameIs')
    : (serverSentence(p, language, t) ?? t('dealerDelivery.serviceDidNotRespond'));
}

function fromProfile(d: DealerProfile): readonly DayScheduleInput[] {
  return DAYS.map((day) => {
    const stored = d.operatingHours.find((h) => h.day === day);
    return stored
      ? { day, isClosed: stored.isClosed, opensAt: stored.opensAt, closesAt: stored.closesAt }
      : { day, isClosed: true, opensAt: null, closesAt: null };
  });
}
