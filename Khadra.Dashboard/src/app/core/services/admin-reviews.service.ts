import { HttpClient, httpResource } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PagedResult } from '../models/bookings.api';
import { ModerationReview, ReviewDirection } from '../models/reviews.api';

/**
 * Review moderation (pre-launch item 81), through `/api/v1/admin/reviews`: both directions, filtered on the server,
 * and the two decisions an administrator can make about one.
 */
@Injectable({ providedIn: 'root' })
export class AdminReviewsService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/admin/reviews';

  readonly visibility = signal<'hidden' | 'visible' | null>(null);
  readonly direction = signal<ReviewDirection | null>(null);
  readonly rating = signal<number | null>(null);
  readonly search = signal('');
  readonly page = signal(1);
  readonly pageSize = 20;

  readonly list = httpResource<PagedResult<ModerationReview>>(() => {
    const params: Record<string, string | number> = { page: this.page(), pageSize: this.pageSize };
    const visibility = this.visibility();
    if (visibility) params['visibility'] = visibility;
    const direction = this.direction();
    if (direction) params['direction'] = direction;
    const rating = this.rating();
    if (rating !== null) params['rating'] = rating;
    const search = this.search().trim();
    if (search) params['search'] = search;
    return { url: this.base, params };
  });

  private async act(
    reviewId: string,
    action: 'hide' | 'restore',
    body: unknown = {},
  ): Promise<ModerationReview> {
    const token = await firstValueFrom(this.http.get<{ requestToken: string }>('/bff/antiforgery'));
    return firstValueFrom(
      this.http.post<ModerationReview>(
        `${this.base}/${encodeURIComponent(reviewId)}/${action}`,
        body,
        {
          headers: { 'X-XSRF-TOKEN': token.requestToken },
        },
      ),
    );
  }

  hide = (reviewId: string, reason: string) => this.act(reviewId, 'hide', { reason });

  restore = (reviewId: string) => this.act(reviewId, 'restore');

  refresh(): void {
    this.list.reload();
  }
}
