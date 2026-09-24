import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { PublicGalleryCard } from '../../core/api/catalogue.api';
import { Paged } from '../../core/api/common.api';
import { LookupsService } from '../../core/api/lookups.service';
import { snapshotProblem } from '../../core/http/problem';
import { I18nService } from '../../core/i18n/i18n.service';
import { SeoService } from '../../core/seo/seo.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { OfficeCardComponent } from '../../shared/office-card/office-card.component';
import { StatePanelComponent } from '../../shared/state/state-panel.component';
import { httpData } from '../../core/http/http-data';
import { injectResponseStatus } from '../../core/http/server-context';
import {
  DIRECTORY_TEXT_MAX,
  DirectorySearch,
  EMPTY_DIRECTORY_SEARCH,
  directoryFromParams,
  directoryToApi,
  directoryToParams,
  isNarrowed,
} from './directory-search';

const PAGE_SIZE = 18;
/** How long typing pauses before the name search runs. Interaction timing, not a business rule. */
const TYPING_PAUSE_MS = 350;

/**
 * The directory of rental offices, searched on its own terms: the office's name, its city, and whether
 * it delivers. Every filter is in the URL and every one is answered by `GET /galleries`, so the count,
 * the pages and the order are the server's.
 */
@Component({
  selector: 'kh-dealers',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, OfficeCardComponent, StatePanelComponent, IconComponent],
  templateUrl: './dealers.component.html',
})
export class DealersComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly lookups = inject(LookupsService);
  private readonly router = inject(Router);

  protected readonly textMax = DIRECTORY_TEXT_MAX;

  private readonly params = toSignal(inject(ActivatedRoute).queryParams, { initialValue: {} as Record<string, unknown> });
  protected readonly search = computed(() => directoryFromParams(this.params()));
  protected readonly narrowed = computed(() => isNarrowed(this.search()) || this.search().city !== null);

  /** What is in the box, which runs ahead of the URL while the visitor is still typing. */
  protected readonly typed = signal<string | null>(null);
  protected readonly boxText = computed(() => this.typed() ?? this.search().text);

  protected readonly offices = httpData<Paged<PublicGalleryCard>>(() => ({
    url: '/api/v1/galleries',
    params: directoryToApi(this.search(), PAGE_SIZE),
  }));
  protected readonly problem = computed(() => (this.offices.error() ? snapshotProblem(this.offices.error()) : null));
  protected readonly skeletons = Array.from({ length: 6 }, (_, index) => index);

  private pause: ReturnType<typeof setTimeout> | undefined;
  /** The text this page itself last put in the URL, to tell its own navigations from anyone else's. */
  private pushedText: string | null = null;
  /** True while this page's own navigation is in flight: the URL is about to catch up, not change hands. */
  private navigating = false;
  /** The URL text the box last saw, so typing (which leaves the URL alone) is never mistaken for a navigation. */
  private seenText: string | null = null;

  constructor() {
    const seo = inject(SeoService);
    const setStatus = injectResponseStatus();
    effect(() => {
      if (this.problem()) setStatus(503);
    });
    effect(() => {
      const search = this.search();
      seo.set({
        title: this.i18n.t('seo.offices.title'),
        description: this.i18n.t('seo.offices.description'),
        path: search.city ? `dealers?city=${search.city}` : 'dealers',
        // A city's directory is a page worth finding; a name search or a later page is not.
        noindex: search.page > 1 || isNarrowed(search),
      });
    });
    // The box hands back to the URL once the URL has caught up with it — never mid-word, so the caret
    // does not jump while the visitor is still typing ("arabiat " with its trailing space stays as typed
    // while the URL holds "arabiat"). A URL this page did NOT write — Back, a link, the empty state's
    // clear — always wins, so the box never shows one search while the list shows another.
    effect(() => {
      const text = this.search().text;
      const typed = this.typed();
      const urlMoved = this.seenText !== null && text !== this.seenText;
      this.seenText = text;
      if (typed === null) return;
      if (typed === text || (urlMoved && !this.navigating && text !== this.pushedText)) this.typed.set(null);
    });
    inject(DestroyRef).onDestroy(() => clearTimeout(this.pause));
  }

  protected onType(value: string): void {
    this.typed.set(value);
    clearTimeout(this.pause);
    this.pause = setTimeout(() => this.apply({ text: value.trim().slice(0, DIRECTORY_TEXT_MAX) }, true), TYPING_PAUSE_MS);
  }

  protected submitText(event: Event): void {
    event.preventDefault();
    clearTimeout(this.pause);
    this.apply({ text: this.boxText().trim().slice(0, DIRECTORY_TEXT_MAX) });
  }

  protected setCity(value: string): void {
    this.apply({ city: value || null });
  }

  protected setDelivery(on: boolean): void {
    this.apply({ delivery: on });
  }

  protected clear(): void {
    clearTimeout(this.pause);
    this.pushedText = '';
    this.typed.set(null);
    void this.router.navigate([], { queryParams: {} });
  }

  protected pageParams(page: number): Record<string, string> {
    return directoryToParams({ ...this.search(), page });
  }

  /** Any change of filter starts again at page one. Typing replaces the history entry, it does not stack. */
  private apply(change: Partial<DirectorySearch>, replaceUrl = false): void {
    const next = { ...EMPTY_DIRECTORY_SEARCH, ...this.search(), ...change, page: 1 };
    this.pushedText = next.text;
    this.navigating = true;
    void this.router
      .navigate([], { queryParams: directoryToParams(next), replaceUrl })
      .finally(() => (this.navigating = false));
  }
}
