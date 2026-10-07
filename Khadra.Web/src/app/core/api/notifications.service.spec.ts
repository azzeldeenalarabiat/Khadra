import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ConsentGateService } from '../session/consent-gate.service';
import { SessionService } from '../session/session.service';
import { NotificationsService } from './notifications.service';

const COUNT = '/api/v1/notifications/unread-count';
const settle = () => new Promise((resolve) => setTimeout(resolve));

describe('the bell badge', () => {
  let http: HttpTestingController;
  let service: NotificationsService;
  /** Whether a legal text awaits the customer's consent (Wave 4, W4-8). */
  const consentBlocked = signal(false);

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SessionService, useValue: { isSignedIn: signal(true) } },
        { provide: ConsentGateService, useValue: { blocked: consentBlocked } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(NotificationsService);
    // Signing in reads the count once.
    TestBed.tick();
    http.expectOne(COUNT).flush(3);
    await settle();
    expect(service.unread()).toBe(3);
  });

  afterEach(() => {
    http.verify();
    consentBlocked.set(false);
  });

  // Wave 4, W4-8: every answer behind the prompt would be a refusal, so the badge waits, then asks the moment it lifts.
  it('is not asked behind the consent prompt, and is asked again once the texts are accepted', async () => {
    consentBlocked.set(true);
    TestBed.tick();
    await service.refresh();
    http.expectNone(COUNT);

    consentBlocked.set(false);
    TestBed.tick();
    http.expectOne(COUNT).flush(5);
    await settle();
    expect(service.unread()).toBe(5);
  });

  it('drops the moment a notification is opened, then settles on the server count', async () => {
    const marking = service.markRead('n1');
    expect(service.unread()).toBe(2);
    http.expectOne('/api/v1/notifications/n1/read').flush(null);
    await marking;
    http.expectOne(COUNT).flush(2);
    await settle();
    expect(service.unread()).toBe(2);
  });

  it('never lets a poll that was already in flight put a cleared count back', async () => {
    const poll = service.refresh();
    const pending = http.expectOne(COUNT);

    const clearing = service.markAllRead();
    http.expectOne('/api/v1/notifications/read-all').flush(1);
    await clearing;
    expect(service.unread()).toBe(0);

    pending.flush(3);
    await poll;
    expect(service.unread()).toBe(0);

    http.expectOne(COUNT).flush(0);
    await settle();
    expect(service.unread()).toBe(0);
  });

  it('puts the count back from the server when marking read fails', async () => {
    const marking = service.markRead('n1').catch(() => undefined);
    expect(service.unread()).toBe(2);
    http.expectOne('/api/v1/notifications/n1/read').flush(null, { status: 503, statusText: 'Unavailable' });
    await marking;
    http.expectOne(COUNT).flush(3);
    await settle();
    expect(service.unread()).toBe(3);
  });
});
