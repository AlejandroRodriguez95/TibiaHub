import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { Vip } from './vip';
import { VipEvent, VipLive, VipSnapshot, VipState, groupStatus } from './vip-live';

const api = 'http://localhost:5267/tools/vip';
const base: VipSnapshot = { capturedAt: '2026-10-23T10:00:00Z', maxCharacters: 1000, maxGroups: 100,
  groups: [{ id: 10, name: 'Alejandro' }, { id: 20, name: 'Friends' }],
  characters: [{ id: 1, name: 'Lazy Snorlax', world: 'Antica', groupIds: [10, 20], status: 'online', lastObservedAt: null },
    { id: 2, name: 'Krieghor', world: 'Antica', groupIds: [10], status: 'offline', lastObservedAt: null }] };

describe('VIP list', () => {
  let events: Subject<VipEvent>;
  let http: HttpTestingController;
  beforeEach(() => {
    events = new Subject<VipEvent>();
    TestBed.configureTestingModule({ imports: [Vip], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    TestBed.overrideComponent(Vip, { set: { providers: [{ provide: VipLive, useValue: { connect: () => events.asObservable() } }] } });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  function render() {
    const fixture = TestBed.createComponent(Vip); fixture.detectChanges();
    const request = http.expectOne(api); expect(request.request.withCredentials).toBe(true);
    request.flush(structuredClone(base));
    events.next({ kind: 'connection', value: 'connected' }); fixture.detectChanges();
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  it('prompts anonymous visitors to log in without connecting', () => {
    const fixture = TestBed.createComponent(Vip); fixture.detectChanges();
    http.expectOne(api).flush(null, { status: 401, statusText: 'Unauthorized' }); fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('a')?.getAttribute('href')).toBe('/login');
    expect(element.querySelector('form')).toBeNull();
    expect(events.observed).toBe(false);
  });

  it('shows shared members in multiple expandable groups and updates aggregate status', () => {
    const { fixture, element } = render();
    const headers = () => [...element.querySelectorAll('.vip-expand')];
    expect(headers()[0].textContent).toContain('Alejandro ? online (2)');
    expect(headers()[1].textContent).toContain('Friends ? online (1)');
    (headers()[0] as HTMLButtonElement).click(); fixture.detectChanges();
    expect(headers()[0].getAttribute('aria-expanded')).toBe('false');
    expect(element.querySelector<HTMLUListElement>('#vip-group-10')?.hidden).toBe(true);
    events.next({ kind: 'statuses', value: { capturedAt: '2026-10-23T10:01:00Z', characters: [{ id: 1, status: 'offline', lastObservedAt: null }] } }); fixture.detectChanges();
    expect(headers()[0].textContent).toContain('Alejandro ? offline (2)');
    expect(headers()[1].textContent).toContain('Friends ? offline (1)');
    events.next({ kind: 'connection', value: 'reconnecting' }); fixture.detectChanges();
    expect(headers()[0].textContent).toContain('unknown');
    expect(element.querySelector('[data-status="online"]')).toBeNull();
    fixture.destroy(); expect(events.observed).toBe(false);
  });

  it('uses a fresh CSRF token for writes and reloads the persisted list', async () => {
    const { fixture, element } = render();
    await fixture.whenStable();
    const input = element.querySelector<HTMLInputElement>('#vip-character')!;
    input.value = 'Bob'; input.dispatchEvent(new Event('input'));
    fixture.detectChanges(); await fixture.whenStable();
    element.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    http.expectOne('http://localhost:5267/account/csrf').flush({ token: 'fresh' });
    const write = http.expectOne(api + '/characters');
    expect(write.request.headers.get('X-CSRF-TOKEN')).toBe('fresh');
    expect(write.request.withCredentials).toBe(true); expect(write.request.body).toEqual({ name: 'Bob' });
    write.flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne(api).flush({ ...base, capturedAt: '2026-10-23T10:02:00Z' }); fixture.detectChanges();
    await fixture.whenStable();
    expect(input.value).toBe('');
  });
});

describe('VIP state ordering', () => {
  it('preserves newer deltas when a reconnect snapshot arrives late', () => {
    const state = new VipState(); state.snapshot(structuredClone(base));
    state.statuses({ capturedAt: '2026-10-23T10:02:00Z', characters: [{ id: 1, status: 'offline', lastObservedAt: null }] });
    const next = state.snapshot({ ...structuredClone(base), capturedAt: '2026-10-23T10:01:00Z' });
    expect(next.characters[0].status).toBe('offline');
    state.statuses({ capturedAt: '2026-10-23T10:00:30Z', characters: [{ id: 1, status: 'online', lastObservedAt: null }] });
    expect(state.value?.characters[0].status).toBe('offline');
  });
  it('handles a delta before the first snapshot and clears deleted members', () => {
    const state = new VipState();
    state.statuses({ capturedAt: '2026-10-23T10:02:00Z', characters: [{ id: 1, status: 'unknown', lastObservedAt: null }] });
    expect(state.snapshot(structuredClone(base)).characters[0].status).toBe('unknown');
    expect(state.snapshot({ ...base, capturedAt: '2026-10-23T10:03:00Z', characters: [] }).characters).toEqual([]);
  });
  it('derives online, unknown, and empty group statuses conservatively', () => {
    expect(groupStatus(base.characters)).toBe('online');
    expect(groupStatus([{ ...base.characters[0], status: 'unknown' }, base.characters[1]])).toBe('unknown');
    expect(groupStatus([base.characters[1]])).toBe('offline');
    expect(groupStatus([])).toBe('unknown');
    expect(groupStatus(base.characters, false)).toBe('unknown');
  });
});
