import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it } from 'vitest';

import { NotificationItem } from '../../core/api/notifications.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { notificationText } from './notification-text';

const item = (over: Partial<NotificationItem>): NotificationItem =>
  ({
    notificationId: 'n1',
    kind: 'YourBookingExpired',
    subjectId: null,
    subjectReference: 'KH-NY8AHLNK',
    actorName: 'Petra Wheels',
    isMine: false,
    occurredAt: '2026-10-08T09:00:00Z',
    readAt: null,
    isRead: false,
    ...over,
  }) as NotificationItem;

/** Pre-launch item 103: an office that has left the platform is a code, worded in the reader's language. */
describe('notificationText, the actor', () => {
  afterEach(() => TestBed.resetTestingModule());

  function i18n(language: 'ar' | 'en'): I18nService {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    const service = TestBed.inject(I18nService);
    service.use(language);
    return service;
  }

  it('words the rental-office stand-in in Arabic instead of printing its English phrase', () => {
    const text = notificationText(
      i18n('ar'),
      item({ actorName: 'The rental office', actorStandIn: 'RentalOffice' }),
    );
    expect(text).toContain('مكتب التأجير');
    expect(text).not.toContain('The rental office');
  });

  it('names an office by its own name', () => {
    expect(notificationText(i18n('ar'), item({}))).toContain('Petra Wheels');
  });
});
