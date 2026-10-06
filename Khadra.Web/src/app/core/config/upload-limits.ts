/**
 * The file rules `/app-config` publishes for anything a customer uploads: identity documents and dispute evidence
 * alike. A page checks them only to spare a pointless upload; the server judges every file again.
 */
export interface UploadLimits {
  readonly maximumSizeBytes: number;
  readonly allowedContentTypes: readonly string[];
}

/** The published types as a reader names them: "JPEG, PNG, WEBP, PDF". */
export function uploadTypeNames(types: readonly string[]): string {
  return types.map((type) => (type.split('/')[1] ?? type).toUpperCase()).join(', ');
}

/** Why a chosen file would be refused, before anything is sent: its kind, its size, or nothing. */
export function uploadProblem(
  file: { readonly type: string; readonly size: number },
  limits: UploadLimits | null,
): 'wrongType' | 'tooLarge' | null {
  if (!limits) return null;
  if (!limits.allowedContentTypes.includes(file.type)) return 'wrongType';
  if (file.size > limits.maximumSizeBytes) return 'tooLarge';
  return null;
}
