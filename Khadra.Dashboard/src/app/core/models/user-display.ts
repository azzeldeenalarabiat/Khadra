import { SessionUser } from '../services/session.service';

/**
 * How the signed-in person is shown in the frame.
 *
 * The console started as an Admin-only surface, so the sidebar and topbar carried the design's
 * sample admin verbatim. Now that dealer staff sign into the same shell, a dealer was being
 * greeted by another person's name and told they were a Super Admin — which is not a cosmetic
 * problem: the account label is how you check you are signed in as who you think you are.
 */

/** Two initials for the avatar. Falls back to the email when there is no usable name. */
export function initialsOf(user: SessionUser | null): string {
  const source = user?.fullName?.trim() || user?.email?.trim() || '';
  if (!source) return '·';

  const words = source.split(/[\s@._-]+/).filter(Boolean);
  const letters = words.slice(0, 2).map((word) => word[0]);
  return (letters.join('') || source[0]).toUpperCase();
}

/** The role in the words a person would use, not the enum name. */
export function roleLabel(role: string | undefined): string {
  switch (role) {
    case 'Admin':
      return 'Administrator';
    case 'DealerOwner':
      return 'Dealer owner';
    case 'DealerEmployee':
      return 'Dealer employee';
    case 'Customer':
      return 'Customer';
    default:
      return '';
  }
}

/** The first breadcrumb: which side of the platform you are standing on. */
export function areaLabel(role: string | undefined): string {
  return role === 'DealerOwner' || role === 'DealerEmployee' ? 'My dealership' : 'Admin';
}
