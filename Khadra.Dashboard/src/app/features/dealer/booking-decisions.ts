import { Injectable, inject } from '@angular/core';
import { DealerBookingsService, HandoverInput } from '../../core/services/dealer-bookings.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { TranslationKey } from '../../core/i18n/en';
import { I18nService } from '../../core/i18n/i18n.service';

/** The three handover figures, by the stable name the dialog returns them under. */
const ODOMETER = 'odometerKm';
const FUEL = 'fuelLevel';
const CASH = 'cashCollected';

/**
 * The dealer's decisions on a booking, each behind the console's confirmation dialog so the
 * consequence is stated before it happens and the outcome reported back (design: `modals()`).
 *
 * Shared by the list and the detail screen so the two never word the same decision differently.
 */
@Injectable({ providedIn: 'root' })
export class BookingDecisions {
  private readonly service = inject(DealerBookingsService);
  private readonly ui = inject(ConsoleUiService);
  private readonly t = inject(I18nService).t;

  /**
   * Codes the API accepts (RejectionReasons.Labels), with the words the dealer picks from.
   *
   * The code is what travels; the label is only what is read, and it is resolved fresh so the list
   * follows the language. They were the same string once, and the dialog matched the chosen WORDS
   * back to a code — which meant an Arabic label matched nothing and every rejection was filed as
   * 'Other'.
   */
  get rejectionReasons(): readonly { readonly code: string; readonly label: string }[] {
    return [
      { code: 'VehicleUnavailable', label: this.t('dealerDecide.reason.vehicleUnavailable') },
      { code: 'DatesConflict', label: this.t('dealerDecide.reason.datesConflict') },
      { code: 'OutsideDeliveryRadius', label: this.t('dealerDecide.reason.outsideRadius') },
      {
        code: 'CustomerVerificationIncomplete',
        label: this.t('dealerDecide.reason.verificationIncomplete'),
      },
      { code: 'Other', label: this.t('dealerDecide.reason.other') },
    ];
  }

  approve(bookingId: string, reference: string, customer: string, done: () => void): void {
    this.ui.openAction(
      {
        icon: 'check-circle',
        tone: 'ok',
        title: this.t('dealerDecide.approve.title', { reference }),
        body: this.t('dealerDecide.approve.body', { customer }),
        fields: [
          {
            name: 'noteToCustomer',
            label: this.t('dealerDecide.approve.noteLabel'),
            type: 'text',
            optional: true,
            placeholder: this.t('dealerDecide.approve.notePlaceholder'),
            hint: this.t('dealerDecide.approve.noteHint'),
          },
        ],
        note: this.t('dealerDecide.approve.note'),
        confirm: this.t('dealerDecide.approve.confirm'),
        result: {
          title: this.t('dealerDecide.approve.doneTitle'),
          body: this.t('dealerDecide.approve.doneBody', { reference }),
        },
      },
      async (values) => {
        await this.service.approve(bookingId, values['noteToCustomer']?.trim() || null);
        done();
      },
      {
        title: this.t('dealerDecide.approve.doneTitle'),
        body: this.t('dealerDecide.approve.doneToast', { reference }),
      },
    );
  }

  reject(bookingId: string, reference: string, done: () => void): void {
    const reasons = this.rejectionReasons;
    this.ui.openAction(
      {
        icon: 'x-circle',
        tone: 'bad',
        danger: true,
        title: this.t('dealerDecide.reject.title', { reference }),
        body: this.t('dealerDecide.reject.body'),
        fields: [
          {
            name: 'reason',
            label: this.t('dealerDecide.reject.reasonLabel'),
            type: 'select',
            // The API code IS the option value, so nothing has to be matched back from the words.
            options: reasons.map((r) => ({ value: r.code, label: r.label })),
          },
          {
            name: 'details',
            label: this.t('dealerDecide.reject.detailsLabel'),
            type: 'text',
            placeholder: this.t('dealerDecide.reject.detailsPlaceholder'),
          },
        ],
        confirm: this.t('dealerDecide.reject.confirm'),
        result: {
          title: this.t('dealerDecide.reject.doneTitle'),
          body: this.t('dealerDecide.reject.doneBody'),
          tone: 'warn',
        },
      },
      async (values) => {
        const code = values['reason'] ?? reasons[0].code;
        const details = values['details']?.trim();
        if (!details) throw { error: { title: this.t('dealerDecide.reject.needDetails') } };
        await this.service.reject(bookingId, code, details);
        done();
      },
      {
        title: this.t('dealerDecide.reject.doneTitle'),
        body: this.t('dealerDecide.reject.doneToast', { reference }),
        tone: 'warn',
      },
    );
  }

  recordPickup(bookingId: string, reference: string, vehicle: string, done: () => void): void {
    this.ui.openAction(
      {
        icon: 'key',
        tone: 'ok',
        title: this.t('dealerDecide.pickup.title', { vehicle }),
        body: this.t('dealerDecide.pickup.body', { reference }),
        fields: [
          {
            name: ODOMETER,
            label: this.t('dealerDecide.odometerLabel'),
            type: 'text',
            optional: true,
            placeholder: this.t('dealerDecide.pickup.odometerPlaceholder'),
          },
          {
            name: FUEL,
            label: this.t('dealerDecide.fuelLabel'),
            type: 'text',
            optional: true,
            placeholder: this.t('dealerDecide.pickup.fuelPlaceholder'),
          },
          {
            name: CASH,
            label: this.t('dealerDecide.cashLabel'),
            type: 'text',
            optional: true,
            placeholder: this.t('dealerDecide.pickup.cashPlaceholder'),
          },
          {
            name: 'notes',
            label: this.t('dealerDecide.notesLabel'),
            type: 'text',
            optional: true,
            placeholder: this.t('dealerDecide.pickup.notesPlaceholder'),
          },
        ],
        note: this.t('dealerDecide.pickup.note'),
        confirm: this.t('dealerDecide.pickup.confirm'),
        result: {
          title: this.t('dealerDecide.pickup.doneTitle'),
          body: this.t('dealerDecide.pickup.doneBody', { reference }),
        },
      },
      async (values) => {
        await this.service.recordPickup(bookingId, this.handover(values));
        done();
      },
      {
        title: this.t('dealerDecide.pickup.doneTitle'),
        body: this.t('dealerDecide.pickup.doneBody', { reference }),
      },
    );
  }

  recordReturn(bookingId: string, reference: string, vehicle: string, done: () => void): void {
    this.ui.openAction(
      {
        icon: 'arrow-square-in',
        tone: 'ok',
        title: this.t('dealerDecide.return.title', { vehicle }),
        body: this.t('dealerDecide.return.body', { reference }),
        fields: [
          {
            name: ODOMETER,
            label: this.t('dealerDecide.odometerLabel'),
            type: 'text',
            optional: true,
            placeholder: this.t('dealerDecide.return.odometerPlaceholder'),
          },
          {
            name: FUEL,
            label: this.t('dealerDecide.fuelLabel'),
            type: 'text',
            optional: true,
            placeholder: this.t('dealerDecide.return.fuelPlaceholder'),
          },
          {
            name: CASH,
            label: this.t('dealerDecide.cashLabel'),
            type: 'text',
            optional: true,
            placeholder: this.t('dealerDecide.return.cashPlaceholder'),
          },
          {
            name: 'notes',
            label: this.t('dealerDecide.notesLabel'),
            type: 'text',
            optional: true,
            placeholder: this.t('dealerDecide.return.notesPlaceholder'),
          },
        ],
        note: this.t('dealerDecide.return.note'),
        confirm: this.t('dealerDecide.return.confirm'),
        result: {
          title: this.t('dealerDecide.return.doneTitle'),
          body: this.t('dealerDecide.return.doneBody', { reference }),
        },
      },
      async (values) => {
        await this.service.recordReturn(bookingId, this.handover(values));
        done();
      },
      {
        title: this.t('dealerDecide.return.doneTitle'),
        body: this.t('dealerDecide.return.doneBody', { reference }),
      },
    );
  }

  /**
   * The three figures that decide a disputed return, read back by NAME.
   *
   * They were read back by the visible label — `values['Odometer (km)']` — which meant translating
   * the caption returned `undefined` for all three, `number()` turned that into `null`, and the
   * handover was recorded with no odometer, no fuel and no cash, silently. The constants above are
   * the same ones the fields are declared with, so the two cannot drift apart again.
   */
  private handover(values: Record<string, string>): HandoverInput {
    const number = (name: string, labelKey: TranslationKey): number | null => {
      const raw = values[name]?.trim();
      if (!raw) return null;
      const parsed = Number(raw);
      if (!Number.isFinite(parsed)) {
        throw { error: { title: this.t('dealerDecide.mustBeANumber', { field: this.t(labelKey) }) } };
      }
      return parsed;
    };
    return {
      odometerKm: number(ODOMETER, 'dealerDecide.odometerLabel'),
      fuelLevel: number(FUEL, 'dealerDecide.fuelLabel'),
      cashCollected: number(CASH, 'dealerDecide.cashLabel'),
      notes: values['notes']?.trim() || null,
    };
  }
}
