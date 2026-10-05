/**
 * The legal texts as the API sends them (Wave 2 G1): Khadra.Application/Legal/Dtos and the `legal` block of
 * `/app-config`. Hand-written, like every model here.
 */

/** `Terms` or `Privacy` on the wire; a name this build does not know is shown spelled out, never guessed. */
export type LegalKindName = 'Terms' | 'Privacy';

/** The kinds this console publishes, in the order it shows them. */
export const LEGAL_KINDS: readonly LegalKindName[] = ['Terms', 'Privacy'];

export interface LegalTexts {
  readonly en: string;
  readonly ar: string;
}

/** `Current` for the version in force, `Superseded` for every one a later version replaced. */
export type LegalVersionState = 'Current' | 'Superseded';

export interface LegalDocumentVersionSummary {
  readonly versionId: string;
  readonly kind: string;
  readonly versionLabel: string;
  readonly effectiveFrom: string;
  readonly publishedAt: string;
  readonly publishedByAdminId: string;
  /** Read live; null once the publisher's account no longer resolves. */
  readonly publishedByName: string | null;
  readonly state: LegalVersionState | string;
}

/** One version in full: its Markdown as published, the HTML the public page shows, and each body's SHA-256. */
export interface LegalDocumentVersion {
  readonly summary: LegalDocumentVersionSummary;
  readonly body: LegalTexts;
  readonly html: LegalTexts;
  readonly sha256: LegalTexts;
}

/** What publishing would publish. Nothing has been written. */
export interface LegalDocumentPreview {
  readonly kind: string;
  /** The label as it would be stored: trimmed. */
  readonly versionLabel: string;
  readonly html: LegalTexts;
  /** The version in force now, which this one would replace; null for the first. */
  readonly replaces: LegalDocumentVersionSummary | null;
}

/** The body of a preview and of a publish. */
export interface LegalTextsRequest {
  readonly kind: LegalKindName;
  readonly versionLabel: string;
  readonly bodyEn: string;
  readonly bodyAr: string;
}

/** The `legal` block of `/app-config`: each text in force, with its public page. */
export interface LegalConfig {
  readonly documents: readonly LegalConfigDocument[];
}

export interface LegalConfigDocument {
  readonly kind: string;
  readonly slug: string;
  readonly versionId: string;
  readonly versionLabel: string;
  readonly effectiveFrom: string;
  /** Null while the API has no website address: no link is invented then. */
  readonly pageUrls: LegalTexts | null;
}
