import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { I18nService } from '../../core/i18n/i18n.service';
import {
  ProblemSnapshot,
  fieldMessageFor,
  serverSentence,
  snapshotProblem,
} from '../../core/i18n/problem';
import { CustomerPageView } from '../../core/models/dealer-console.api';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { DealerConsoleService } from '../../core/services/dealer-console.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { Language } from '../../core/i18n/language';
import { CONTENT_LANGUAGES, boxErrorNames, boxKey } from '../../core/i18n/bilingual-content';
import {
  CustomerPageAudiencePreview,
  CustomerPageDraft,
  customerPageDirty,
  customerPageDraft,
  customerPagePreviews,
  customerPageRequest,
  customerPageRows,
  unknownCustomerPageSections,
} from './customer-page.presenter';

/**
 * The customer page: what this office tells customers in its own words, and which of it it shows.
 *
 * Two things are deliberately NOT on this screen. The platform's rules — cancellation, payment, the
 * deposit, the documents a renter needs — are Khadra's and are stated on every booking quote; fuel,
 * mileage and the security deposit are the car's, set per vehicle in Fleet. An office writing its own
 * version of either would be writing a promise nothing enforces, so the page says where each lives
 * instead, without repeating a single figure.
 *
 * The toggles are built from the server's own list of sections, and the preview is the server's own
 * answer to "what does a customer see" — see `customer-page.presenter.ts`, which holds both rules and
 * is where their spec reads them.
 *
 * Owner-only to save. Staff read it, because they answer customers' questions about what is on it.
 */
@Component({
  selector: 'kh-dealer-customer-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dealer-customer-page.component.html',
  imports: [IconComponent, RouterLink],
})
export class DealerCustomerPageComponent {
  private readonly i18n = inject(I18nService);
  protected readonly t = inject(I18nService).t;
  private readonly service = inject(DealerConsoleService);
  private readonly ui = inject(ConsoleUiService);

  protected readonly resource = this.service.customerPage;
  /** Guarded: `value()` throws in the error state, so nothing reads the resource directly. */
  private readonly data = loaded(this.resource);
  protected readonly page = computed(() => this.data() ?? null);

  private readonly dealer = loaded(this.service.me);
  protected readonly canEdit = computed(() => !!this.dealer()?.isOwner);

  /** The draft, keyed by the wire name of each section. */
  protected readonly texts = signal<CustomerPageDraft>({});
  /** Section names the owner has switched off, by their wire names. */
  protected readonly hidden = signal<ReadonlySet<string>>(new Set<string>());
  protected readonly busy = signal(false);
  /** What the last failed save said, as facts; the words are chosen at render time. */
  protected readonly problem = signal<ProblemSnapshot | null>(null);

  protected readonly problemText = computed(() => {
    const p = this.problem();
    if (!p) return null;
    return (
      serverSentence(p, this.i18n.lang(), this.t) ??
      this.t('dealerCustomerPage.serviceDidNotRespond')
    );
  });

  constructor() {
    // Seed the draft from the server each time it answers; the form is a draft of THAT answer.
    effect(() => {
      const page = this.page();
      if (page) this.reset(page);
    });
  }

  protected readonly rows = computed(() => {
    const page = this.page();
    return page ? customerPageRows(page, this.t) : [];
  });

  protected readonly maxLength = computed(() => this.page()?.maxTextLength ?? 0);

  protected text(field: string): string {
    return this.texts()[field] ?? '';
  }

  protected setText(field: string, event: Event): void {
    const value = (event.target as HTMLTextAreaElement).value;
    this.texts.update((all) => ({ ...all, [field]: value }));
  }

  protected isHidden(name: string): boolean {
    return this.hidden().has(name);
  }

  protected toggle(name: string): void {
    this.hidden.update((all) => {
      const next = new Set(all);
      if (!next.delete(name)) next.add(name);
      return next;
    });
  }

  /** How much of the allowance a box has used. The ceiling is the server's, never a literal. */
  protected used(field: string): number {
    return this.text(field).length;
  }

  /**
   * Refused rather than cut, the same way the server refuses it.
   *
   * A condition trimmed mid-sentence says something its author did not write, and an office would
   * have no way of knowing the page was saying it.
   */
  protected tooLong(field: string): boolean {
    const max = this.maxLength();
    return max > 0 && this.used(field) > max;
  }

  protected readonly anyTooLong = computed(() =>
    this.rows().some((row) => this.tooLong(row.field)),
  );

  /**
   * The server has a section this build cannot show, so a save — which replaces the whole page —
   * would erase what the office wrote in it. Saving is refused, and the page says why.
   */
  protected readonly staleConsole = computed(() => {
    const page = this.page();
    return !!page && unknownCustomerPageSections(page).length > 0;
  });

  protected readonly dirty = computed(() => {
    const page = this.page();
    return !!page && customerPageDirty(page, this.texts(), this.hidden());
  });

  /**
   * BOTH previews, side by side, and neither of them the console's own working-out.
   *
   * An owner needs to see what each audience gets — that is the whole point of writing two languages
   * — and a single preview would have followed whatever `Accept-Language` their own browser sends,
   * flipping between the two with no way to ask for the other.
   */
  protected readonly previews = computed<readonly CustomerPageAudiencePreview[]>(() =>
    customerPagePreviews(this.page(), this.t),
  );

  /** Whether a customer would see anything at all of what this office has written. */
  protected readonly previewEmpty = computed(() =>
    this.previews().every((preview) => preview.rows.length === 0),
  );

  /** The two boxes each section is edited through, in the order they are drawn. */
  protected readonly languages = CONTENT_LANGUAGES;

  protected boxKeyFor(field: string, language: Language): string {
    return boxKey(field, language);
  }

  /** The heading over one box: the language, named in the reader's own language. */
  protected languageLabel(language: Language): string {
    return this.i18n.languageName(language);
  }

  protected readonly failure = computed(() => {
    const error = this.resource.error() as { status?: number } | undefined;
    return error ? this.t('dealerCustomerPage.couldntLoadNothingChanged') : null;
  });

  /**
   * What the server said about ONE box, under every name that box can arrive under.
   *
   * Keyed by the section's field and its language rather than by the draft key: the draft key is the
   * console's own, and the names a refusal uses are the server's. See `boxErrorNames`.
   */
  protected boxError(field: string, language: Language): string | null {
    return fieldMessageFor(this.problem(), boxErrorNames(field, language), this.i18n.lang(), this.t);
  }

  protected reset(page: CustomerPageView): void {
    this.texts.set(customerPageDraft(page));
    this.hidden.set(new Set(page.hiddenSections));
    this.problem.set(null);
  }

  protected discard(): void {
    const page = this.page();
    if (page) this.reset(page);
  }

  protected async save(): Promise<void> {
    // Checked here as well as on the button: a disabled button is one keyboard shortcut or one
    // template change away from being enabled, and this is the line that would erase data.
    if (this.busy() || !this.canEdit() || this.anyTooLong() || this.staleConsole()) return;
    this.busy.set(true);
    this.problem.set(null);
    try {
      await this.service.updateCustomerPage(customerPageRequest(this.texts(), this.hidden()));
      // Re-read rather than trust the response: the preview beside the form has to be the server's
      // own answer about what a customer sees, and the reload is what keeps it that.
      this.resource.reload();
      this.ui.showToast(
        this.t('dealerCustomerPage.savedTitle'),
        this.t('dealerCustomerPage.savedBody'),
      );
    } catch (error) {
      this.problem.set(snapshotProblem(error));
    } finally {
      this.busy.set(false);
    }
  }
}
