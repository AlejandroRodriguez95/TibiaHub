import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from './app.routes';

describe('Account pages', () => {
  const url = 'http://localhost:5267/account';
  let http: HttpTestingController;
  let harness: RouterTestingHarness;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter(routes)],
    });
    http = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
  });

  afterEach(() => http.verify());

  async function open(path: string, authenticated = false) {
    await harness.navigateByUrl(path);
    const request = http.expectOne(`${url}/myaccount`);
    expect(request.request.withCredentials).toBe(true);
    if (authenticated) request.flush({ id: 'user-1', email: 'player@example.com' });
    else request.flush(null, { status: 401, statusText: 'Unauthorized' });
    harness.detectChanges();
    await harness.fixture.whenStable();
  }

  async function settleNavigation() {
    await harness.fixture.whenStable();
    http.expectOne(`${url}/myaccount`).flush(null, { status: 401, statusText: 'Unauthorized' });
    harness.detectChanges();
    await harness.fixture.whenStable();
  }

  it('restores the account page and logs out with a fresh CSRF token', async () => {
    await open('/account', true);
    const element = harness.routeNativeElement!;
    expect(element.textContent).toContain('player@example.com');
    expect(element.textContent).not.toContain('user-1');
    expect(element.textContent).not.toContain('User ID');
    element.querySelector<HTMLButtonElement>('.account-actions button')!.click();
    const csrf = http.expectOne(`${url}/csrf`);
    expect(csrf.request.withCredentials).toBe(true);
    csrf.flush({ token: 'fresh-token' });
    const logout = http.expectOne(`${url}/logout`);
    expect(logout.request.withCredentials).toBe(true);
    expect(logout.request.headers.get('X-CSRF-TOKEN')).toBe('fresh-token');
    logout.flush(null, { status: 204, statusText: 'No Content' });
    await settleNavigation();
    expect(TestBed.inject(Router).url).toContain('/login');
    expect(harness.routeNativeElement!.textContent).toContain('You have logged out.');
  });

  it('saves a preferred world from the cached list with CSRF protection', async () => {
    await harness.navigateByUrl('/account');
    http.expectOne(url + '/myaccount').flush({ id: 'user-1', email: 'player@example.com', defaultWorld: 'Antica', worlds: ['Antica', 'Secura'] });
    harness.detectChanges();
    await harness.fixture.whenStable();
    const element = harness.routeNativeElement!;
    const select = element.querySelector<HTMLSelectElement>('#default-world')!;
    expect(select.value).toBe('Antica');
    expect(element.textContent).not.toContain('Look up a character');
    select.value = 'Secura';
    select.dispatchEvent(new Event('change'));
    await harness.fixture.whenStable();
    element.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    http.expectOne(url + '/csrf').flush({ token: 'preference-token' });
    const save = http.expectOne(url + '/default-world');
    expect(save.request.body).toEqual({ world: 'Secura' });
    expect(save.request.withCredentials).toBe(true);
    expect(save.request.headers.get('X-CSRF-TOKEN')).toBe('preference-token');
    save.flush({ defaultWorld: 'Secura' });
    harness.detectChanges();
    expect(element.textContent).toContain('Default world saved.');
  });

  it('asks anonymous visitors to log in on the account page', async () => {
    await open('/account');
    const element = harness.routeNativeElement!;
    expect(element.textContent).toContain('You need to log in');
    expect(element.querySelector('a')!.getAttribute('href')).toBe('/login');
    expect(element.querySelector('form')).toBeNull();
  });

  for (const mode of ['login', 'register']) {
    it(`submits the ${mode} page with credentials and CSRF protection`, async () => {
      await open(`/${mode}`);
      const element = harness.routeNativeElement!;
      const email = element.querySelector<HTMLInputElement>('#account-email')!;
      const password = element.querySelector<HTMLInputElement>('#account-password')!;
      email.value = 'player@example.com';
      email.dispatchEvent(new Event('input'));
      password.value = 'ValidPassword123!';
      password.dispatchEvent(new Event('input'));
      harness.detectChanges();
      element.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
      http.expectOne(`${url}/csrf`).flush({ token: 'token' });
      const write = http.expectOne(`${url}/${mode}`);
      expect(write.request.withCredentials).toBe(true);
      expect(write.request.headers.get('X-CSRF-TOKEN')).toBe('token');
      expect(write.request.body).toEqual({ email: 'player@example.com', password: 'ValidPassword123!' });
      write.flush({ id: 'user-1', email: 'player@example.com' });
      await harness.fixture.whenStable();
      const session = http.expectOne(`${url}/myaccount`);
      if (mode === 'login') session.flush({ id: 'user-1', email: 'player@example.com' });
      else session.flush(null, { status: 401, statusText: 'Unauthorized' });
      harness.detectChanges();
      await harness.fixture.whenStable();
      if (mode === 'login') {
        expect(TestBed.inject(Router).url).toBe('/account');
        expect(harness.routeNativeElement!.textContent).toContain('Signed in as player@example.com');
      } else {
        expect(TestBed.inject(Router).url).toContain('/login');
        expect(harness.routeNativeElement!.textContent).toContain('Account created. You can now log in.');
        expect(harness.routeNativeElement!.querySelector<HTMLInputElement>('#account-password')!.value).toBe('');
      }
    });
  }

  it('offers retry when session lookup fails', async () => {
    await harness.navigateByUrl('/account');
    http.expectOne(`${url}/myaccount`).flush(null, { status: 503, statusText: 'Unavailable' });
    harness.detectChanges();
    const element = harness.routeNativeElement!;
    expect(element.querySelector('[role="alert"]')?.textContent).toContain('Could not check');
    expect(element.querySelector('form')).toBeNull();
    element.querySelector('button')!.click();
    http.expectOne(`${url}/myaccount`).flush({ id: 'user-1', email: 'player@example.com' });
    harness.detectChanges();
    expect(element.textContent).toContain('player@example.com');
  });
});
