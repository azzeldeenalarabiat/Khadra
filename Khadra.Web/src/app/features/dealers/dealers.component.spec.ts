import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { DealersComponent } from './dealers.component';

const wait = (ms = 0) => new Promise((resolve) => setTimeout(resolve, ms));
const EMPTY_PAGE = { items: [], page: 1, pageSize: 18, totalCount: 0, totalPages: 0, hasPrevious: false, hasNext: false };

describe('the office directory search box', () => {
  let fixture: ComponentFixture<DealersComponent>;
  let query: BehaviorSubject<Record<string, string>>;
  let navigate: ReturnType<typeof vi.spyOn>;
  let http: HttpTestingController;

  const box = () => (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('input[type=search]')!;
  const type = (value: string) => {
    box().value = value;
    box().dispatchEvent(new Event('input'));
  };
  /** Renders, and answers whatever the page asked the API for (an empty directory, empty lookups). */
  const stable = async () => {
    for (let round = 0; round < 3; round++) {
      TestBed.tick();
      for (const request of http.match(() => true)) {
        request.flush(request.request.url === '/api/v1/galleries' ? EMPTY_PAGE : []);
      }
      await wait();
    }
    TestBed.tick();
  };
  /** The router moves the URL: what a real navigation would do to the query the page reads. */
  const urlBecomes = async (params: Record<string, string>) => {
    query.next(params);
    await stable();
  };
  /** Longer than the page's typing pause. */
  const pause = () => wait(450);

  beforeEach(async () => {
    query = new BehaviorSubject<Record<string, string>>({});
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { queryParams: query } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(DealersComponent);
    await stable();
  });

  afterEach(() => http.match(() => true));

  it('searches once typing pauses, replacing the history entry and starting again at page one', async () => {
    await urlBecomes({ page: '3' });
    type('arab');
    type('arabiat');
    expect(navigate).not.toHaveBeenCalled();
    await pause();
    expect(navigate).toHaveBeenCalledTimes(1);
    expect(navigate).toHaveBeenCalledWith([], { queryParams: { q: 'arabiat' }, replaceUrl: true });
  });

  it('keeps what the visitor typed, trailing space and all, while the URL catches up', async () => {
    type('arabiat ');
    await pause();
    await urlBecomes({ q: 'arabiat' });
    expect(box().value).toBe('arabiat ');
  });

  it('shows what an outside navigation searched for, even over text the visitor had typed', async () => {
    type('arabiat ');
    await pause();
    await urlBecomes({ q: 'arabiat' });
    // Back, or a link to another search: a URL this page did not write.
    await urlBecomes({ q: 'petra' });
    expect(box().value).toBe('petra');
  });

  it('empties the box and the URL together when the filters are cleared', async () => {
    await urlBecomes({ q: 'zzz', delivery: '1' });
    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.directory-search__clear')!.click();
    expect(navigate).toHaveBeenLastCalledWith([], { queryParams: {} });
    await urlBecomes({});
    expect(box().value).toBe('');
  });
});

/** Pre-launch item 155: the directory's city select, when the city list could not be loaded. */
describe('the office directory city filter', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('says the city list is unavailable instead of offering "Any city" alone', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { queryParams: new BehaviorSubject({ city: 'amman-id' }) } },
      ],
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(DealersComponent);
    for (let round = 0; round < 3; round++) {
      TestBed.tick();
      for (const request of http.match(() => true)) {
        if (request.request.url === '/api/v1/cities') request.flush(null, { status: 503, statusText: 'Service Unavailable' });
        else request.flush(request.request.url === '/api/v1/galleries' ? EMPTY_PAGE : []);
      }
      await wait();
    }
    TestBed.tick();

    const select = (fixture.nativeElement as HTMLElement).querySelector<HTMLSelectElement>('.directory-search__city select')!;
    expect(select.disabled).toBe(true);
    // The page's default language is Arabic.
    expect([...select.options].map((option) => option.textContent?.trim())).toEqual(['قائمة المدن غير متاحة']);
  });
});
