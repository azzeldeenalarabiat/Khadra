import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { NotificationsPanelComponent, PANEL_SIZE } from './notifications-panel.component';

const BOOKING = '01a0d0d8-26df-7dff-916e-9d867a17e31b';
const DISPUTE = '01a0d0d9-1b5e-7eda-938e-2c40cad2a91c';
const item = (id: string, kind: string, subjectId: string | null, isRead = false) => ({
  notificationId: id, kind, subjectId, subjectReference: 'KH-TEST0001', actorName: 'Test Rentals', isMine: true,
  occurredAt: '2026-09-24T04:36:00+00:00', readAt: isRead ? '2026-09-24T04:40:00+00:00' : null, isRead,
});

describe('the bell panel', () => {
  let fixture: ComponentFixture<NotificationsPanelComponent>;
  let http: HttpTestingController;
  let navigate: ReturnType<typeof vi.spyOn>;
  let closed: number;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), provideRouter([{ path: '**', children: [] }])],
    });
    http = TestBed.inject(HttpTestingController);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(NotificationsPanelComponent);
    closed = 0;
    fixture.componentInstance.closed.subscribe(() => closed++);

    const request = http.expectOne((r) => r.url === '/api/v1/notifications');
    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('pageSize')).toBe(String(PANEL_SIZE));
    request.flush({
      items: [item('n1', 'YourDepositRefunded', BOOKING), item('n2', 'YourDisputeUpdated', DISPUTE, true), item('n3', 'SomethingNew', null)],
      page: 1, pageSize: PANEL_SIZE, totalCount: 3, unreadCount: 2,
    });
    await fixture.whenStable();
  });

  afterEach(() => http.verify());

  const rows = () => [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('.notification')];

  it('shows the newest notifications from the feed, the unread ones marked', () => {
    expect(rows()).toHaveLength(3);
    expect(rows().map((row) => row.classList.contains('is-unread'))).toEqual([true, false, true]);
  });

  it('opening an unread one marks it read, goes to its booking, and closes the panel', async () => {
    rows()[0].click();
    http.expectOne('/api/v1/notifications/n1/read').flush(null);
    expect(navigate).toHaveBeenCalledWith(expect.arrayContaining(['bookings', BOOKING]));
    expect(closed).toBe(1);
  });

  it('a dispute update goes to the dispute, and a read one is not marked again', () => {
    rows()[1].click();
    http.expectNone('/api/v1/notifications/n2/read');
    expect(navigate).toHaveBeenCalledWith(expect.arrayContaining(['disputes', DISPUTE]));
    expect(closed).toBe(1);
  });

  it('one about nothing still closes the panel, without going anywhere', () => {
    rows()[2].click();
    http.expectOne('/api/v1/notifications/n3/read').flush(null);
    expect(navigate).not.toHaveBeenCalled();
    expect(closed).toBe(1);
  });

  it('"view all" leads to the full page and closes the panel', () => {
    const all = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>('.notif-panel__all')!;
    expect(all.getAttribute('href')).toMatch(/\/notifications$/);
    all.click();
    expect(closed).toBe(1);
  });
});
