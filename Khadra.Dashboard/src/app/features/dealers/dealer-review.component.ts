import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { AdminDealersService } from '../../core/services/admin-dealers.service';
import { loaded } from '../../core/services/loaded';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { isAtRisk } from '../../core/services/sla';
import { DocumentTile, KeyValue, TimelineStep, Tone } from '../../core/models/console.models';
import { DealerReview } from '../../core/models/dealers.api';
import { DocTileComponent } from '../../shared/doc-tile/doc-tile.component';
import { IconComponent } from '../../shared/icon/icon.component';
import { TimelineComponent } from '../../shared/timeline/timeline.component';
import { MapComponent } from '../../shared/map/map.component';
import { LookupsService } from '../../core/services/lookups.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { FormatService } from '../../core/i18n/format.service';
import { TranslationKey } from '../../core/i18n/en';
import { spellEnumName } from '../../core/i18n/status-key';

/**
 * What each `DealerDocumentType` is called on this screen. The server sends the type's NAME
 * (`CommercialRegistration`), which is a machine value, not a label.
 */
const DOCUMENT_TYPE_LABELS: Readonly<Record<string, TranslationKey>> = {
  CommercialRegistration: 'dealerReview.documentCommercialRegistration',
  VehicleRegistration: 'dealerReview.documentVehicleRegistration',
  OwnerIdentity: 'dealerReview.documentOwnerIdentity',
};

/** The formats a signed link is served as, worded. Anything else is shown as the MIME type it is. */
const FORMAT_LABELS: Readonly<Record<string, TranslationKey>> = {
  'application/pdf': 'docFormat.pdf',
  'image/jpeg': 'docFormat.jpeg',
  'image/png': 'docFormat.png',
  'image/webp': 'docFormat.webp',
};

/**
 * Dealer application review: the Admin's licence check (spec 3.1).
 *
 * Three outcomes, not two — approve, reject with a reason, or send it back with a specific note so
 * the dealer fixes one thing rather than re-applying. Rejection, clarification and suspension all
 * require a written reason, which is why each goes through the confirmation dialog: the consequence
 * is stated, the reason is captured, and both end up in the audit trail.
 */
@Component({
  selector: 'kh-dealer-review',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dealer-review.component.html',
  imports: [RouterLink, IconComponent, DocTileComponent, TimelineComponent, MapComponent],
})
export class DealerReviewComponent {
  private readonly i18n = inject(I18nService);
  protected readonly t = this.i18n.t;
  private readonly formats = inject(FormatService);
  private readonly lookups = inject(LookupsService);
  private readonly service = inject(AdminDealersService);
  private readonly ui = inject(ConsoleUiService);
  private readonly route = inject(ActivatedRoute);
  private readonly now = signal(Date.now());

  protected readonly resource = this.service.review;

  // Resource.value() throws while a request has failed, so nothing reads it directly; failure()
  // below goes on reading error(), which does not throw.
  private readonly review = loaded(this.resource);

  // Angular reuses this component when only the route parameter changes, so reading the snapshot once
  // in the constructor would leave the screen showing the previous dealer. Nothing links one dealer
  // to another today, which is why it has never shown -- but it would be a silent wrong-record bug
  // the moment something does, and a review screen displaying the wrong application is the worst
  // possible place for one.
  private readonly dealerId = toSignal(
    this.route.paramMap.pipe(map((parameters) => parameters.get('dealerId'))),
    { initialValue: this.route.snapshot.paramMap.get('dealerId') },
  );

  constructor() {
    effect(() => this.service.reviewing.set(this.dealerId()));
    // Keep the countdown honest while the screen is open without re-fetching.
    const ticker = setInterval(() => this.now.set(Date.now()), 60_000);
    effect((onCleanup) => onCleanup(() => clearInterval(ticker)));
  }

  protected readonly dealer = computed(() => this.review()?.dealer ?? null);

  /**
   * The city's own NAME, resolved from the curated lookup the applicant chose from.
   *
   * Resolved here rather than carried on the DTO: the city belongs to PlatformSettings, and putting
   * its name inside a DTO built from the Dealers aggregate would reach across a context boundary to
   * denormalise a value that can be renamed. The lookup is already loaded for the filters.
   */
  protected readonly cityName = computed(() => {
    const cityId = this.dealer()?.cityId;
    if (!cityId) return null;

    const city = (loaded(this.lookups.cities)() ?? []).find((candidate) => candidate.id === cityId);
    if (!city) return null;

    const arabic = this.i18n.lang() === 'ar';
    return (arabic ? city.nameAr || city.nameEn : city.nameEn || city.nameAr).trim() || null;
  });

  protected readonly initials = computed(() => {
    const name = this.dealer()?.businessName ?? '';
    return name
      .split(' ')
      .slice(0, 2)
      .map((part) => part[0] ?? '')
      .join('');
  });

  protected readonly statusTone = computed<Tone>(() => {
    const dealer = this.dealer();
    if (!dealer) return 'dim';
    if (dealer.isSuspended) return 'bad';
    if (dealer.verificationStatus === 'Approved') return 'ok';
    if (dealer.verificationStatus === 'Rejected') return 'dim';
    return 'warn';
  });

  protected readonly statusLabel = computed(() => {
    const dealer = this.dealer();
    if (!dealer) return '';
    // A suspension sits on top of an approval rather than replacing it, so it is worded first.
    if (dealer.isSuspended) return this.i18n.statusLabel('Suspended');
    return this.i18n.statusLabel(dealer.verificationStatus);
  });

  /**
   * The line under the business name: its registration, how many of the required documents are on
   * file, and when it was submitted. Three independent facts, each a whole message; both counts are
   * the application's own lists, never an assumed three.
   */
  protected readonly headerMeta = computed(() => {
    const dealer = this.dealer();
    if (!dealer) return '';
    return [
      this.t('dealersList.commercialRegistration', {
        number: dealer.commercialRegistrationNumber,
      }),
      this.t('dealerReview.documentsOfCount', {
        have: dealer.submittedDocuments.length,
        count: dealer.requiredDocuments.length,
      }),
      this.t('dealerReview.submittedOn', { date: this.formats.dateTime(dealer.submittedAt) }),
    ].join(' · ');
  });

  /**
   * How long is left on the review promise, or how far past it this application already is.
   *
   * The length of that promise is measured from the application itself — `reviewDueAt` minus
   * `submittedAt` — not from the SLA the platform happens to be running today. `Dealer.Register`
   * freezes `reviewDueAt` at submission for exactly this reason: an application submitted under a
   * 48-hour promise is still owed 48 hours after the owner lowers the setting to 24. The screen used
   * to read "the 48-hour review SLA" from a literal, which was right only by coincidence and would
   * have started contradicting the countdown printed beside it.
   */
  protected readonly sla = computed(() => {
    const review = this.review();
    if (!review) return { figure: '—', note: '', tone: 'dim' as Tone };
    if (review.dealer.verificationStatus !== 'PendingReview') {
      return {
        figure: this.t('dealerReview.settled'),
        note: this.t('dealerReview.noDecisionOutstanding'),
        tone: 'ok' as Tone,
      };
    }

    const now = this.now();
    const started = Date.parse(review.dealer.submittedAt);
    const due = Date.parse(review.dealer.reviewDueAt);
    const promised = Math.round((due - started) / 3_600_000);

    // `isBreachingSla` is the server's answer, frozen when this was fetched; the ticker below moves
    // on without it. Both are consulted, or a tab left open through the deadline reads the growing
    // overrun as time REMAINING -- "1h left", in the colour of good news, an hour after the promise
    // was broken. Same rule the list uses, so the two screens cannot disagree about one application.
    const reading = this.formats.sla(review.dealer.reviewDueAt, review.isBreachingSla, now);

    return reading.passed
      ? {
          figure: reading.text,
          note: this.t('dealerReview.pastTheReviewSla', { count: promised }),
          tone: 'bad' as Tone,
        }
      : {
          figure: reading.text,
          note: this.t('dealerReview.untilTheReviewSla', { count: promised }),
          tone: isAtRisk(started, due, now) ? ('warn' as Tone) : ('ok' as Tone),
        };
  });

  protected readonly businessRows = computed<readonly KeyValue[]>(() => {
    const review = this.review();
    if (!review) return [];
    return [
      { k: this.t('dealerProfile.businessName'), v: review.dealer.businessName },
      {
        k: this.t('dealerProfile.commercialRegistration'),
        v: review.dealer.commercialRegistrationNumber,
      },
      {
        k: this.t('dealerProfile.location'),
        v: this.formats.coordinates(review.latitude, review.longitude),
      },
      { k: this.t('common.description'), v: review.description ?? '—' },
      {
        k: this.t('dealerReview.submitted'),
        v: this.formats.dateTime(review.dealer.submittedAt),
      },
      {
        k: this.t('dealersList.colReviewDue'),
        v: this.formats.dateTime(review.dealer.reviewDueAt),
      },
      // Active staff only — the same number the dealership sees on its own profile. The review
      // response used to carry a second count that included deactivated rows, so one dealership
      // had two staff figures depending on which screen an admin was looking at.
      {
        k: this.t('dealerEmployees.employees'),
        v: this.formats.number(review.dealer.employeeCount),
      },
      ...(review.dealer.reviewNote
        ? [{ k: this.t('dealerReview.lastReviewNote'), v: review.dealer.reviewNote }]
        : []),
    ];
  });

  protected readonly documents = computed<readonly DocumentTile[]>(() => {
    const review = this.review();
    if (!review) return [];
    return review.documents.map((document) => ({
      label: this.documentLabel(document.type),
      status: this.t('dealerReview.provided'),
      tone: 'ok' as Tone,
      // The format the server will actually serve this file as -- not a filename, and certainly not
      // the signed URL, which is a credential. This tile read `${type}.jpg` for every document until
      // it turned out the registrations on file are PDFs: it was telling an Admin doing a licence
      // check that they were about to open a photograph. A real upload is keyed by a generated guid,
      // so there is no filename worth showing either; the format is the part that is true and useful.
      file: this.formatLabel(document.contentType),
      meta: this.t('dealerReview.linkExpiresAt', { time: this.formats.time(document.expiresAt) }),
      href: document.url,
    }));
  });

  /** The document types still missing, worded. */
  protected readonly missing = computed(() =>
    (this.dealer()?.missingDocuments ?? []).map((type) => this.documentLabel(type)),
  );

  /**
   * The banner under the documents, as one sentence with the list inside it. No count: the header
   * already reads "n of m documents", and "all three" was a number this screen had no way to know.
   */
  protected readonly missingSentence = computed(() =>
    this.t('dealerReview.missingDocumentsRequired', {
      documents: this.missing().join(this.t('common.listSeparator')),
    }),
  );

  /** A document type's name in the reader's language; one this build does not know is spelled out. */
  private documentLabel(type: string): string {
    const key = DOCUMENT_TYPE_LABELS[type];
    return key ? this.t(key) : spellEnumName(type);
  }

  /**
   * "application/pdf" -> "PDF". What the reviewer is about to open, in a word.
   *
   * An unrecognised type is shown as it arrived rather than guessed at: the Admin can still read it,
   * and the tile does not claim a format nobody vouched for.
   */
  private formatLabel(contentType: string): string {
    const key = FORMAT_LABELS[contentType];
    return key ? this.t(key) : contentType;
  }

  /**
   * When these links stop working, as an instant rather than a duration.
   *
   * The earliest of them, because that is when the section stops being usable. Once it is past, the
   * note says so: the 60-second ticker re-evaluates this, so an admin who left the tab open is told
   * to reload rather than clicking three buttons that have quietly become 404s.
   *
   * Null when the application has no documents at all — there are then no links to describe, and a
   * note about signed URLs above an empty section is a sentence about nothing.
   */
  protected readonly linkExpiry = computed<string | null>(() => {
    const expiries = this.review()
      ?.documents.map((document) => Date.parse(document.expiresAt))
      .filter((value) => Number.isFinite(value));
    if (!expiries?.length) return null;

    const earliest = Math.min(...expiries);
    return earliest <= this.now()
      ? this.t('dealerReview.signedLinksExpired')
      : this.t('dealerReview.signedLinksExpireAt', { time: this.formats.time(earliest) });
  });

  /**
   * The application's history, worded here from the facts the server sends.
   *
   * The server used to send each step as an English label and detail, which an Arabic screen printed
   * as it came. A decision the record can no longer name (the dealer has since resubmitted) is said to
   * have been recorded, never guessed at.
   */
  protected readonly timeline = computed<readonly TimelineStep[]>(() => {
    const review = this.review();
    if (!review) return [];
    return review.timeline.map((entry) => {
      const when = this.formats.dateTime(entry.occurredAt);
      const meta = (detail: string) => this.t('dealerReview.timelineMeta', { detail, when });
      const step = ((): { label: string; meta: string } => {
        switch (entry.step) {
          case 'Submitted':
            return {
              label: this.t('dealerReview.stepSubmitted'),
              meta: meta(review.dealer.businessName),
            };
          case 'DocumentsAttached':
            return {
              label: this.t('dealerReview.stepDocumentsAttached'),
              meta: meta(
                this.t('dealerReview.documentsOfRequired', {
                  have: this.formats.number(entry.documentCount ?? 0),
                  need: this.formats.number(entry.requiredDocumentCount ?? 0),
                }),
              ),
            };
          case 'Decision':
            return {
              label: this.decisionStepLabel(entry.decision),
              // A missing note means two different things. On a decision still standing, the reviewer
              // wrote none. On one the dealer has already answered,  cleared it —
              // so the note is not absent, it is no longer on record, and saying the first would be
              // this screen asserting something the server did not say.
              meta: meta(
                entry.note?.trim() ||
                  this.t(
                    entry.decision === null
                      ? 'dealerReview.noteNoLongerOnRecord'
                      : 'dealerReview.noNoteRecorded',
                  ),
              ),
            };
          case 'Resubmitted':
            return {
              label: this.t('dealerReview.stepResubmitted'),
              meta: meta(this.t('dealerReview.resubmittedDetail')),
            };
          case 'AwaitingDecision':
            return {
              label: this.t('dealerReview.stepAwaitingDecision'),
              meta: meta(this.t('dealerReview.noDecisionRecordedYet')),
            };
          default:
            // A step this console does not know yet: named from the wire, never dropped.
            return { label: this.i18n.statusLabel(entry.step), meta: when };
        }
      })();
      return {
        ...step,
        tone: entry.isComplete ? ('ok' as Tone) : ('warn' as Tone),
        future: !entry.isComplete,
      };
    });
  });

  /** The decision a step recorded, as the step's title. */
  private decisionStepLabel(decision: string | null): string {
    switch (decision) {
      case null:
        return this.t('dealerReview.stepDecisionRecorded');
      case 'Approved':
        return this.t('dealerReview.stepApproved');
      case 'Rejected':
        return this.t('dealerReview.stepRejected');
      case 'ClarificationNeeded':
        return this.t('dealerReview.stepClarificationRequested');
      default:
        return this.i18n.statusLabel(decision);
    }
  }

  /** A settled application has no decision left to make; a suspended one can only be reactivated. */
  protected readonly canDecide = computed(() => {
    const dealer = this.dealer();
    return (
      dealer !== null &&
      dealer.verificationStatus !== 'Approved' &&
      dealer.verificationStatus !== 'Rejected'
    );
  });

  /**
   * Clarification can only be asked of an application that is actually waiting on an Admin.
   *
   * `Dealer.RequestClarification` requires `IsAwaitingAdmin`, which is PendingReview alone — so on
   * an application already sent back, this button was offered and answered `dealer.not_awaiting_review`.
   * The ball is with the dealer until they resubmit; there is nothing to ask twice.
   */
  protected readonly canClarify = computed(
    () => this.dealer()?.verificationStatus === 'PendingReview',
  );

  protected readonly canSuspend = computed(() => {
    const dealer = this.dealer();
    return dealer !== null && dealer.verificationStatus === 'Approved' && !dealer.isSuspended;
  });

  protected readonly canReactivate = computed(() => this.dealer()?.isSuspended === true);

  /**
   * The reason recorded with a settled decision.
   *
   * Blank for an approval, which needs none: the note field carries a rejection reason or a
   * clarification request, and inventing prose for an approval would put words in an admin's mouth.
   *
   * A suspension keeps its reason in its OWN field, because it sits on top of an approval rather
   * than replacing it. Reading reviewNote alone made the screen for a suspended dealership say "No
   * reason was recorded with this decision" -- when the modal had refused to submit without one,
   * and the audit log had it all along.
   */
  protected readonly decisionNote = computed(() => {
    const dealer = this.dealer();
    if (!dealer) return null;
    if (dealer.isSuspended) return dealer.suspensionReason?.trim() || null;
    return dealer.reviewNote?.trim() || null;
  });

  protected readonly failure = computed(() => {
    const error = this.resource.error() as { status?: number } | undefined;
    if (!error) return null;
    if (error.status === 404) return this.t('dealerReview.thatDealerApplicationNo');
    if (error.status === 403) return this.t('dealerReview.onlyAdministratorsCanReview');
    return this.t('dealerReview.theApplicationCouldNot');
  });

  protected approve(): void {
    const dealer = this.dealer();
    if (!dealer) return;
    this.ui.openAction(
      {
        icon: 'check-circle',
        tone: 'ok',
        title: this.t('dealerReview.approveNameQuestion', { name: dealer.businessName }),
        body: this.t('dealerReview.theDealerWillBe'),
        note: this.t('dealerReview.thisDecisionIsRecorded'),
        confirm: this.t('dealerReview.approveDealer'),
        result: {
          title: this.t('dealerReview.dealerApproved'),
          body: this.t('dealerReview.nameCanNowTrade', { name: dealer.businessName }),
        },
      },
      async () => {
        await this.service.approve(dealer.dealerId);
        this.service.refresh();
      },
      {
        title: this.t('dealerReview.dealerApproved'),
        body: this.t('dealerReview.nameCanNowTrade', { name: dealer.businessName }),
      },
    );
  }

  protected reject(): void {
    const dealer = this.dealer();
    if (!dealer) return;
    this.ui.openAction(
      {
        icon: 'x-circle',
        tone: 'bad',
        danger: true,
        title: this.t('dealerReview.rejectNameQuestion', { name: dealer.businessName }),
        body: this.t('dealerReview.theApplicationIsClosed'),
        fields: [
          {
            // The key the typed value is stored under, read back as values['reason'] below: a
            // machine name, never a translation, or an Arabic screen sends an empty reason.
            name: 'reason',
            label: this.t('dealerDecide.reject.reasonLabel'),
            type: 'text',
            placeholder: this.t('dealerReview.whatIsWrongWith'),
            hint: this.t('dealerReview.theDealerSeesThis'),
          },
        ],
        confirm: this.t('dealerReview.rejectApplication'),
        result: { title: this.t('dealerReview.applicationRejected'), body: '', tone: 'bad' },
      },
      async (values) => {
        await this.service.reject(dealer.dealerId, values['reason'] ?? '');
        this.service.refresh();
      },
      {
        title: this.t('dealerReview.applicationRejected'),
        body: this.t('dealerReview.nameWasToldWhy', { name: dealer.businessName }),
        tone: 'bad',
      },
    );
  }

  protected clarify(): void {
    const dealer = this.dealer();
    if (!dealer) return;
    this.ui.openAction(
      {
        icon: 'question',
        tone: 'warn',
        title: this.t('dealerReview.requestClarification'),
        body: this.t('dealerReview.theApplicationGoesBack'),
        fields: [
          {
            // Read back as values['note']: a machine name, never a translation.
            name: 'note',
            label: this.t('dealerReview.note2'),
            type: 'text',
            placeholder: this.t('dealerReview.eGTheVehicle'),
            hint: this.t('dealerReview.nameTheOneThing'),
          },
        ],
        confirm: this.t('dealerReview.sendBack'),
        result: { title: this.t('dealerReview.sentBackToThe'), body: '', tone: 'warn' },
      },
      async (values) => {
        await this.service.requestClarification(dealer.dealerId, values['note'] ?? '');
        this.service.refresh();
      },
      {
        title: this.t('dealerReview.sentBackToThe'),
        body: this.t('dealerReview.theReviewClockRestarts'),
        tone: 'warn',
      },
    );
  }

  protected suspend(): void {
    const dealer = this.dealer();
    if (!dealer) return;
    this.ui.openAction(
      {
        icon: 'prohibit',
        tone: 'bad',
        danger: true,
        title: this.t('dealerReview.suspendNameQuestion', { name: dealer.businessName }),
        body: this.t('dealerReview.theyStopTradingImmediately'),
        fields: [
          {
            // Read back as values['reason']: a machine name, never a translation.
            name: 'reason',
            label: this.t('dealerDecide.reject.reasonLabel'),
            type: 'text',
            placeholder: this.t('dealerReview.whyIsThisDealer'),
          },
        ],
        confirm: this.t('dealerReview.suspendDealer'),
        result: { title: this.t('dealerReview.dealerSuspended'), body: '', tone: 'bad' },
      },
      async (values) => {
        await this.service.suspend(dealer.dealerId, values['reason'] ?? '');
        this.service.refresh();
      },
      {
        title: this.t('dealerReview.dealerSuspended'),
        body: this.t('dealerReview.nameCanNoLongerTrade', { name: dealer.businessName }),
        tone: 'bad',
      },
    );
  }

  protected reactivate(): void {
    const dealer = this.dealer();
    if (!dealer) return;
    this.ui.openAction(
      {
        icon: 'check-circle',
        tone: 'ok',
        title: this.t('dealerReview.reactivateNameQuestion', { name: dealer.businessName }),
        body: this.t('dealerReview.theyCanTradeAgain'),
        confirm: this.t('common.reactivate'),
        result: { title: this.t('dealerReview.dealerReactivated'), body: '' },
      },
      async () => {
        await this.service.reactivate(dealer.dealerId);
        this.service.refresh();
      },
      {
        title: this.t('dealerReview.dealerReactivated'),
        body: this.t('dealerReview.nameCanTradeAgain', { name: dealer.businessName }),
      },
    );
  }

  protected reload(): void {
    this.resource.reload();
  }
}
