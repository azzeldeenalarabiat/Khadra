import { TranslationKey } from '../../core/i18n/en';
import { ModalField } from '../../core/models/console.models';

/**
 * What is wrong with the SHAPE of an identifier typed into a dialog, before it is sent (Wave 5, F88), or null.
 *
 * Deliberately looser than the server. `EmailAddress` and `PhoneNumber` own the rules — a Jordanian mobile range,
 * the E.164 length, a two-letter domain ending — and the API's refusal is worded in either language from its `code`
 * inside the same dialog. This catches only what is plainly not an address or a number (no "@", a space, a name typed
 * into the phone box), so that it can never refuse something the server would accept: a stricter copy here would
 * drift from the domain the first time either changed.
 *
 * Only an `email` or `tel` line is checked, and an empty one is not: whether a field may be empty is `optional`'s
 * question, already answered by the dialog.
 */
export function fieldShapeProblem(field: ModalField, value: string): TranslationKey | null {
  if (field.type !== 'line') return null;
  const trimmed = value.trim();
  if (!trimmed) return null;

  if (field.inputMode === 'email') {
    return /^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(trimmed) ? null : 'modalField.emailShape';
  }
  if (field.inputMode === 'tel') {
    // The server keeps the digits and a leading "+" and drops spaces, dashes, dots and brackets; the shortest number
    // it accepts has eight digits.
    const digits = trimmed.replace(/\D/g, '').length;
    return digits >= 8 ? null : 'modalField.phoneShape';
  }
  return null;
}
