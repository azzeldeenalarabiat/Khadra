import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Tone } from '../../core/models/console.models';
import {
  ModerationReview,
  REVIEW_HIDE_REASONS,
  ReviewDirection,
} from '../../core/models/reviews.api';
import { AdminReviewsService } from '../../core/services/admin-reviews.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { I18nService } from '../../core/i18n/i18n.service';
import { FormatService } from '../../core/i18n/format.service';
import { TranslationKey } from '../../core/i18n/en';

type Visibility = 'all' | 'visible' | 'hidden';

const VISIBILITY_CHIPS: readonly { readonly key: Visibility; readonly label: TranslationKey }[] = [
  { key: 'all', label: 'common.all' },
  { key: 'visible', label: 'reviews.filter.shown' },
  { key: 'hidden', label: 'reviews.filter.hidden' },
];

const RATINGS = [5, 4, 3, 2, 1] as const;

/**
 * Review moderation (spec 3.2; pre-launch item 81), as the design's Reviews screen draws it: both directions, the
 * whole comment, and the one decision an administrator can make — hide under a policy reason, or restore.
 *
 * A rating is never edited. Hiding a customer's review of an office removes its words and keeps its score, so an
 * office cannot erase a bad rating by reporting it; hiding an office's rating of a customer removes the score from
 * their reputation, because the score is all it has. Each dialog says which, for the review in front of it.
 */
@Component({
  selector: 'kh-reviews',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './reviews.component.html',
  imports: [RouterLink, IconComponent],
})
export class ReviewsComponent {
  private readonly i18n = inject(I18nService);
  protected readonly t = this.i18n.t;
  protected readonly formats = inject(FormatService);
  private readonly service = inject(AdminReviewsService);
  private readonly ui = inject(ConsoleUiService);

  protected readonly resource = this.service.list;
  protected readonly search = this.service.search;
  protected readonly direction = this.service.direction;
  protected readonly rating = this.service.rating;
  protected readonly ratings = RATINGS;
  private readonly loadedPage = loaded(this.resource);

  protected readonly chips = computed(() =>
    VISIBILITY_CHIPS.map((chip) => ({ key: chip.key, label: this.t(chip.label) })),
  );

  protected readonly activeChip = computed<Visibility>(() => this.service.visibility() ?? 'all');

  protected readonly rows = computed(() => this.loadedPage()?.items ?? []);
  protected readonly totalPages = computed(() => this.loadedPage()?.totalPages ?? 1);
  protected readonly page = computed(() => this.loadedPage()?.page ?? 1);

  protected readonly summary = computed(() => {
    const page = this.loadedPage();
    if (!page) return '';
    const from = page.totalCount === 0 ? 0 : (page.page - 1) * page.pageSize + 1;
    const to = Math.min(page.page * page.pageSize, page.totalCount);
    return this.t('reviews.showingRange', { from, to, count: page.totalCount });
  });

  protected readonly failure = computed(() => {
    const error = this.resource.error() as { status?: number } | undefined;
    if (!error) return null;
    if (error.status === 403) return this.t('reviews.forAdministrators');
    return this.t('reviews.couldNotLoad');
  });

  protected selectChip(key: Visibility): void {
    this.service.visibility.set(key === 'all' ? null : key);
    this.service.page.set(1);
  }

  protected setDirection(value: string): void {
    this.service.direction.set(
      value === 'CustomerRatesDealer' || value === 'DealerRatesCustomer' ? value : null,
    );
    this.service.page.set(1);
  }

  protected setRating(value: string): void {
    const rating = Number(value);
    this.service.rating.set(Number.isInteger(rating) && rating >= 1 && rating <= 5 ? rating : null);
    this.service.page.set(1);
  }

  protected setSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
    this.service.page.set(1);
  }

  protected goTo(page: number): void {
    if (page >= 1 && page <= this.totalPages()) this.service.page.set(page);
  }

  protected reload(): void {
    this.resource.reload();
  }

  /** A customer's review of an office: public, and its score survives hiding. */
  protected isOfficeReview(review: ModerationReview): boolean {
    return review.direction === 'CustomerRatesDealer';
  }

  protected directionLabel(direction: ReviewDirection | string): string {
    return this.i18n.enumLabel('reviewDirection', direction);
  }

  protected reasonLabel(reason: string): string {
    return this.i18n.enumLabel('reviewHideReason', reason);
  }

  /** Where the reviewer's name links: a customer's profile, or an office's application. */
  protected reviewerLink(review: ModerationReview): string[] {
    return this.isOfficeReview(review)
      ? ['/customers', review.reviewerId]
      : ['/dealers', review.reviewerId];
  }

  protected subjectLink(review: ModerationReview): string[] {
    return this.isOfficeReview(review)
      ? ['/dealers', review.subjectId]
      : ['/customers', review.subjectId];
  }

  protected tone(review: ModerationReview): Tone {
    if (review.isHidden) return 'bad';
    return review.isPublished ? 'ok' : 'dim';
  }

  protected statusLabel(review: ModerationReview): string {
    if (review.isHidden) return this.t('reviews.status.hidden');
    return this.t(
      review.isPublished ? 'reviews.status.published' : 'reviews.status.awaitingReveal',
    );
  }

  protected hide(review: ModerationReview): void {
    const office = this.isOfficeReview(review);
    const done = {
      title: this.t('reviews.hidden'),
      body: this.t(office ? 'reviews.hiddenBodyOffice' : 'reviews.hiddenBodyCustomer'),
      tone: 'warn' as const,
    };
    this.ui.openAction(
      {
        icon: 'eye-slash',
        tone: 'warn',
        title: this.t('reviews.hideTitle'),
        body: this.t(office ? 'reviews.hideBodyOffice' : 'reviews.hideBodyCustomer'),
        confirm: this.t('reviews.hideConfirm'),
        fields: [
          {
            // A machine name, read back as values['reason']: the code the server stores, never its words.
            name: 'reason',
            label: this.t('reviews.policyReason'),
            type: 'select',
            options: REVIEW_HIDE_REASONS.map((code) => ({
              value: code,
              label: this.reasonLabel(code),
            })),
          },
        ],
        result: done,
      },
      async (values) => {
        try {
          await this.service.hide(review.reviewId, values['reason'] ?? '');
        } finally {
          this.service.refresh();
        }
      },
      done,
    );
  }

  protected restore(review: ModerationReview): void {
    const office = this.isOfficeReview(review);
    const done = {
      title: this.t('reviews.restored'),
      body: this.t(office ? 'reviews.restoredBodyOffice' : 'reviews.restoredBodyCustomer'),
    };
    this.ui.openAction(
      {
        icon: 'eye',
        tone: 'accent',
        title: this.t('reviews.restoreTitle'),
        body: this.t(office ? 'reviews.restoreBodyOffice' : 'reviews.restoreBodyCustomer'),
        confirm: this.t('reviews.restoreConfirm'),
        result: done,
      },
      async () => {
        try {
          await this.service.restore(review.reviewId);
        } finally {
          this.service.refresh();
        }
      },
      done,
    );
  }

  protected when(iso: string): string {
    return this.formats.date(iso);
  }
}
