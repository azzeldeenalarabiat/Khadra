import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { filter, map, startWith } from 'rxjs';
import { DealerConsoleService } from '../../core/services/dealer-console.service';
import { SessionService } from '../../core/services/session.service';
import { accountRouteFor } from '../../core/guards/role.guards';
import { Tone } from '../../core/models/console.models';
import { IconName } from '../../shared/icon/icon-paths';
import { loaded } from '../../core/services/loaded';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { IconComponent } from '../../shared/icon/icon.component';

interface LockedCopy {
  readonly icon: IconName;
  readonly tone: Tone;
  readonly badge: string;
  readonly title: string;
  readonly body: string;
  readonly listTitle: string;
  readonly points: readonly {
    readonly icon: IconName;
    readonly tone: Tone;
    readonly text: string;
  }[];
}

/**
 * The gate every dealer screen sits behind (design: the `locked` state).
 *
 * A dealership that cannot trade -- still under review, sent back for clarification, rejected, or
 * suspended -- sees one card explaining exactly where it stands instead of a console full of 403s.
 * The screens that are still theirs while locked (the dealer page, so a clarification can be fixed;
 * settings) stay reachable. The API enforces the same rule on every call; this is the explanation,
 * not the enforcement.
 */
@Component({
  selector: 'kh-dealer-gate',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dealer-gate.component.html',
  imports: [RouterOutlet, RouterLink, IconComponent],
})
export class DealerGateComponent {
  protected readonly t = inject(I18nService).t;
  private readonly format = inject(FormatService);
  private readonly console = inject(DealerConsoleService);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);

  /**
   * Never `me.value()` directly: it THROWS in the error state, and this component wraps EVERY dealer
   * screen, so the throw took the whole console down with it — the same failure `loaded()` was
   * written for on the admin side.
   */
  protected readonly dealer = loaded(this.console.me);

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects),
      startWith(this.router.url),
    ),
    { initialValue: this.router.url },
  );

  /**
   * Reachable while locked: the page an applicant is fixing, and their own account.
   *
   * The employee console has its own paths for the same two ideas — they have no application to fix,
   * so only their account is here.
   */
  private readonly openWhileLocked = ['/dealer/profile', '/dealer/settings', '/employee/settings'];

  /**
   * A SUSPENDED dealer still has customers holding its cars. Returns must be recordable, so the
   * bookings screens stay open; approving and rejecting are hidden there by the booking screen
   * itself, and the API refuses them regardless.
   */
  private readonly openWhileSuspended = [
    '/dealer/bookings',
    '/dealer/disputes',
    '/employee/bookings',
    '/employee/disputes',
  ];

  /**
   * Whether the dealership's standing is known yet.
   *
   * Four answers, not two. This used to have only "locked" and "not locked", and an unanswered
   * `GET /dealers/me` counted as not locked: a dealer whose standing could not be read was handed
   * the whole console, where every action then failed server-side with a 403 it could not explain.
   * A gate that cannot see has to say so, not wave everyone through.
   *
   * The fourth is an owner who has registered but not yet submitted a gallery — spec 3.1's gap
   * between step one and step two. The server says exactly that, `dealer.not_registered` on a 404,
   * and it is not a failure: it is the state every new owner starts in. Reading it as "unreachable"
   * told them the platform was broken and offered them a Try again that could never succeed.
   */
  private readonly failure = computed(
    () => this.console.me.error() as { status?: number; error?: { code?: string } } | undefined,
  );

  protected readonly waiting = computed(() => !this.dealer() && !this.failure());

  /** No dealership answers to this account. What that MEANS depends on who is asking. */
  protected readonly notRegistered = computed(() => {
    const error = this.failure();
    return !!error && error.status === 404 && error.error?.code === 'dealer.not_registered';
  });

  /**
   * The same 404 tells two different people two different things.
   *
   * For an owner it is "you have not filed your gallery yet", and the answer is the application
   * form. For an employee it is "the dealership that employed you has deactivated you" — spec 4.2
   * says their access ends immediately, and `DealerMembershipResolver` returns exactly this code for
   * them. Offering an employee a "submit your gallery" button would be wrong twice over: it is not
   * their business to file, and `POST /dealers` is owner-only, so the button could only ever 403.
   */
  protected readonly isOwner = computed(() => this.session.user()?.role === 'DealerOwner');

  /**
   * This card is shown to both consoles, and each has its own paths.
   *
   * Sending an employee at `/dealer/settings` from here was a dead link the moment they got a console
   * of their own: `dealerStaffGuard` bounces them straight back to the screen they are standing on.
   */
  protected readonly accountRoute = computed(() => accountRouteFor(this.session.user() ?? null));
  protected readonly bookingsRoute = computed(() =>
    this.session.user()?.role === 'DealerEmployee' ? '/employee/bookings' : '/dealer/bookings',
  );

  protected readonly unreachable = computed(
    () => !this.dealer() && !!this.failure() && !this.notRegistered(),
  );

  /** The application form is the one screen that belongs to an owner with no dealership. */
  protected readonly applying = computed(() => this.url().startsWith('/dealer/apply'));

  protected retry(): void {
    this.console.me.reload();
  }

  protected readonly locked = computed(() => {
    const dealer = this.dealer();
    if (!dealer || dealer.canTrade) return false;
    const open = dealer.isSuspended
      ? [...this.openWhileLocked, ...this.openWhileSuspended]
      : this.openWhileLocked;
    return !open.some((path) => this.url().startsWith(path));
  });

  /** A suspension is the one locked state where an employee still has work to do. */
  protected readonly suspended = computed(() => !!this.dealer()?.isSuspended);

  protected readonly copy = computed<LockedCopy | null>(() => {
    const dealer = this.dealer();
    if (!dealer) return null;
    const name = dealer.businessName;
    // "Your customers were notified" is the owner's sentence; an employee has no customers of their
    // own and cannot act on the suspension, so they are told what it means for their shift instead.
    const owner = this.isOwner();

    if (dealer.isSuspended) {
      return {
        icon: 'pause-circle',
        tone: 'warn',
        badge: this.t('gate.suspended.badge'),
        title: owner
          ? this.t('gate.suspended.titleOwner')
          : this.t('gate.suspended.titleStaff', { name }),
        body: owner
          ? this.t('gate.suspended.bodyOwner', { name })
          : this.t('gate.suspended.bodyStaff', { name }),
        listTitle: this.t('gate.suspended.listTitle'),
        points: [
          {
            icon: 'check',
            tone: 'ok',
            text: this.t('gate.suspended.point1'),
          },
          {
            icon: 'x',
            tone: 'bad',
            text: this.t('gate.suspended.point2'),
          },
          {
            icon: 'chat-circle',
            tone: 'accent',
            text: dealer.suspensionReason
              ? this.t('gate.suspended.reason', { reason: dealer.suspensionReason })
              : this.t('gate.suspended.noReason'),
          },
        ],
      };
    }

    switch (dealer.verificationStatus) {
      case 'Rejected':
        return {
          icon: 'x-circle',
          tone: 'bad',
          badge: this.t('gate.rejected.badge'),
          title: this.t('gate.rejected.title'),
          body: this.t('gate.rejected.body', { name }),
          listTitle: this.t('gate.rejected.listTitle'),
          points: [
            {
              icon: 'file-x',
              tone: 'bad',
              text: dealer.reviewNote ?? this.t('gate.rejected.noNote'),
            },
            {
              icon: 'clock-counter-clockwise',
              tone: 'accent',
              text: this.t('gate.rejected.reapply'),
            },
          ],
        };
      case 'ClarificationNeeded':
        return {
          icon: 'question',
          tone: 'warn',
          badge: this.t('gate.clarification.badge'),
          title: this.t('gate.clarification.title'),
          body: this.t('gate.clarification.body', { name }),
          listTitle: this.t('gate.clarification.listTitle'),
          points: [
            {
              icon: 'chat-circle',
              tone: 'warn',
              text: dealer.reviewNote ?? this.t('gate.clarification.noNote'),
            },
            {
              icon: 'check',
              tone: 'ok',
              text: this.t('gate.clarification.stillEditable'),
            },
          ],
        };
      default:
        return {
          icon: 'clock',
          tone: 'accent',
          badge: this.t('gate.pending.badge'),
          title: this.t('gate.pending.title'),
          body: this.t('gate.pending.body', { name }),
          listTitle: this.t('gate.pending.listTitle'),
          points: [
            {
              icon: 'clock',
              tone: 'accent',
              text: this.t('gate.pending.due', { due: this.format.dateTime(dealer.reviewDueAt) }),
            },
            {
              icon: 'storefront',
              tone: 'ok',
              text: this.t('gate.pending.prepare'),
            },
          ],
        };
    }
  });
}
