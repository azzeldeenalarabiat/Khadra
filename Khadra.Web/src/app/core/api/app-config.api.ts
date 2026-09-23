/**
 * `GET /api/v1/app-config` — the platform's published facts. Hand-written to the API's
 * `AppConfigDto` (Khadra.Application/PlatformSettings/AppConfig/GetAppConfigQuery.cs), as the console
 * and the app both are; nothing here is generated, so a change there is a change here.
 */
export interface AppConfig {
  /** IANA zone every calendar answer on this platform is expressed in. */
  readonly timeZone: string;
  readonly currency: { readonly code: string; readonly minorUnits: number };
  /** Null: the owner has set no age limit. */
  readonly minimumRenterAge: number | null;
  readonly maxAdvanceBookingDays: number;
  readonly minimumBookingLeadTimeMinutes: number;
  readonly maxRentalDays: number;
  readonly paymentWindowHours: number;
  readonly documents: {
    readonly maximumSizeBytes: number;
    readonly allowedContentTypes: readonly string[];
  };
  readonly password: {
    readonly minimumLength: number;
    readonly maximumLength: number;
    readonly requiresLetter: boolean;
    readonly requiresDigit: boolean;
    readonly allowsWhitespace: boolean;
  };
  readonly payments: { readonly mode: 'None' | 'Sandbox' | 'Live' | string };
  readonly vocabularies: {
    readonly transmissions: readonly VocabularyEntry[];
    readonly fuelTypes: readonly VocabularyEntry[];
    readonly pickupMethods: readonly VocabularyEntry[];
    readonly cancellationReasons: readonly VocabularyEntry[];
    readonly rejectionReasons: readonly VocabularyEntry[];
  };
}

/** `name` is what the API accepts and returns; the labels are what a reader sees. */
export interface VocabularyEntry {
  readonly name: string;
  readonly labelEn: string;
  readonly labelAr: string;
}

export function vocabularyLabel(
  entries: readonly VocabularyEntry[] | undefined,
  name: string | null | undefined,
  arabic: boolean,
): string {
  if (!name) return '';
  const entry = entries?.find((candidate) => candidate.name === name);
  if (!entry) return name;
  const label = arabic ? entry.labelAr : entry.labelEn;
  return label || entry.labelEn || entry.labelAr || name;
}
