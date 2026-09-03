import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { AdminDealersService } from '../../core/services/admin-dealers.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
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
  imports: [DatePipe, IconComponent, DocTileComponent, TimelineComponent],
})
export class DealerReviewComponent {
  private readonly service = inject(AdminDealersService);
  private readonly ui = inject(ConsoleUiService);
  private readonly route = inject(ActivatedRoute);
  private readonly now = signal(Date.now());

  protected readonly resource = this.service.review;

  constructor() {
    this.service.reviewing.set(this.route.snapshot.paramMap.get('dealerId'));
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

  /** How long is left on the 48-hour promise, or how far past it this application already is. */
  protected readonly sla = computed(() => {
    const review = this.resource.value();
    if (!review) return { figure: '—', note: '', tone: 'dim' as Tone };
    if (review.dealer.verificationStatus !== 'PendingReview') {
      return { figure: 'Settled', note: 'No decision outstanding.', tone: 'ok' as Tone };
    }

    const due = Date.parse(review.dealer.reviewDueAt);
    const hours = Math.round(Math.abs(due - this.now()) / 3_600_000);
    return review.isBreachingSla
      ? { figure: `${hours}h over`, note: 'Past the 48-hour review SLA.', tone: 'bad' as Tone }
      : {
          figure: `${hours}h left`,
          note: 'Until the 48-hour review SLA.',
          tone: hours < 12 ? ('warn' as Tone) : ('ok' as Tone),
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
      { k: 'Employees', v: String(review.employeeCount) },
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
      // A name, not the signed URL: the URL is a credential and has no business being on screen.
      file: `${document.type}.jpg`,
      meta: `Link expires ${new Date(document.expiresAt).toLocaleTimeString('en-GB')}`,
      href: document.url,
    }));
  });

  protected readonly missing = computed(() => this.dealer()?.missingDocuments ?? []);

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

  protected readonly canSuspend = computed(() => {
    const dealer = this.dealer();
    return dealer !== null && dealer.verificationStatus === 'Approved' && !dealer.isSuspended;
  });

  protected readonly canReactivate = computed(() => this.dealer()?.isSuspended === true);

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
