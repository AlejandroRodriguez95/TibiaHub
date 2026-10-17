import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { App } from './app';
import { routes } from './app.routes';

describe('App navigation', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [App], providers: [provideHttpClient(), provideRouter(routes)],
  }));

  it('links to the three account pages', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const links = [...(fixture.nativeElement as HTMLElement).querySelectorAll('nav a')];
    expect(links.map(link => link.getAttribute('href'))).toEqual(['/', '/login', '/register', '/account']);
  });

  it('keeps character lookup on the home page', async () => {
    const harness = await RouterTestingHarness.create('/');
    expect(harness.routeNativeElement!.querySelector('h1')?.textContent).toContain('Character lookup');
    expect(harness.routeNativeElement!.querySelector('#character-name')).toBeTruthy();
  });
});
