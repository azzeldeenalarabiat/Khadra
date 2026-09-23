import { HttpClient, httpResource } from '@angular/common/http';
import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { CustomerDocument, CustomerDocuments } from '../../core/api/documents.api';
import { AppConfigService } from '../../core/config/app-config.service';
import { ProblemSnapshot, snapshotProblem } from '../../core/http/problem';
import { problemText } from '../../core/http/problem-text';
import { TranslationKey } from '../../core/i18n/en';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { StatePanelComponent } from '../../shared/state/state-panel.component';

/** The order documents are listed in. Which of the identity documents applies is the server's call. */
const ORDER = ['DrivingLicenceFront', 'DrivingLicenceBack', 'NationalId', 'Passport'];

interface Slot {
  readonly type: string;
  readonly document: CustomerDocument | null;
}

/**
 * The customer's identity documents — the same records the app uploads to. The slots shown are the
 * documents on file plus the ones the server says are still missing; the file types and the size limit
 * are the ones `/app-config` publishes, checked here only to save a pointless upload. The server judges
 * every file again.
 */
@Component({
  selector: 'kh-documents',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [IconComponent, StatePanelComponent],
  templateUrl: './documents.component.html',
})
export class DocumentsComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly format = inject(FormatService);
  private readonly http = inject(HttpClient);
  private readonly appConfig = inject(AppConfigService);
  private readonly document = inject(DOCUMENT);

  protected readonly documents = httpResource<CustomerDocuments>(() => '/api/v1/customers/me/documents');
  protected readonly loadProblem = computed(() => (this.documents.error() ? snapshotProblem(this.documents.error()) : null));

  protected readonly limits = computed(() => this.appConfig.config()?.documents ?? null);
  protected readonly accept = computed(() => (this.limits()?.allowedContentTypes ?? []).join(','));
  protected readonly limitsText = computed(() => {
    const limits = this.limits();
    if (!limits) return '';
    return this.i18n.t('documents.limits', { types: this.typeNames(limits.allowedContentTypes), size: this.size(limits.maximumSizeBytes) });
  });

  protected readonly slots = computed<Slot[]>(() => {
    const value = this.documents.value();
    if (!value) return [];
    const types = new Set([...value.documents.map((doc) => doc.type), ...value.missing]);
    return [...types]
      .sort((a, b) => (ORDER.indexOf(a) === -1 ? 99 : ORDER.indexOf(a)) - (ORDER.indexOf(b) === -1 ? 99 : ORDER.indexOf(b)))
      .map((type) => ({ type, document: value.documents.find((doc) => doc.type === type) ?? null }));
  });

  protected readonly missingText = computed(() =>
    (this.documents.value()?.missing ?? []).map((type) => this.typeLabel(type)).join(this.i18n.isArabic() ? '، ' : ', '),
  );

  protected readonly uploading = signal<string | null>(null);
  protected readonly problems = signal<Record<string, string>>({});

  protected typeLabel(type: string): string {
    return ORDER.includes(type) ? this.i18n.t(`documents.type.${type}` as TranslationKey) : type;
  }

  protected statusLabel(status: string): string {
    return ['PendingReview', 'Verified', 'Rejected'].includes(status) ? this.i18n.t(`documents.status.${status}` as TranslationKey) : status;
  }

  protected statusTone(status: string): string {
    return status === 'Verified' ? 'badge--ok' : status === 'Rejected' ? 'badge--bad' : '';
  }

  protected async upload(type: string, event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    const limits = this.limits();
    this.setProblem(type, null);
    if (limits && !limits.allowedContentTypes.includes(file.type)) {
      this.setProblem(type, this.i18n.t('documents.wrongType', { types: this.typeNames(limits.allowedContentTypes) }));
      return;
    }
    if (limits && file.size > limits.maximumSizeBytes) {
      this.setProblem(type, this.i18n.t('documents.tooLarge', { size: this.size(limits.maximumSizeBytes) }));
      return;
    }

    const form = new FormData();
    form.append('type', type);
    form.append('file', file, file.name);
    this.uploading.set(type);
    try {
      await firstValueFrom(this.http.post('/api/v1/customers/me/documents', form));
      this.documents.reload();
    } catch (error) {
      this.setProblem(type, this.word(snapshotProblem(error)));
    } finally {
      this.uploading.set(null);
    }
  }

  protected async view(doc: CustomerDocument): Promise<void> {
    // Opened first, filled after: a window opened after an await is treated as a pop-up and blocked.
    // Not with 'noopener' in the features: that makes open() return null. The opener is cut by hand.
    const opened = this.document.defaultView?.open('', '_blank') ?? null;
    if (opened) opened.opener = null;
    try {
      const link = await firstValueFrom(this.http.get<{ url: string }>(`/api/v1/customers/me/documents/${doc.documentId}/link`));
      if (!link.url.startsWith('/api/v1/documents/')) throw { status: 0 };
      if (opened) opened.location.href = link.url;
      else this.document.location.assign(link.url);
    } catch (error) {
      opened?.close();
      this.setProblem(doc.type, this.word(snapshotProblem(error)));
    }
  }

  private word(problem: ProblemSnapshot): string {
    return problemText(problem, this.i18n.t.bind(this.i18n), this.i18n.language(), this.appConfig.config());
  }

  private setProblem(type: string, text: string | null): void {
    this.problems.update((current) => {
      const next = { ...current };
      if (text) next[type] = text;
      else delete next[type];
      return next;
    });
  }

  private typeNames(types: readonly string[]): string {
    return types.map((type) => (type.split('/')[1] ?? type).toUpperCase()).join(', ');
  }

  private size(bytes: number): string {
    return `${this.format.number(bytes / (1024 * 1024), 0)} MB`;
  }
}
