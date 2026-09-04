import { DatePipe } from '@angular/common';
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
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { isAtRisk } from '../../core/services/sla';
import { DocumentTile, KeyValue, TimelineStep, Tone } from '../../core/models/console.models';
import { DealerReview } from '../../core/models/dealers.api';
import { DocTileComponent } from '../../shared/doc-tile/doc-tile.component';
import { IconComponent } from '../../shared/icon/icon.component';
import { TimelineComponent } from '../../shared/timeline/timeline.component';

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
  imports: [DatePipe, RouterLink, IconComponent, DocTileComponent, TimelineComponent],
})
export class DealerReviewComponent {
  private readonly service = inject(AdminDealersService);
  private readonly ui = inject(ConsoleUiService);
  private readonly route = inject(ActivatedRoute);
  private readonly now = signal(Date.now());

  protected readonly resource = this.service.review;

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

  protected readonly dealer = computed(() => this.resource.value()?.dealer ?? null);

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
    if (dealer.isSuspended) return 'Suspended';
    return dealer.verificationStatus.replace(/([a-z])([A-Z])/g, '$1 $2');
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
    const review = this.resource.value();
    if (!review) return { figure: '—', note: '', tone: 'dim' as Tone };
    if (review.dealer.verificationStatus !== 'PendingReview') {
      return { figure: 'Settled', note: 'No decision outstanding.', tone: 'ok' as Tone };
    }

    const now = this.now();
    const started = Date.parse(review.dealer.submittedAt);
    const due = Date.parse(review.dealer.reviewDueAt);
    const promised = Math.round((due - started) / 3_600_000);
    const promise = `${promised}-hour review SLA`;
    const hours = Math.round(Math.abs(due - now) / 3_600_000);

    // `isBreachingSla` is the server's answer, frozen when this was fetched; the ticker below moves
    // on without it. Both are consulted, or a tab left open through the deadline reads the growing
    // overrun as time REMAINING -- "1h left", in the colour of good news, an hour after the promise
    // was broken. Same rule the list uses, so the two screens cannot disagree about one application.
    const breached = review.isBreachingSla || due <= now;

    return breached
      ? { figure: `${hours}h over`, note: `Past the ${promise}.`, tone: 'bad' as Tone }
      : {
          figure: `${hours}h left`,
          note: `Until the ${promise}.`,
          tone: isAtRisk(started, due, now) ? ('warn' as Tone) : ('ok' as Tone),
        };
  });

  protected readonly businessRows = computed<readonly KeyValue[]>(() => {
    const review = this.resource.value();
    if (!review) return [];
    return [
      { k: 'Business name', v: review.dealer.businessName },
      { k: 'Commercial registration', v: review.dealer.commercialRegistrationNumber },
      { k: 'Location', v: `${review.latitude.toFixed(4)}, ${review.longitude.toFixed(4)}` },
      { k: 'Description', v: review.description ?? '—' },
      { k: 'Submitted', v: new Date(review.dealer.submittedAt).toLocaleString('en-GB') },
      { k: 'Review due', v: new Date(review.dealer.reviewDueAt).toLocaleString('en-GB') },
      // Active staff only — the same number the dealership sees on its own profile. The review
      // response used to carry a second count that included deactivated rows, so one dealership
      // had two staff figures depending on which screen an admin was looking at.
      { k: 'Employees', v: String(review.dealer.employeeCount) },
      ...(review.dealer.reviewNote ? [{ k: 'Last review note', v: review.dealer.reviewNote }] : []),
    ];
  });

  protected readonly documents = computed<readonly DocumentTile[]>(() => {
    const review = this.resource.value();
    if (!review) return [];
    return review.documents.map((document) => ({
      label: document.type.replace(/([a-z])([A-Z])/g, '$1 $2'),
      status: 'Provided',
      tone: 'ok' as Tone,
      // The format the server will actually serve this file as -- not a filename, and certainly not
      // the signed URL, which is a credential. This tile read `${type}.jpg` for every document until
      // it turned out the registrations on file are PDFs: it was telling an Admin doing a licence
      // check that they were about to open a photograph. A real upload is keyed by a generated guid,
      // so there is no filename worth showing either; the format is the part that is true and useful.
      file: formatLabel(document.contentType),
      meta: `Link expires ${new Date(document.expiresAt).toLocaleTimeString('en-GB')}`,
      href: document.url,
    }));
  });

  protected readonly missing = computed(() => this.dealer()?.missingDocuments ?? []);

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
    const expiries = this.resource
      .value()
      ?.documents.map((document) => Date.parse(document.expiresAt))
      .filter((value) => Number.isFinite(value));
    if (!expiries?.length) return null;

    const earliest = Math.min(...expiries);
    return earliest <= this.now()
      ? 'expired — reload the page'
      : `expire at ${new Date(earliest).toLocaleTimeString('en-GB')}`;
  });

  protected readonly timeline = computed<readonly TimelineStep[]>(() => {
    const review = this.resource.value();
    if (!review) return [];
    return review.timeline.map((entry) => ({
      label: entry.label,
      meta: `${entry.detail} · ${new Date(entry.occurredAt).toLocaleString('en-GB')}`,
      tone: entry.isComplete ? ('ok' as Tone) : ('warn' as Tone),
      future: !entry.isComplete,
    }));
  });

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
   */
  protected readonly decisionNote = computed(() => this.dealer()?.reviewNote?.trim() || null);

  protected readonly failure = computed(() => {
    const error = this.resource.error() as { status?: number } | undefined;
    if (!error) return null;
    if (error.status === 404) return 'That dealer application no longer exists.';
    if (error.status === 403) return 'Only administrators can review dealer applications.';
    return 'The application could not be loaded. Nothing has been changed.';
  });

  protected approve(): void {
    const dealer = this.dealer();
    if (!dealer) return;
    this.ui.openAction(
      {
        icon: 'check-circle',
        tone: 'ok',
        title: `Approve ${dealer.businessName}?`,
        body: 'The dealer will be able to publish cars and receive bookings immediately.',
        note: 'This decision is recorded against your account in the audit log.',
        confirm: 'Approve dealer',
        result: { title: 'Dealer approved', body: `${dealer.businessName} can now trade.` },
      },
      async () => {
        await this.service.approve(dealer.dealerId);
        this.service.refresh();
      },
      { title: 'Dealer approved', body: `${dealer.businessName} can now trade.` },
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
        title: `Reject ${dealer.businessName}?`,
        body: 'The application is closed. The dealer can correct it and resubmit.',
        fields: [
          {
            label: 'Reason',
            type: 'text',
            placeholder: 'What is wrong with the application?',
            hint: 'The dealer sees this. Be specific enough to act on.',
          },
        ],
        confirm: 'Reject application',
        result: { title: 'Application rejected', body: '', tone: 'bad' },
      },
      async (values) => {
        await this.service.reject(dealer.dealerId, values['Reason'] ?? '');
        this.service.refresh();
      },
      { title: 'Application rejected', body: `${dealer.businessName} was told why.`, tone: 'bad' },
    );
  }

  protected clarify(): void {
    const dealer = this.dealer();
    if (!dealer) return;
    this.ui.openAction(
      {
        icon: 'question',
        tone: 'warn',
        title: 'Request clarification',
        body: 'The application goes back to the dealer with your note. They fix it and resubmit.',
        fields: [
          {
            label: 'Note',
            type: 'text',
            placeholder: 'e.g. the vehicle registration photo is unreadable',
            hint: 'Name the one thing to fix.',
          },
        ],
        confirm: 'Send back',
        result: { title: 'Sent back to the dealer', body: '', tone: 'warn' },
      },
      async (values) => {
        await this.service.requestClarification(dealer.dealerId, values['Note'] ?? '');
        this.service.refresh();
      },
      {
        title: 'Sent back to the dealer',
        body: 'The review clock restarts when they resubmit.',
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
        title: `Suspend ${dealer.businessName}?`,
        body: 'They stop trading immediately. The licence check is not undone, so reactivating does not send them back through review.',
        fields: [
          { label: 'Reason', type: 'text', placeholder: 'Why is this dealer being suspended?' },
        ],
        confirm: 'Suspend dealer',
        result: { title: 'Dealer suspended', body: '', tone: 'bad' },
      },
      async (values) => {
        await this.service.suspend(dealer.dealerId, values['Reason'] ?? '');
        this.service.refresh();
      },
      {
        title: 'Dealer suspended',
        body: `${dealer.businessName} can no longer trade.`,
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
        title: `Reactivate ${dealer.businessName}?`,
        body: 'They can trade again straight away; their approval was never withdrawn.',
        confirm: 'Reactivate',
        result: { title: 'Dealer reactivated', body: '' },
      },
      async () => {
        await this.service.reactivate(dealer.dealerId);
        this.service.refresh();
      },
      { title: 'Dealer reactivated', body: `${dealer.businessName} can trade again.` },
    );
  }

  protected reload(): void {
    this.resource.reload();
  }
}

/**
 * "application/pdf" -> "PDF". What the reviewer is about to open, in a word.
 *
 * An unrecognised type is shown as it arrived rather than guessed at: the Admin can still read it,
 * and the tile does not claim a format nobody vouched for.
 */
function formatLabel(contentType: string): string {
  return (
    {
      'application/pdf': 'PDF',
      'image/jpeg': 'JPEG image',
      'image/png': 'PNG image',
      'image/webp': 'WebP image',
    }[contentType] ?? contentType
  );
}
