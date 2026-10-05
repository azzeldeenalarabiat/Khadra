/**
 * The legal texts (Wave 2 G1). Hand-written to the API's `PublicLegalDocumentDto` and `LegalConfigDto`
 * (Khadra.Application/Legal/Dtos, Khadra.Application/PlatformSettings/AppConfig).
 */

/** A legal text this website has a page for, by the path segment the API and the page share. */
export type LegalSlug = 'terms' | 'privacy';

/** `GET /api/v1/legal-documents/{slug}/current`: the version in force. */
export interface PublicLegalDocument {
  readonly kind: string;
  readonly versionId: string;
  readonly versionLabel: string;
  readonly effectiveFrom: string;
  readonly publishedAt: string;
  /**
   * Rendered by the server from a checked Markdown subset, with no direction of its own: the element
   * holding it sets `dir` and `lang`.
   */
  readonly html: { readonly en: string; readonly ar: string };
}

/** The `legal` block of `/app-config`: each text in force. A text with nothing in force is absent. */
export interface LegalConfig {
  readonly documents: readonly LegalConfigDocument[];
}

export interface LegalConfigDocument {
  readonly kind: string;
  readonly slug: string;
  readonly versionId: string;
  readonly versionLabel: string;
  readonly effectiveFrom: string;
  /** Null while the API has no website address to build them from. */
  readonly pageUrls: { readonly en: string; readonly ar: string } | null;
}
