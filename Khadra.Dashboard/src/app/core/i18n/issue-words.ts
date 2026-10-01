import { EnumFamily } from './status-key';

/** `I18nService.enumLabel`: a server enum in the reader's language, spelled out when this build has no word. */
type EnumLabel = (family: EnumFamily, name: string | null | undefined) => string;

/**
 * The calculator's issues, as the server joins their codes into a hold's detail ("EndingRefundMissing, RefundsConflict"):
 * one line each, worded as a booking's Money section words them (pre-launch item 219). A code this build does not
 * know is spelled out by the label's fallback, never dropped.
 */
export function issueLines(joined: string | null | undefined, label: EnumLabel): readonly string[] {
  return (joined ?? '')
    .split(',')
    .map((code) => code.trim())
    .filter((code) => code.length > 0)
    .map((code) => label('financialIssue', code));
}
