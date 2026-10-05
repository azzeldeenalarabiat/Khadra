import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { KeyValue, TimelineStep, Tone } from '../../core/models/console.models';
import {
  Dispute,
  DisputeResolution,
  DisputeStatement,
  ResolutionPreview,
} from '../../core/models/disputes.api';
import { AdminDisputesService } from '../../core/services/admin-disputes.service';
import { roundTo } from '../../core/services/money';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { TimelineComponent } from '../../shared/timeline/timeline.component';
import { I18nService } from '../../core/i18n/i18n.service';
import { FormatService } from '../../core/i18n/format.service';
import { TranslationKey } from '../../core/i18n/en';
import { Language } from '../../core/i18n/language';
import { ProblemSnapshot, serverSentence, snapshotProblem } from '../../core/i18n/problem';
import { MoneyPipe } from '../../shared/money.pipe';
import { decidedEarlier, earlierDecisionNotice } from './earlier-decisions.presenter';
import {
  chargeAllowance,
  chargeProblem,
  disputeRefusal,
  exceedsPlaces,
  slaTone,
  splitPlaces,
  stepFor,
} from './dispute-form.presenter';
import { PayoutFormat } from '../payouts/payouts.presenter';
import { PreviewWords, previewConfirmSentence, previewView } from './resolution-preview.presenter';

/** How long the split must rest before its preview is asked for: a pause in typing, not a business figure. */
const PREVIEW_PAUSE_MS = 400;

/** The four shapes spec 3.3 names, each one a preset split of the deposit the booking holds. */
type Preset = 'refund' | 'penalty' | 'partial' | 'waive';

/**
 * Dispute resolution (spec 3.3): the one screen on the platform where a decision about money is
 * made, and the only way a penalty is ever applied.
 *
 * A resolution is three legs of the held deposit plus an optional charge to the dealer. The legs
 * must add up to exactly what the booking froze, and the server is the judge of that — this screen
 * shows the running total so an admin sees the imbalance before submitting, but it never decides
 * the basis itself. Until Payments ships, resolving records the decision and moves nothing, which
 * the panel says next to the button rather than in a footnote.
 */
@Component({
  selector: 'kh-dispute-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dispute-detail.component.html',
  imports: [RouterLink, IconComponent, TimelineComponent, MoneyPipe],
})
export class DisputeDetailComponent {
  private readonly i18n = inject(I18nService);
  protected readonly t = this.i18n.t;
  // Server enum names, in the reader's language. Shared rather than per-component: the same enum
  // shows on half a dozen screens, and a copy each is a copy each to forget a new member in.
  protected readonly statusLabel = this.i18n.statusLabel;
  protected readonly enumLabel = this.i18n.enumLabel;
  private readonly formats = inject(FormatService);
  private readonly service = inject(AdminDisputesService);
  private readonly ui = inject(ConsoleUiService);
  private readonly route = inject(ActivatedRoute);

  private readonly ticketId = toSignal(
    this.route.paramMap.pipe(map((params) => params.get('ticketId'))),
    { initialValue: this.route.snapshot.paramMap.get('ticketId') },
  );

  /**
   * The ticket the split on screen was seeded for.
   *
   * The router reuses this component between /disputes/A and /disputes/B, and the resource reloads
   * whenever the ticket is taken on, so "have I seeded yet" cannot be a boolean.
   */
  private seededFor: string | null = null;

  constructor() {
    effect(() => this.service.viewing.set(this.ticketId()));
    // The split is seeded ONCE per ticket, from the ticket, so it starts balanced against this
    // booking. It deliberately reads nothing but the loaded dispute: seeding used to call
    // apply('partial'), which reads allocated(), so the effect took a dependency on the three legs
    // -- every keystroke re-ran it and reset the admin's split back to a full refund, while the
    // boxes kept showing what had been typed and the total kept claiming it balanced.
    effect(() => {
      const d = this.dispute();
      if (!d || d.resolution || this.seededFor === d.ticketId) return;
      this.seededFor = d.ticketId;
      this.seed(d);
    });
    // The preview follows the split, after a pause: one request when typing stops, not one per keystroke.
    effect(() => {
      const split = this.previewSplit();
      const key = this.previewKey();
      clearTimeout(this.previewTimer);
      if (!split || !key) return;
      this.previewTimer = setTimeout(() => {
        const { ticketId, ...body } = split;
        this.service
          .preview(ticketId, body)
          .then((preview) => this.previewAnswer.set({ key, preview }))
          .catch((error: unknown) => this.previewAnswer.set({ key, problem: snapshotProblem(error) }));
      }, PREVIEW_PAUSE_MS);
    });
    inject(DestroyRef).onDestroy(() => clearTimeout(this.previewTimer));
  }

  protected readonly resource = this.service.dispute;

  /**
   * `httpResource.value()` THROWS while the resource is in an error state, so it cannot be read
   * unguarded from an effect: the throw escapes, change detection stops, and a ticket that 404s
   * renders a blank page instead of the "Couldn't load this dispute" block the template already has.
   */
  protected readonly dispute = computed(() =>
    this.resource.hasValue() ? this.resource.value() : null,
  );

  protected readonly preset = signal<Preset>('partial');
  protected readonly refund = signal(0);
  protected readonly platform = signal(0);
  protected readonly dealer = signal(0);
  protected readonly dealerCharge = signal('');
  protected readonly note = signal('');
  protected readonly busy = signal(false);
  /** A refused action, held as the facts the server sent; worded in `problemText`. */
  protected readonly problem = signal<ProblemSnapshot | null>(null);

  /**
   * The refusal in the language on screen. Chosen here rather than when the request failed, so a
   * switch while the banner is showing re-words it instead of leaving it in the old language.
   */
  protected readonly problemText = computed(() => {
    const problem = this.problem();
    return problem ? disputeRefusal(problem, this.t, this.i18n.lang()) : null;
  });

  /**
   * The four ways an administrator can dispose of a held deposit.
   *
   * A `computed` rather than a `readonly` field, and the difference is a bug rather than a style
   * preference: a field initialiser resolves ONCE at construction, so `this.t(...)` in one freezes
   * the language at the moment the screen was created. Two of these labels were already keyed and
   * already frozen -- switching to Arabic with this screen open left them in English until a reload.
   * Pre-launch item 49 records the same defect on the fleet filter chips.
   */
  protected readonly presets = computed<readonly { key: Preset; label: string; desc: string }[]>(
    () => [
      {
        key: 'refund',
        label: this.t('disputeDetail.refundTheCustomer'),
        desc: this.t('disputeDetail.theWholeDepositGoes'),
      },
      {
        key: 'penalty',
        label: this.t('disputeDetail.applyThePenaltyIn'),
        desc: this.t('disputeDetail.theDepositIsSplit'),
      },
      {
        key: 'partial',
        label: this.t('disputeDetail.partial'),
        desc: this.t('disputeDetail.youSetEachLeg'),
      },
      {
        key: 'waive',
        label: this.t('disputeDetail.waiveEverything'),
        desc: this.t('disputeDetail.noPenaltyTheDeposit'),
      },
    ],
  );

  /** A failed load, held as the resource's facts and worded here, so a language switch re-words it. */
  protected readonly failure = computed(() => {
    const error = this.resource.error();
    if (!error) return null;
    return describeLoadFailure(snapshotProblem(error), this.t, this.i18n.lang());
  });

  /**
   * The currency the deposit is actually held in. Only ever read inside the loaded-dispute branch of
   * the template, so the empty fallback cannot reach the screen -- and an assumed 'JOD' would be a
   * currency code printed beside three amounts on the one screen where the platform moves money.
   */
  protected readonly currency = computed(() => this.dispute()?.depositHeld.currency ?? '');
  protected readonly held = computed(() => this.dispute()?.depositHeld.amount ?? 0);

  /**
   * The places the split is entered and rounded at: the currency's minor units as the server states
   * them, or the figures' own precision until it has (E2E F35, checklist 16).
   */
  private readonly scale = computed(() =>
    splitPlaces(
      this.formats.minorUnits(),
      this.held(),
      this.refund(),
      this.platform(),
      this.dealer(),
    ),
  );

  /** What one press of an input's spinner is worth: one minor unit of the currency. */
  protected readonly step = computed(() =>
    stepFor(splitPlaces(this.formats.minorUnits(), this.held())),
  );

  /** A leg typed with more places than the currency has: the server refuses it rather than rounding (F36). */
  protected readonly placesHint = computed(() => {
    const units = this.formats.minorUnits();
    const legs = [this.refund(), this.platform(), this.dealer()];
    return units !== undefined && legs.some((leg) => exceedsPlaces(leg, units))
      ? this.t('disputeDetail.tooManyPlaces', { count: units })
      : null;
  });

  /** What the booking lets this ticket charge the office (F34), by the server's own rule. */
  protected readonly allowance = computed(() => {
    const d = this.dispute();
    return d ? chargeAllowance(d) : null;
  });

  /** The range the charge field accepts, or null while it is closed. */
  protected readonly chargeRange = computed(() => {
    const allowance = this.allowance();
    return allowance?.kind === 'range' ? allowance : null;
  });

  private readonly chargeIssue = computed(() => {
    const allowance = this.allowance();
    return allowance
      ? chargeProblem(this.dealerCharge(), allowance, this.formats.minorUnits())
      : null;
  });

  /** The line under the charge field: why it is closed, what it allows, or what is wrong with it. */
  protected readonly chargeHint = computed<{ readonly text: string; readonly bad: boolean } | null>(
    () => {
      const allowance = this.allowance();
      if (!allowance) return null;
      if (allowance.kind === 'none') {
        return {
          text:
            allowance.reason === 'notAssessed'
              ? this.t('disputeDetail.chargeNotAssessedHint')
              : this.t('disputeDetail.chargeExhaustedHint'),
          bad: false,
        };
      }
      const issue = this.chargeIssue();
      const units = this.formats.minorUnits();
      if (issue === 'tooPrecise' && units !== undefined) {
        return { text: this.t('disputeDetail.tooManyPlaces', { count: units }), bad: true };
      }
      if (issue === 'notANumber') return { text: this.t('disputeDetail.notAnAmount'), bad: true };
      // What the booking allows, in the server's figures, in the alarm tone once what was typed falls
      // outside it.
      const text = allowance.chargedEarlier
        ? this.t('disputeDetail.chargeAfterEarlierHint', {
            charged: this.formats.money(
              allowance.chargedEarlier.amount,
              allowance.chargedEarlier.currency,
            ),
            max: this.formats.money(allowance.max, allowance.currency),
          })
        : this.t('disputeDetail.chargeRangeHint', {
            range: this.formats.moneyRange(allowance.min, allowance.max, allowance.currency),
          });
      return { text, bad: issue === 'outOfRange' };
    },
  );

  protected readonly allocated = computed(() =>
    roundTo(this.refund() + this.platform() + this.dealer(), this.scale()),
  );
  protected readonly remainder = computed(() =>
    roundTo(this.held() - this.allocated(), this.scale()),
  );
  protected readonly balanced = computed(() => this.remainder() === 0);
  /** On a live ticket, what earlier disputes on this booking already decided (item 169), or null. */
  protected readonly earlierNotice = computed(() => {
    const d = this.dispute();
    return d
      ? earlierDecisionNotice(d, this.t, (value) => this.formats.money(value.amount, value.currency))
      : null;
  });

  // ── What the decision does to the money (Wave 2 C1; E2E F37) ───────────────────────────────────
  //
  // Asked of the server whenever the split rests, once it adds up and passes the form's own checks: the preview runs
  // every check resolving runs, then the office's money exactly as the payouts ledger would record it. Shown only for
  // the split it was taken of, so a figure on screen is never one the current amounts would not produce.

  private readonly previewFormat: PayoutFormat = {
    money: (value) => this.formats.money(value.amount, value.currency),
    dateTime: (iso) => this.formats.dateTime(iso),
    day: (isoDay) => this.formats.calendarDay(isoDay),
  };

  private readonly previewWords = computed<PreviewWords>(() => {
    this.i18n.lang();
    return {
      t: this.t,
      label: this.i18n.enumLabel,
      status: (name: string) => this.statusLabel(name, 'booking'),
    };
  });

  /** The split a preview would be taken of, or null while it cannot be: unbalanced, refused here, or not live. */
  private readonly previewSplit = computed(() => {
    const d = this.dispute();
    if (!d?.isLive || !this.balanced() || this.placesHint() !== null || this.chargeIssue() !== null) return null;
    const charge = this.allowance()?.kind === 'range' ? this.dealerCharge().trim() : '';
    return {
      ticketId: d.ticketId,
      refundToCustomer: this.refund(),
      retainedByPlatform: this.platform(),
      transferredToDealer: this.dealer(),
      dealerCharge: charge === '' ? null : Number(charge),
    };
  });

  private readonly previewKey = computed(() => {
    const split = this.previewSplit();
    return split ? JSON.stringify(split) : null;
  });

  private readonly previewAnswer = signal<
    | { readonly key: string; readonly preview: ResolutionPreview }
    | { readonly key: string; readonly problem: ProblemSnapshot }
    | null
  >(null);
  private previewTimer: ReturnType<typeof setTimeout> | undefined;

  /** The preview of the split on screen, or null while there is none for it yet. */
  protected readonly preview = computed(() => {
    const answer = this.previewAnswer();
    return answer && answer.key === this.previewKey() && 'preview' in answer ? answer.preview : null;
  });

  protected readonly previewProblemText = computed(() => {
    const answer = this.previewAnswer();
    return answer && answer.key === this.previewKey() && 'problem' in answer
      ? disputeRefusal(answer.problem, this.t, this.i18n.lang())
      : null;
  });

  protected readonly previewPending = computed(
    () => this.previewKey() !== null && this.preview() === null && this.previewProblemText() === null,
  );

  protected readonly previewShown = computed(() => {
    const preview = this.preview();
    return preview ? previewView(preview, this.previewWords(), this.previewFormat) : null;
  });

  protected readonly canResolve = computed(
    () =>
      !!this.dispute()?.isLive &&
      this.balanced() &&
      this.placesHint() === null &&
      this.chargeIssue() === null &&
      this.note().trim().length > 0 &&
      !this.busy(),
  );

  protected readonly tone = computed<Tone>(() => {
    const d = this.dispute();
    if (!d) return 'dim';
    if (d.status === 'Resolved') return 'ok';
    if (d.status === 'Withdrawn') return 'dim';
    return d.isOverdue ? 'bad' : 'warn';
  });

  /**
   * The platform's promise on this ticket: "7h remaining", then "Overdue by 13h" — the server's flag OR
   * the clock — tinted by the server's SLA state, the work queue's own rule (F42).
   */
  protected readonly sla = computed(() => {
    const d = this.dispute();
    if (!d) return { figure: '', tone: null };
    if (!d.isLive) return { figure: this.t('disputeDetail.closed'), tone: null };
    const reading = this.formats.sla(d.slaDeadline, d.isOverdue);
    return { figure: reading.text, tone: slaTone(d, reading.passed) };
  });

  protected readonly age = computed(() => {
    const d = this.dispute();
    if (!d) return '';
    return this.formats.duration(Date.now() - Date.parse(d.openedAt));
  });

  /** The three panels the design puts across the top: the booking, the parties, the money. */
  protected readonly cards = computed<
    readonly {
      title: string;
      icon: 'car-simple' | 'user' | 'currency-circle-dollar';
      rows: readonly KeyValue[];
    }[]
  >(() => {
    const d = this.dispute();
    if (!d) return [];
    const b = d.booking;
    // Every amount carries the code of its own value, never one borrowed from a neighbouring figure.
    const money = (value: { readonly amount: number; readonly currency: string }): string =>
      this.formats.money(value.amount, value.currency);
    const penalty = b.penalty && !b.penalty.isNothingOwed ? b.penalty : null;
    const earlier = decidedEarlier(d);
    return [
      {
        title: this.t('common.booking'),
        icon: 'car-simple',
        rows: [
          { k: this.t('bookingsList.reference'), v: b.reference },
          {
            k: this.t('dealerBooking.vehicle'),
            v: b.vehicle
              ? `${b.vehicle.make} ${b.vehicle.model} ${b.vehicle.year}`
              : this.t('dealerBooking.noLongerListed'),
          },
          {
            k: this.t('dealerBooking.rental'),
            v: this.t('disputeDetail.periodRange', {
              start: this.date(b.periodStart),
              end: this.date(b.periodEnd),
            }),
          },
          { k: this.t('common.status'), v: this.statusLabel(b.status, 'booking') },
        ],
      },
      {
        title: this.t('disputesList.parties'),
        icon: 'user',
        rows: [
          { k: this.t('dealersList.colDealer'), v: this.dealerName(d) },
          { k: this.t('vehicleDetail.customer'), v: this.customerName(d) },
          {
            k: this.t('disputesList.raisedBy'),
            v: this.t('disputeDetail.nameWithParty', {
              name: this.openerName(d),
              party: this.enumLabel('party', d.openedByParty),
            }),
          },
          {
            k: this.t('disputeDetail.handledBy'),
            v: this.holderName(d) ?? this.t('common.unassigned'),
          },
        ],
      },
      {
        title: this.t('disputeDetail.moneyOnThisBooking'),
        icon: 'currency-circle-dollar',
        rows: [
          { k: this.t('myBooking.rentalTotal'), v: money(b.pricing.rentalTotal) },
          { k: this.t('common.depositHeld'), v: money(d.depositHeld) },
          // Why a later ticket holds less (item 169): the server's figure, shown, never subtracted.
          ...(earlier ? [{ k: this.t('common.decidedByEarlierDisputes'), v: money(earlier) }] : []),
          { k: this.t('vehicleDetail.securityDeposit'), v: money(b.pricing.securityDeposit) },
          {
            k: this.t('common.penaltyAssessed'),
            // Two facts, each worded on its own: the amount (ONE run for a range, so Arabic cannot
            // lay the bounds out upper bound first) and the party it is assessed against.
            v: penalty
              ? [
                  penalty.isRange
                    ? this.formats.moneyRange(
                        penalty.minAmount.amount,
                        penalty.maxAmount.amount,
                        penalty.minAmount.currency,
                      )
                    : money(penalty.minAmount),
                  this.enumLabel('party', penalty.attributedTo),
                ].join(' · ')
              : this.t('common.none'),
          },
        ],
      },
    ];
  });

  /** Every statement and every decision, oldest first: the case as it actually unfolded. */
  protected readonly timeline = computed<readonly TimelineStep[]>(() => {
    const d = this.dispute();
    if (!d) return [];
    // The opening statement IS statement #1 -- Open() writes it from the reason -- so it is labelled
    // as the opening rather than added a second time above the list. Listing both put the same
    // sentence on the trail twice and made a one-statement ticket read as two.
    // Meta lines are lists of whole facts, each worded on its own, joined by a separator.
    const facts = (...parts: string[]) => parts.join(' · ');
    const steps: TimelineStep[] = d.statements.map((s, index) => ({
      label:
        index === 0
          ? this.t('disputeDetail.openedBy', { name: this.authorName(s) })
          : this.t('disputeDetail.answeredBy', { name: this.authorName(s) }),
      meta: facts(
        this.when(s.createdAt),
        this.enumLabel('party', s.party),
        ...(s.evidence.length
          ? [this.t('disputeDetail.filesAttached', { count: s.evidence.length })]
          : []),
      ),
      tone: (index === 0 ? 'warn' : s.party === 'Dealer' ? 'accent' : 'dim') as Tone,
    }));
    if (steps.length === 0) {
      steps.push({
        label: this.t('disputeDetail.openedBy', { name: this.openerName(d) }),
        meta: facts(
          this.when(d.openedAt),
          this.enumLabel('party', d.openedByParty),
          this.t('disputeDetail.quoted', { text: d.reason }),
        ),
        tone: 'warn',
      });
    }
    const holder = this.holderName(d);
    if (holder !== null && !d.resolution) {
      steps.push({
        label: this.t('disputeDetail.takenOnBy', { name: holder }),
        meta: this.t('status.underReview'),
        tone: 'accent',
      });
    }
    if (d.resolution) {
      steps.push({
        label: this.t('disputeDetail.resolvedBy', { name: this.resolverName(d.resolution) }),
        meta: facts(
          this.when(d.resolution.resolvedAt),
          this.t('disputeDetail.quoted', { text: d.resolution.note }),
        ),
        tone: 'ok',
      });
    } else {
      steps.push({
        label: this.t('disputeDetail.decision'),
        meta: this.t('disputeDetail.dueAt', { when: this.when(d.slaDeadline) }),
        tone: 'dim',
        future: true,
      });
    }
    return steps;
  });

  /** Every file both parties attached, newest first, with the party that sent it. */
  protected readonly evidence = computed(() => {
    const d = this.dispute();
    if (!d) return [];
    return d.statements.flatMap((s) =>
      s.evidence.map((file) => ({
        fileName: file.fileName,
        url: file.url,
        party: s.party,
        author: this.authorName(s),
        when: this.when(s.createdAt),
      })),
    );
  });

  protected select(preset: Preset): void {
    const d = this.dispute();
    if (d) this.apply(preset, d);
  }

  /**
   * The opening position for a ticket: the whole deposit back to the customer, which balances.
   *
   * Writes only. Nothing here reads a signal, so the effect that calls it cannot end up depending
   * on the very fields an admin is typing into.
   */
  private seed(d: Dispute): void {
    this.preset.set('partial');
    this.refund.set(d.depositHeld.amount);
    this.platform.set(0);
    this.dealer.set(0);
    this.dealerCharge.set('');
    this.note.set('');
    this.problem.set(null);
  }

  /** Seeds the three legs from a preset. Partial keeps whatever is already there to edit. */
  private apply(preset: Preset, d: Dispute): void {
    this.preset.set(preset);
    const held = d.depositHeld.amount;
    switch (preset) {
      case 'refund':
      case 'waive':
        this.refund.set(held);
        this.platform.set(0);
        this.dealer.set(0);
        this.dealerCharge.set('');
        break;
      case 'penalty': {
        // The booking's own assessment decides who the deposit follows; a customer at fault means
        // the dealer is made whole from it, a dealer at fault means the customer is.
        const owed =
          d.booking.penalty && !d.booking.penalty.isNothingOwed ? d.booking.penalty : null;
        const toDealer =
          owed?.attributedTo === 'Customer' ? Math.min(held, owed.minAmount.amount) : 0;
        const places = splitPlaces(this.formats.minorUnits(), held, toDealer);
        this.dealer.set(roundTo(toDealer, places));
        this.refund.set(roundTo(held - toDealer, places));
        this.platform.set(0);
        break;
      }
      case 'partial':
        // Nothing. "Partial" means the admin sets each leg themselves, so choosing it must not
        // overwrite the legs they are already setting -- it used to reset an unbalanced split back
        // to a full refund, which is the one thing an admin part-way through a split has not asked
        // for. The opening position is seeded once when the ticket loads; see seed().
        break;
    }
  }

  protected setLeg(leg: 'refund' | 'platform' | 'dealer', event: Event): void {
    const value = Math.max(0, Number((event.target as HTMLInputElement).value) || 0);
    if (leg === 'refund') this.refund.set(value);
    if (leg === 'platform') this.platform.set(value);
    if (leg === 'dealer') this.dealer.set(value);
    this.preset.set('partial');
  }

  protected text(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement).value;
  }

  protected assign(): void {
    const d = this.dispute();
    if (!d || this.busy()) return;
    this.busy.set(true);
    this.service
      .assign(d.ticketId)
      .then(() => {
        this.service.refresh();
        this.ui.showToast(
          this.t('disputeDetail.assignedToYou'),
          this.t('disputeDetail.recordedOnTheTicket'),
        );
      })
      .catch((error: unknown) => this.problem.set(snapshotProblem(error)))
      .finally(() => this.busy.set(false));
  }

  protected resolve(): void {
    const d = this.dispute();
    if (!d || !this.canResolve()) return;
    // Only a charge the booking allows is ever sent; the field is closed for any other.
    const charge = this.allowance()?.kind === 'range' ? this.dealerCharge().trim() : '';
    const cur = this.currency();

    this.ui.openAction(
      {
        icon: 'scales',
        tone: 'warn',
        danger: true,
        title: this.t('disputeDetail.recordThisDecision'),
        // One whole sentence per shape, with every amount formatted in the deposit's own currency and
        // each party named as the reader's language names them -- never the English stand-in.
        body: charge
          ? this.t('disputeDetail.resolveConfirmBodyWithCharge', {
              refund: this.formats.money(this.refund(), cur),
              customer: this.customerName(d),
              platform: this.formats.money(this.platform(), cur),
              dealerShare: this.formats.money(this.dealer(), cur),
              dealer: this.dealerName(d),
              charge: this.formats.money(Number(charge), cur),
            })
          : this.t('disputeDetail.resolveConfirmBody', {
              refund: this.formats.money(this.refund(), cur),
              customer: this.customerName(d),
              platform: this.formats.money(this.platform(), cur),
              dealerShare: this.formats.money(this.dealer(), cur),
              dealer: this.dealerName(d),
            }),
        // What the payouts ledger will record, when the preview of exactly this split is on screen.
        note: [
          ...(this.preview() ? [previewConfirmSentence(this.preview()!, this.previewWords(), this.previewFormat)] : []),
          this.t('disputeDetail.decisionRecordedNoFunds'),
        ].join(' '),
        confirm: this.t('disputeDetail.resolveDispute'),
        result: {
          title: this.t('disputeDetail.disputeResolved'),
          body: this.t('disputeDetail.decisionRecordedNoFunds2'),
        },
      },
      async () => {
        await this.service.resolve(d.ticketId, {
          refundToCustomer: this.refund(),
          retainedByPlatform: this.platform(),
          transferredToDealer: this.dealer(),
          dealerCharge: charge === '' ? null : Number(charge),
          note: this.note().trim(),
        });
        this.service.refresh();
        this.service.refreshList();
      },
      {
        title: this.t('disputeDetail.disputeResolved'),
        body: this.t('disputeDetail.decisionRecordedNoFunds2'),
      },
    );
  }

  protected reload(): void {
    this.service.refresh();
  }

  /**
   * An amount at the deposit's own scale with no code beside it, for the running total where the
   * code is printed once after the held figure. Never a sign typed in front: a negative remainder
   * (more allocated than is held) keeps the minus the formatter gives it.
   */
  protected amount(value: number): string {
    return this.formats.money(value, null);
  }

  /** The held deposit with its own currency code. */
  protected heldWithCode(): string {
    return this.formats.money(this.held(), this.currency());
  }

  protected date(iso: string): string {
    return this.formats.date(iso);
  }

  protected when(iso: string): string {
    return this.formats.dayMonthTime(iso);
  }

  protected initials(name: string): string {
    return name
      .trim()
      .split(/\s+/)
      .slice(0, 2)
      .map((part) => part[0] ?? '')
      .join('');
  }

  // ── People, in the reader's language ─────────────────────────────────────────────────────────
  //
  // Every name on this dispute arrives with a fact beside it. When the fact says the account or the
  // dealership is gone, the name is an English stand-in kept for older customer apps, and this screen
  // says so in its own words instead.

  protected openerName(d: Dispute): string {
    return d.openedByAccountClosed ? this.t('common.accountClosed') : d.openedByName;
  }

  protected authorName(s: DisputeStatement): string {
    return s.authorAccountClosed ? this.t('common.accountClosed') : s.authorName;
  }

  protected resolverName(r: DisputeResolution): string {
    return r.resolvedByAccountClosed ? this.t('common.accountClosed') : r.resolvedByName;
  }

  /** Who holds the ticket, or null while nobody does. A closed account still holds it. */
  protected holderName(d: Dispute): string | null {
    if (d.assignedAdminId === null) return null;
    return d.assignedAdminAccountClosed || d.assignedAdminName === null
      ? this.t('common.accountClosed')
      : d.assignedAdminName;
  }

  protected dealerName(d: Dispute): string {
    return d.booking.dealerRemoved
      ? this.t('common.dealerNoLongerOnPlatform')
      : d.booking.dealerName;
  }

  protected customerName(d: Dispute): string {
    return d.booking.customerAccountClosed
      ? this.t('common.customerAccountClosed')
      : d.booking.customerName;
  }
}

/** Why the ticket could not load, in the language on screen when it is shown. */
function describeLoadFailure(
  problem: ProblemSnapshot,
  t: (key: TranslationKey) => string,
  language: Language,
): string {
  if (problem.status === 404) return t('disputeDetail.thatDisputeWasNot');
  if (problem.status === 403) return t('disputeDetail.theDisputeWorkspaceIs');
  return serverSentence(problem, language, t) ?? t('dealerDispute.theDisputeCouldNot');
}
