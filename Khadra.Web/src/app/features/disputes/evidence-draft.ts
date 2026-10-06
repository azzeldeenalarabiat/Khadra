import { signal } from '@angular/core';
import { UploadLimits, uploadProblem, uploadTypeNames } from '../../core/config/upload-limits';
import { TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';

/** A file this page refused before sending anything, and why. */
export interface RefusedEvidence {
  readonly kind: 'wrongType' | 'tooLarge';
  readonly name: string;
}

/**
 * Files chosen as evidence for a dispute (Wave 3 C4), checked against the limits `/app-config` publishes before any
 * upload starts. The server judges every file again; this only spares the customer a pointless wait.
 */
export class EvidenceDraft {
  readonly files = signal<readonly File[]>([]);
  readonly refused = signal<RefusedEvidence | null>(null);

  /** Adds the chosen files, unless one of them would be refused: then nothing is added, and that one is named. */
  choose(chosen: readonly File[], limits: UploadLimits | null): void {
    this.refused.set(null);
    for (const file of chosen) {
      const problem = uploadProblem(file, limits);
      if (problem) {
        this.refused.set({ kind: problem, name: file.name });
        return;
      }
    }
    this.files.update((current) => [...current, ...chosen]);
  }

  remove(index: number): void {
    this.files.update((current) => current.filter((_, at) => at !== index));
  }

  clear(): void {
    this.files.set([]);
    this.refused.set(null);
  }
}

/** Why a chosen file was refused, in the reader's language, with the published limits it broke. */
export function evidenceRefusalText(
  refused: RefusedEvidence | null,
  limits: UploadLimits | null,
  t: (key: TranslationKey, params?: MessageParams) => string,
  megabytes: (bytes: number) => string,
): string | null {
  if (!refused || !limits) return null;
  return refused.kind === 'wrongType'
    ? t('documents.wrongType', { types: uploadTypeNames(limits.allowedContentTypes) })
    : t('documents.tooLarge', { size: megabytes(limits.maximumSizeBytes) });
}
