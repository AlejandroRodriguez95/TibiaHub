import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Account } from './account';

describe('Account cookie flow', () => {
  const url = 'http://localhost:5267/account';
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Account], providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('restores a session with credentials and logs out using a fresh CSRF token', () => {
    const fixture = TestBed.createComponent(Account);
    const session = http.expectOne(`${url}/me`);
    expect(session.request.withCredentials).toBe(true);
    session.flush({ id: '1', email: 'player@example.com' });
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('Signed in as player@example.com');
    element.querySelector('button')!.click();
    const csrf = http.expectOne(`${url}/csrf`);
    expect(csrf.request.withCredentials).toBe(true);
    csrf.flush({ token: 'fresh-token' });
    const logout = http.expectOne(`${url}/logout`);
    expect(logout.request.withCredentials).toBe(true);
    expect(logout.request.headers.get('X-CSRF-TOKEN')).toBe('fresh-token');
    logout.flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();
    expect(element.textContent).toContain('You have logged out.');
    expect(element.querySelector('input[type="password"]')).toBeTruthy();
  });

  it('shows the login form for an anonymous session', () => {
    const fixture = TestBed.createComponent(Account);
    http.expectOne(`${url}/me`).flush(null, { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('form')).toBeTruthy();
    expect(element.querySelector('[role="alert"]')).toBeNull();
  });

  for (const mode of ['login', 'register']) {
    it(`submits ${mode} with credentials and CSRF protection`, async () => {
      const fixture = TestBed.createComponent(Account);
      http.expectOne(`${url}/me`).flush(null, { status: 401, statusText: 'Unauthorized' });
      fixture.detectChanges();
      const element = fixture.nativeElement as HTMLElement;
      if (mode === 'register') {
        (element.querySelectorAll('nav button')[1] as HTMLButtonElement).click();
        fixture.detectChanges();
      }
      await fixture.whenStable();
      const email = element.querySelector<HTMLInputElement>('#account-email')!;
      const password = element.querySelector<HTMLInputElement>('#account-password')!;
      email.value = 'player@example.com';
      email.dispatchEvent(new Event('input'));
      password.value = 'ValidPassword123!';
      password.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      element.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
      http.expectOne(`${url}/csrf`).flush({ token: 'token' });
      const write = http.expectOne(`${url}/${mode}`);
      expect(write.request.withCredentials).toBe(true);
      expect(write.request.headers.get('X-CSRF-TOKEN')).toBe('token');
      expect(write.request.body).toEqual({ email: 'player@example.com', password: 'ValidPassword123!' });
      write.flush({ id: '1', email: 'player@example.com' });
      fixture.detectChanges();
      await fixture.whenStable();
      if (mode === 'login') expect(element.textContent).toContain('Signed in as player@example.com');
      else {
        expect(element.textContent).toContain('Account created. You can now log in.');
        expect(element.querySelector<HTMLInputElement>('#account-password')!.value).toBe('');
      }
    });
  }

  it('offers retry when session lookup fails rather than assuming logout', () => {
    const fixture = TestBed.createComponent(Account);
    http.expectOne(`${url}/me`).flush(null, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('[role="alert"]')?.textContent).toContain('Could not check');
    expect(element.querySelector('form')).toBeNull();
  });
});
