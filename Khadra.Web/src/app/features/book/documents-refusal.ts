import { CustomerDocuments } from '../../core/api/documents.api';

/**
 * The document types Khadra rejected, as a `booking.documents_incomplete` refusal names them (Wave 4, W4-9; additive,
 * so an older API simply names none). Type names only: the reason is the customer's to read on their documents page.
 */
export function rejectedDocumentTypes(error: unknown): readonly string[] {
  const body = typeof error === 'object' && error !== null ? (error as { error?: unknown }).error : undefined;
  const types =
    typeof body === 'object' && body !== null ? (body as { rejectedDocumentTypes?: unknown }).rejectedDocumentTypes : undefined;
  return Array.isArray(types) ? types.filter((type): type is string => typeof type === 'string') : [];
}

/**
 * Whether what the customer still owes includes a file Khadra asked them to replace, from their own record: a rejected
 * document whose type the server still lists as missing. A rejected file already replaced by a new upload is not.
 */
export function owesARejectedDocument(documents: CustomerDocuments | undefined): boolean {
  if (!documents || documents.isComplete) return false;
  return documents.documents.some(
    (document) => document.status === 'Rejected' && documents.missing.includes(document.type),
  );
}
