/** What the booking page knows when the customer reaches for "Send the request". */
export interface RequestState {
  readonly busy: boolean;
  /** The quote's verdict for these dates; null until it answers. */
  readonly available: boolean | null;
  readonly delivery: boolean;
  readonly deliveryPointChosen: boolean;
  /** True only when the session SAYS the address is unverified; unknown is not a refusal. */
  readonly emailUnverified: boolean;
  /** True only when the documents endpoint SAYS they are incomplete; loading or failed is not a refusal. */
  readonly documentsIncomplete: boolean;
}

/**
 * Whether a request could be accepted as it stands (Wave 3 E4; E2E F18). The page already knows when the email is
 * unverified or the documents are incomplete, and shows a notice for each; sending anyway could only end in the
 * server's refusal. What the page does not know yet is never treated as a refusal: the server judges every request
 * again, and words its own refusal when one comes.
 */
export function canSendRequest(state: RequestState): boolean {
  return (
    !state.busy &&
    state.available === true &&
    !(state.delivery && !state.deliveryPointChosen) &&
    !state.emailUnverified &&
    !state.documentsIncomplete
  );
}

/** Whether the request is held back only by a step the customer must take first: an email or the documents. */
export function waitsOnTheCustomer(state: RequestState): boolean {
  return state.emailUnverified || state.documentsIncomplete;
}
