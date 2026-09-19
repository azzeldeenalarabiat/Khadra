import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { KeyValue, Tone } from '../../core/models/console.models';
import { CustomerDocumentSummary } from '../../core/models/customers.api';
import { AdminCustomersService } from '../../core/services/admin-customers.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { I18nService } from '../../core/i18n/i18n.service';
import { FormatService } from '../../core/i18n/format.service';
import { TranslationKey } from '../../core/i18n/en';
import { spellEnumName } from '../../core/i18n/status-key';

/** An account row. `ltr` marks a Latin run -- an email, a "+962" number -- the template isolates. */
interface AccountRow extends KeyValue {
  readonly ltr?: boolean;
}

/** What each `CustomerDocumentType` is called; the same words the dealer's renter panel uses. */
const DOCUMENT_TYPE_LABELS: Readonly<Record<string, TranslationKey>> = {
  DrivingLicenceFront: 'renterDocs.type.drivingLicenceFront',
  DrivingLicenceBack: 'renterDocs.type.drivingLicenceBack',
  NationalId: 'renterDocs.type.nationalId',
  Passport: 'renterDocs.type.passport',
};

/** The formats an upload is stored as, worded. Anything else is shown as the MIME type it is. */
const FORMAT_LABELS: Readonly<Record<string, TranslationKey>> = {
  'application/pdf': 'docFormat.pdf',
  'image/jpeg': 'docFormat.jpeg',
  'image/png': 'docFormat.png',
  'image/webp': 'docFormat.webp',
};

/**
 * One customer: their account, what they have on file, and their history with the platform.
 *
 * The documents are DESCRIBED and not linked. Spec 7 keeps identity papers private, and the domain
 * scopes viewing to the customer themselves and to a dealer with an active booking request — an
 * administrator is not named there, and the review mechanism spec 5.1 anticipates has not been
 * decided. So the screen says what is on file, in what state, and stops: minting a signed URL for a
 * passport is a decision for the owner, not a convenience to add quietly.
 */
@Component({
  selector: 'kh-customer-profile',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './customer-profile.component.html',
  imports: [RouterLink, IconComponent],
})
export class CustomerProfileComponent {
  protected readonly t = inject(I18nService).t;
  private readonly formats = inject(FormatService);
  private readonly service = inject(AdminCustomersService);
  private readonly ui = inject(ConsoleUiService);
  private readonly route = inject(ActivatedRoute);

  private readonly customerId = toSignal(
    this.route.paramMap.pipe(map((params) => params.get('customerId'))),
    { initialValue: this.route.snapshot.paramMap.get('customerId') },
  );

  constructor() {
    effect(() => this.service.viewing.set(this.customerId()));
  }

  protected readonly resource = this.service.customer;
  protected readonly customer = loaded(this.resource);

  protected readonly failure = computed(() => {
    const error = this.resource.error() as { status?: number } | undefined;
    if (!error) return null;
    if (error.status === 404) return this.t('customerProfile.thatCustomerWasNot');
    if (error.status === 403) return this.t('customerProfile.customerRecordsAreFor');
    return this.t('customerProfile.theCustomerCouldNot');
  });

  protected readonly tone = computed<Tone>(() => {
    const customer = this.customer();
    if (!customer) return 'dim';
    if (customer.status === 'Suspended') return 'bad';
    return customer.isEmailVerified ? 'ok' : 'warn';
  });

  /**
   * The ACCOUNT's standing, which is not the same question as a status enum's name.
   *
   * Renamed from `statusLabel` when the shared helper arrived: this screen labels two different
   * things -- the account, and each uploaded document -- and one name for both was a collision
   * waiting to be read as a duplicate.
   */
  protected readonly accountStatusLabel = computed(() => {
    const customer = this.customer();
    if (!customer) return '';
    if (customer.status === 'Suspended') return this.t('status.suspended');
    return customer.isEmailVerified ? this.t('status.verified') : this.t('customerProfile.emailUnverified');
  });

  /** A document's status, straight from the server's enum. */
  protected readonly statusLabel = inject(I18nService).statusLabel;

  protected readonly accountRows = computed<readonly AccountRow[]>(() => {
    const customer = this.customer();
    if (!customer) return [];
    const rows: AccountRow[] = [
      { k: this.t('dealerSettings.email'), v: customer.email, ltr: true },
      { k: this.t('customerProfile.phone'), v: customer.phone, ltr: true },
      {
        k: this.t('customerProfile.emailVerified'),
        v: customer.isEmailVerified ? this.when(customer.emailVerifiedAt) : this.t('common.no'),
      },
      // Printed as the server sends it (YYYY-MM-DD): a calendar date, not an instant, and the
      // formatter has no calendar-date form that keeps the year.
      {
        k: this.t('customerProfile.dateOfBirth'),
        v: customer.dateOfBirth ?? this.t('customerProfile.notGiven'),
        ltr: customer.dateOfBirth !== null,
      },
      {
        k: this.t('customerProfile.foreignNational'),
        v: customer.isForeignNational ? this.t('common.yes') : this.t('common.no'),
      },
      { k: this.t('customersList.joined'), v: this.when(customer.createdAt) },
      { k: this.t('adminUsers.lastSignedIn'), v: this.when(customer.lastLoginAt) },
    ];
    if (customer.passwordChangedAt) {
      rows.push({ k: this.t('customerProfile.passwordLastChanged'), v: this.when(customer.passwordChangedAt) });
    }
    return rows;
  });

  protected readonly bookingRows = computed<readonly KeyValue[]>(() => {
    const totals = this.customer()?.bookings;
    if (!totals) return [];
    return [
      { k: this.t('bookingsList.total'), v: this.formats.number(totals.total) },
      { k: this.t('customerProfile.liveNow'), v: this.formats.number(totals.live) },
      { k: this.t('status.completed'), v: this.formats.number(totals.completed) },
      { k: this.t('status.cancelled'), v: this.formats.number(totals.cancelled) },
      { k: this.t('dealerBooking.noShows'), v: this.formats.number(totals.noShow) },
    ];
  });

  protected readonly canSuspend = computed(() => this.customer()?.status === 'Active');
  protected readonly canReactivate = computed(() => this.customer()?.status === 'Suspended');

  protected suspend(): void {
    const customer = this.customer();
    if (!customer) return;
    this.ui.openAction(
      {
        icon: 'prohibit',
        tone: 'bad',
        danger: true,
        title: this.t('customerProfile.suspendNameQuestion', { name: customer.fullName }),
        body: this.t('customerProfile.theyAreSignedOut'),
        // Pre-launch item 18: the audit table is append-only and can never be erased, so an admin
        // must not type identity into a field that outlives every request to remove it.
        note: this.t('customerProfile.theReasonIsRecorded'),
        fields: [
          {
            // The key the typed value is stored under, read back as values['reason'] below: a
            // machine name, never a translation, or an Arabic screen sends an empty reason.
            name: 'reason',
            label: this.t('dealerDecide.reject.reasonLabel'),
            type: 'text',
            placeholder: this.t('customerProfile.whyIsThisAccount'),
          },
        ],
        confirm: this.t('customerProfile.suspendAccount'),
        result: { title: this.t('customerProfile.accountSuspended'), body: '', tone: 'bad' },
      },
      async (values) => {
        await this.service.suspend(customer.userId, values['reason'] ?? '');
        this.service.refresh();
      },
      { title: this.t('customerProfile.accountSuspended'), body: this.t('customerProfile.theirSessionsEndedImmediately') },
    );
  }

  protected reactivate(): void {
    const customer = this.customer();
    if (!customer) return;
    this.ui.openAction(
      {
        icon: 'check-circle',
        tone: 'ok',
        title: this.t('customerProfile.reactivateNameQuestion', { name: customer.fullName }),
        body: this.t('customerProfile.theyCanSignIn'),
        confirm: this.t('customerProfile.reactivateAccount'),
        result: { title: this.t('customerProfile.accountReactivated'), body: '', tone: 'ok' },
      },
      async () => {
        await this.service.reactivate(customer.userId);
        this.service.refresh();
      },
      { title: this.t('customerProfile.accountReactivated'), body: '' },
    );
  }

  protected reload(): void {
    this.resource.reload();
  }

  protected documentTone(document: CustomerDocumentSummary): Tone {
    if (document.status === 'Verified') return 'ok';
    if (document.status === 'Rejected') return 'bad';
    return 'warn';
  }

  /** A document type in the reader's language; one this build does not know is spelled out. */
  protected documentLabel(document: CustomerDocumentSummary): string {
    const key = DOCUMENT_TYPE_LABELS[document.type];
    return key ? this.t(key) : spellEnumName(document.type);
  }

  /**
   * The file's real format and size, and when it arrived: three independent facts, each worded on
   * its own. Never a filename, which the server does not send.
   */
  protected documentDetail(document: CustomerDocumentSummary): string {
    const kilobytes = Math.max(1, Math.round(document.sizeBytes / 1024));
    const formatKey = FORMAT_LABELS[document.contentType];
    return [
      // An unrecognised type is shown as it arrived rather than guessed at.
      formatKey ? this.t(formatKey) : document.contentType,
      this.t('customerProfile.sizeInKilobytes', { size: this.formats.number(kilobytes) }),
      this.t('customerProfile.uploadedOn', { date: this.when(document.uploadedAt) }),
    ].join(' · ');
  }

  protected initials(name: string): string {
    return name
      .split(' ')
      .slice(0, 2)
      .map((part) => part[0] ?? '')
      .join('');
  }

  protected when(iso: string | null): string {
    return iso ? this.formats.dateTime(iso) : this.t('common.never');
  }
}
