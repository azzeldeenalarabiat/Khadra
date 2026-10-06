import { TranslationKey } from '../../core/i18n/en';
import { Language } from '../../core/i18n/language';
import { ProblemSnapshot, serverSentence } from '../../core/i18n/problem';

/**
 * Why a submission was refused, in the reader's own language, worded when it is shown.
 *
 * The codes this screen knows by their own sentences; anything else by the server's English title
 * while the console is English, and the console's own lines while it is not.
 */
export function describeApplicationRefusal(
  problem: ProblemSnapshot,
  t: (key: TranslationKey) => string,
  language: Language,
): string {
  // No answer at all — the request never reached the platform, or something failed before it could.
  if (problem.status === 0) {
    return t('dealerApply.theServiceDidNot');
  }

  switch (problem.code) {
    case 'dealer.already_registered':
    // The database's own answer to the same question, from the unique index on the owner. The
    // handler's check and the index can only disagree in a race — two tabs, or a double click on a
    // slow multipart — and the person on the other end needs the same sentence either way.
    case 'data.conflict':
      return t('dealerApply.thisAccountHasAlready');
    case 'dealer.commercial_registration_taken':
      return t('dealerApply.aGalleryIsAlready');
    // The server's code is `dealer.missing_documents`; the console listened for another name and never worded it (F68).
    case 'dealer.missing_documents':
      return t('dealerApply.allThreeDocumentsAre');
    // The platform's own name (pre-launch item 230): worded here in both languages, and beneath the field too.
    case 'dealer.business_name_reserved':
      return t('dealerApply.nameReserved');
    case 'dealer.document_too_large':
    case 'documents.too_large':
      return t('dealerApply.oneOfTheFiles');
    case 'dealer.invalid_document_content':
    case 'documents.invalid_content':
      return t('dealerApply.uploadEachDocumentAs');
    case 'dealer.invalid_operating_hours':
      return t('dealerApply.closingTimeMustBe');
    default:
      break;
  }

  if (problem.status === 413) {
    return t('dealerApply.theDocumentsTogetherAre');
  }
  return serverSentence(problem, language, t) ?? t('dealerApply.theApplicationWasRejected');
}
