import { NavGroup, NavRequirement } from '../core/models/console.models';
import { DealerPermissions } from '../core/services/dealer-console.service';

/**
 * A dealer rail as this person may use it: every item that names a permission is kept only when the server says
 * they hold it, and a group left empty disappears with its items.
 *
 * Until `GET /dealers/me` answers, `permissions` is null and every gated item is OMITTED rather than guessed:
 * showing one and taking it away a beat later is the worse of the two mistakes. Used for the owner's rail and the
 * employee's alike — the employee's Finance group is the owner's grant to one person (E2E F19).
 */
export function railFor(groups: readonly NavGroup[], permissions: DealerPermissions | null): readonly NavGroup[] {
  return groups
    .map((group) => ({ ...group, items: group.items.filter((item) => holds(item.requires, permissions)) }))
    .filter((group) => group.items.length > 0);
}

function holds(requires: NavRequirement | undefined, permissions: DealerPermissions | null): boolean {
  if (!requires) return true;
  if (!permissions) return false;
  return requires === 'manage-staff' ? permissions.canManageStaff : permissions.canViewReports;
}
