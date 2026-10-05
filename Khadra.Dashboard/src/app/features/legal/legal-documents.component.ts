import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { ProblemSnapshot, serverSentence, snapshotProblem } from '../../core/i18n/problem';
import {
  LEGAL_KINDS,
  LegalDocumentPreview,
  LegalDocumentVersionSummary,
  LegalKindName,
  LegalTextsRequest,
} from '../../core/models/legal.api';
import { AdminLegalService } from '../../core/services/admin-legal.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import {
  legalKindLabel,
  legalRefusal,
  legalStateLabel,
  legalStateTone,
  previewKey,
} from './legal-documents.presenter';

/** The audit trail's name for a published version (`AuditEntityType.LegalDocument`). */
const LEGAL_DOCUMENT_ENTITY = 'LegalDocument';

/**
 * The legal texts (Wave 2 G1; pre-launch item 224): the Terms of Service and the Privacy notice, every version ever
 * published, and the one way a new one is published.
 *
 * Publishing is permanent, so the screen insists on the order: write, preview, publish. The preview is rendered by
 * the server, by the same renderer the public page uses. Publishing needs a preview of exactly what is in the form;
 * a change after it asks for a new one. The confirmation says plainly that the version is in force at once and can
 * never be changed. Nothing here is a fixture: every text, label, date and name is the server's.
 */
@Component({
  selector: 'kh-legal-documents',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './legal-documents.component.html',
  imports: [IconComponent, RouterLink],
})
export class LegalDocumentsComponent {
  private readonly i18n = inject(I18nService);
  protected readonly t = this.i18n.t;
  private readonly formats = inject(FormatService);
  private readonly service = inject(AdminLegalService);
  private readonly ui = inject(ConsoleUiService);

  protected readonly kinds = LEGAL_KINDS;
  protected readonly versions = this.service.versions;
  private readonly page = loaded(this.service.versions);
  protected readonly rows = computed(() => this.page()?.items ?? []);
  protected readonly hasPrevious = computed(() => this.page()?.hasPrevious ?? false);
  protected readonly hasNext = computed(() => this.page()?.hasNext ?? false);
  protected readonly pageNumber = this.service.page;
  protected readonly totalPages = computed(() => this.page()?.totalPages ?? 0);

  protected readonly failure = computed(() => {
    const error = this.versions.error();
    if (!error) return null;
    const problem = snapshotProblem(error);
    return serverSentence(problem, this.i18n.lang(), this.t) ?? this.t('dealerDelivery.serviceDidNotRespond');
  });

  /**
   * The version in force of each document, read off the list: there is no scheduling, so a document's newest
   * version is the one in force, and the list is newest first.
   */
  protected readonly cards = computed(() =>
    this.kinds.map((kind) => ({
      kind,
      label: legalKindLabel(kind, this.t),
      current: this.rows().find((row) => row.kind === kind && row.state === 'Current') ?? null,
    })),
  );

  // ── The form ───────────────────────────────────────────────────────────────────────────────────

  protected readonly formOpen = signal(false);
  protected readonly kind = signal<LegalKindName>(LEGAL_KINDS[0]);
  protected readonly label = signal('');
  protected readonly bodyEn = signal('');
  protected readonly bodyAr = signal('');
  protected readonly previewing = signal(false);
  protected readonly preview = signal<LegalDocumentPreview | null>(null);
  private readonly previewedKey = signal<string | null>(null);
  /** A refused preview, held as the server's facts and worded in `problemText`. */
  protected readonly problem = signal<ProblemSnapshot | null>(null);

  protected readonly problemText = computed(() => {
    const problem = this.problem();
    return problem ? legalRefusal(problem, this.t, this.i18n.lang()) : null;
  });

  private readonly request = computed<LegalTextsRequest>(() => ({
    kind: this.kind(),
    versionLabel: this.label(),
    bodyEn: this.bodyEn(),
    bodyAr: this.bodyAr(),
  }));

  /** A preview of something other than what the form now holds: publishing it would publish words nobody saw. */
  protected readonly previewStale = computed(
    () => this.preview() !== null && this.previewedKey() !== previewKey(this.request()),
  );

  protected readonly canPublish = computed(
    () => this.preview() !== null && !this.previewStale() && !this.previewing(),
  );

  // ── One version in full ────────────────────────────────────────────────────────────────────────

  protected readonly viewing = this.service.viewing;
  protected readonly version = loaded(this.service.version);
  protected readonly versionFailure = computed(() => {
    const error = this.service.version.error();
    return error ? legalRefusal(snapshotProblem(error), this.t, this.i18n.lang()) : null;
  });

  protected startPublishing(kind: LegalKindName): void {
    this.kind.set(kind);
    this.formOpen.set(true);
    this.problem.set(null);
  }

  protected cancel(): void {
    this.formOpen.set(false);
    this.preview.set(null);
    this.previewedKey.set(null);
    this.problem.set(null);
  }

  protected chooseKind(kind: LegalKindName): void {
    this.kind.set(kind);
  }

  protected text(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement).value;
  }

  protected async renderPreview(): Promise<void> {
    if (this.previewing()) return;
    const request = this.request();
    this.previewing.set(true);
    this.problem.set(null);
    try {
      const preview = await this.service.preview(request);
      this.preview.set(preview);
      this.previewedKey.set(previewKey(request));
    } catch (error) {
      this.preview.set(null);
      this.previewedKey.set(null);
      this.problem.set(snapshotProblem(error));
    } finally {
      this.previewing.set(false);
    }
  }

  protected publish(): void {
    const preview = this.preview();
    if (!preview || !this.canPublish()) return;
    const request = this.request();
    const document = legalKindLabel(preview.kind, this.t);
    const params = { label: preview.versionLabel, document };
    this.ui.openAction(
      {
        icon: 'gavel',
        tone: 'warn',
        danger: true,
        title: this.t('legal.confirmTitle', params),
        body: this.t('legal.confirmBody'),
        confirm: this.t('legal.publish'),
        result: { title: this.t('legal.published'), body: this.t('legal.publishedBody', params), tone: 'ok' },
      },
      async () => {
        await this.service.publish(request);
        this.cancel();
        this.label.set('');
        this.bodyEn.set('');
        this.bodyAr.set('');
        this.service.page.set(1);
        this.service.refresh();
      },
      { title: this.t('legal.published'), body: this.t('legal.publishedBody', params) },
    );
  }

  protected view(row: LegalDocumentVersionSummary): void {
    this.viewing.set(row.versionId);
  }

  protected closeView(): void {
    this.viewing.set(null);
  }

  protected reload(): void {
    this.service.refresh();
  }

  protected previous(): void {
    if (this.hasPrevious()) this.service.page.update((page) => page - 1);
  }

  protected next(): void {
    if (this.hasNext()) this.service.page.update((page) => page + 1);
  }

  // ── Words ──────────────────────────────────────────────────────────────────────────────────────

  protected kindLabel(kind: string): string {
    return legalKindLabel(kind, this.t);
  }

  protected stateLabel(state: string): string {
    return legalStateLabel(state, this.t);
  }

  protected stateTone(state: string): string {
    return 's-' + legalStateTone(state);
  }

  protected publisher(row: LegalDocumentVersionSummary): string {
    return row.publishedByName ?? this.t('common.accountClosed');
  }

  protected when(iso: string): string {
    return this.formats.dayMonthTime(iso);
  }

  protected inForce(row: LegalDocumentVersionSummary): string {
    return this.t('legal.inForceSince', { label: row.versionLabel, date: this.formats.date(row.effectiveFrom) });
  }

  protected replaces(preview: LegalDocumentPreview): string {
    return preview.replaces
      ? this.t('legal.replaces', {
          label: preview.replaces.versionLabel,
          date: this.formats.date(preview.replaces.effectiveFrom),
        })
      : this.t('legal.firstVersion');
  }

  /** The audit log, filtered to this version: who published it, and when, as the append-only trail recorded it. */
  protected historyFor(row: LegalDocumentVersionSummary): Record<string, string> {
    return { entityType: LEGAL_DOCUMENT_ENTITY, entityId: row.versionId };
  }

  protected detailTitle(row: LegalDocumentVersionSummary): string {
    return this.t('legal.detailTitle', { document: legalKindLabel(row.kind, this.t), label: row.versionLabel });
  }
}
