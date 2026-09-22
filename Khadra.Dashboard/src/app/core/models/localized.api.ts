/**
 * The two shapes dealer-authored text travels in.
 *
 * Its own file because both sides of the platform read them — a car's description, a gallery's six
 * page sections, its About — and a wire type living inside one feature's model file is how the fleet
 * ends up with a second, slightly different spelling of the same contract.
 */

/**
 * One field in both languages, exactly as the office typed them. Null is "nothing written".
 *
 * Never resolved on the way to an OWNER: an office that has written only Arabic must find an empty
 * English box, because that empty box is the work still to do.
 */
export interface LocalizedText {
  readonly ar: string | null;
  readonly en: string | null;
}

/**
 * A piece of the office's writing as a CUSTOMER gets it, and which language it turned out to be.
 *
 * The language may not be the one asked for — the server shows what the office actually wrote rather
 * than an empty heading — so a screen that assumed otherwise would put English under an Arabic
 * heading with nothing to say so. Render the text in the language this carries, not the one asked
 * for: the direction of the paragraph depends on it.
 */
export interface ResolvedText {
  readonly text: string;
  readonly language: string;
}
