import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { App } from './app';
import { routes } from './app.routes';

describe('App navigation', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [App], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter(routes)],
  }));

  it('links to home, lookup, and account pages', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const links = [...(fixture.nativeElement as HTMLElement).querySelectorAll('nav > a')];
    expect(links.map(link => link.getAttribute('href'))).toEqual(['/', '/characters', '/login', '/account']);
  });

  it('groups the tools in a dropdown that closes with Escape and outside clicks', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const toggle = element.querySelector<HTMLButtonElement>('.tools-toggle')!;
    const menu = element.querySelector<HTMLElement>('#tools-links')!;
    expect(menu.hidden).toBe(true);
    toggle.click();
    fixture.detectChanges();
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(menu.hidden).toBe(false);
    expect([...menu.querySelectorAll('a')].map(a => a.getAttribute('href'))).toEqual(['/tools/npc-selling', '/tools/vip', '/tools/character-scan']);
    toggle.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    expect(menu.hidden).toBe(true);
    toggle.click();
    fixture.detectChanges();
    document.body.click();
    fixture.detectChanges();
    expect(menu.hidden).toBe(true);
  });

  it('shows the landing page at home', async () => {
    const harness = await RouterTestingHarness.create('/');
    expect(harness.routeNativeElement!.querySelector('h1')?.textContent).toContain('Your next adventure');
    expect(harness.routeNativeElement!.querySelector('#landing-name')).toBeTruthy();
  });

  it('keeps character lookup on its own route', async () => {
    const harness = await RouterTestingHarness.create('/characters');
    expect(harness.routeNativeElement!.querySelector('h1')?.textContent).toContain('Character lookup');
    expect(harness.routeNativeElement!.querySelector('#character-name')).toBeTruthy();
  });

  it('passes a trimmed landing search into lookup and displays the response', async () => {
    const harness = await RouterTestingHarness.create('/');
    const input = harness.routeNativeElement!.querySelector<HTMLInputElement>('#landing-name')!;
    input.value = '  Bubble  ';
    input.dispatchEvent(new Event('input'));
    harness.routeNativeElement!.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await harness.fixture.whenStable();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/character/Bubble').flush({ character: { character: { name: 'Bubble', level: 250, world: 'Antica' } } });
    harness.detectChanges();
    expect(harness.routeNativeElement!.querySelector('.character-result')?.textContent).toContain('Bubble');
    expect(harness.routeNativeElement!.querySelector<HTMLInputElement>('#character-name')!.value).toBe('Bubble');
    http.verify();
  });

  it('keeps an empty landing search on home with a useful error', async () => {
    const harness = await RouterTestingHarness.create('/');
    harness.routeNativeElement!.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    harness.detectChanges();
    expect(harness.routeNativeElement!.querySelector('[role="alert"]')?.textContent).toContain('Enter a character name');
    TestBed.inject(HttpTestingController).expectNone(() => true);
  });
});
