import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CharacterScan } from './character-scan';

describe('Character scans', () => {
  const url = '/api/tools/character-scan';
  beforeEach(() => TestBed.configureTestingModule({ imports: [CharacterScan], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  async function submit() {
    const fixture = TestBed.createComponent(CharacterScan);
    fixture.detectChanges();
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('select')).toBeNull();
    const name = element.querySelector<HTMLInputElement>('#scan-name')!;
    name.value = ' Bob ';
    name.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    element.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    return { fixture, element, request: TestBed.inject(HttpTestingController).expectOne(url + '?name=Bob') };
  }

  it('needs only the character name and displays the automatically resolved world', async () => {
    const { fixture, element, request } = await submit();
    expect(request.request.withCredentials).toBe(true);
    request.flush({ world: 'Antica', character: 'Bob', historyStart: '2026-10-20T00:00:00Z', lastObservation: '2026-10-20T01:00:00Z', observedLogouts: 3, usableLogouts: 2,
      candidates: [{ name: 'Dave', score: 15, firstMinuteMatches: 1, secondMinuteMatches: 1 }] });
    fixture.detectChanges();
    expect(element.textContent).toContain('Bob / Antica');
    expect(element.textContent).toContain('2 usable logouts out of 3');
    expect(element.querySelector('tbody')!.textContent).toContain('Dave');
    expect(element.querySelector('tbody')!.textContent).toContain('15');
  });

  it('asks anonymous users to log in after submitting', async () => {
    const { fixture, element, request } = await submit();
    request.flush(null, { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();
    expect(element.querySelector('a')!.getAttribute('href')).toBe('/login');
    expect(element.querySelector('form')).toBeNull();
  });

  it('shows missing history without asking for a world', async () => {
    const { fixture, element, request } = await submit();
    request.flush({ message: 'No retained online history was found for this character.' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();
    expect(element.querySelector('[role="alert"]')!.textContent).toContain('No retained online history');
    expect(element.querySelector('form')).not.toBeNull();
  });
});
