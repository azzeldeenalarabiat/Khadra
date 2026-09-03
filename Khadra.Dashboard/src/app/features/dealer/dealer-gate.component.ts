import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { filter, map, startWith } from 'rxjs';
import { DealerConsoleService } from '../../core/services/dealer-console.service';
import { Tone } from '../../core/models/console.models';
import { IconName } from '../../shared/icon/icon-paths';
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
  private readonly console = inject(DealerConsoleService);
  private readonly router = inject(Router);

  protected readonly me = this.console.me;

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects),
      startWith(this.router.url),
    ),
    { initialValue: this.router.url },
  );

  /** Reachable while locked: the page an applicant is fixing, and their own account. */
  private readonly openWhileLocked = ['/dealer/profile', '/dealer/settings'];

  /**
   * A SUSPENDED dealer still has customers holding its cars. Returns must be recordable, so the
   * bookings screens stay open; approving and rejecting are hidden there by the booking screen
   * itself, and the API refuses them regardless.
   */
  private readonly openWhileSuspended = ['/dealer/bookings', '/dealer/disputes'];

  protected readonly locked = computed(() => {
    const dealer = this.me.value();
    if (!dealer || dealer.canTrade) return false;
    const open = dealer.isSuspended
      ? [...this.openWhileLocked, ...this.openWhileSuspended]
      : this.openWhileLocked;
    return !open.some((path) => this.url().startsWith(path));
  });

  protected readonly copy = computed<LockedCopy | null>(() => {
    const dealer = this.me.value();
    if (!dealer) return null;
    const name = dealer.businessName;

    if (dealer.isSuspended) {
      return {
        icon: 'pause-circle',
        tone: 'warn',
        badge: 'Suspended',
        title: 'Your dealer account is suspended',
        body: `${name} cannot take new bookings or change fleet data while suspended. Rentals already under way continue, and your customers were notified by the platform.`,
        listTitle: 'What still works',
        points: [
          {
            icon: 'check',
            tone: 'ok',
            text: 'Cars already out on rental can still be handed back and recorded.',
          },
          {
            icon: 'x',
            tone: 'bad',
            text: 'New booking requests, fleet edits and staff changes are blocked.',
          },
          {
            icon: 'chat-circle',
            tone: 'accent',
            text: dealer.suspensionReason
              ? `Reason on file: ${dealer.suspensionReason}`
              : 'No reason was recorded with the suspension.',
          },
        ],
      };
    }

    switch (dealer.verificationStatus) {
      case 'Rejected':
        return {
          icon: 'x-circle',
          tone: 'bad',
          badge: 'Rejected',
          title: 'Your dealer application was rejected',
          body: `${name} is not approved to operate on the platform, so the dealer console is unavailable. You can correct your details and submit the application again.`,
          listTitle: 'Reviewer notes',
          points: [
            {
              icon: 'file-x',
              tone: 'bad',
              text: dealer.reviewNote ?? 'No note was recorded with the decision.',
            },
            {
              icon: 'clock-counter-clockwise',
              tone: 'accent',
              text: 'You may update your dealer page and reapply at any time.',
            },
          ],
        };
      case 'ClarificationNeeded':
        return {
          icon: 'question',
          tone: 'warn',
          badge: 'Clarification needed',
          title: 'The platform needs something from you',
          body: `An administrator reviewed ${name}'s application and sent it back. Fix what they asked for on your dealer page, then resubmit.`,
          listTitle: 'What they asked',
          points: [
            {
              icon: 'chat-circle',
              tone: 'warn',
              text: dealer.reviewNote ?? 'No note was recorded with the request.',
            },
            {
              icon: 'check',
              tone: 'ok',
              text: 'Your dealer page stays editable while this is open.',
            },
          ],
        };
      default:
        return {
          icon: 'clock',
          tone: 'accent',
          badge: 'Pending review',
          title: 'Your application is being reviewed',
          body: `${name} was submitted and is waiting for the platform's licence check. Nothing else is needed from you right now; you will be able to list cars the moment it is approved.`,
          listTitle: 'What happens next',
          points: [
            {
              icon: 'clock',
              tone: 'accent',
              text: `A decision is due by ${new Date(dealer.reviewDueAt).toLocaleString('en-GB')}.`,
            },
            {
              icon: 'storefront',
              tone: 'ok',
              text: 'You can prepare your dealer page in the meantime.',
            },
          ],
        };
    }
  });
}
