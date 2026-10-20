import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Admin } from './admin';

describe('Admin panel', () => {
  const url = 'http://localhost:5267/admin/users';
  beforeEach(() => TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('loads and displays registered emails using the session cookie', async () => {
    const fixture = TestBed.createComponent(Admin);
    const request = TestBed.inject(HttpTestingController).expectOne(url);
    expect(request.request.withCredentials).toBe(true);
    request.flush([{ id: '1', email: 'admin@example.com' }, { id: '2', email: 'player@example.com' }]);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('tbody tr').length).toBe(2);
    expect(fixture.nativeElement.textContent).toContain('player@example.com');
  });

  for (const status of [401, 403]) {
    it(`handles ${status} without displaying user data`, () => {
      const fixture = TestBed.createComponent(Admin);
      TestBed.inject(HttpTestingController).expectOne(url).flush(null, { status, statusText: 'Denied' });
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeTruthy();
      expect(fixture.nativeElement.querySelector('table')).toBeNull();
      expect(fixture.nativeElement.querySelector('a[href="/login"]') !== null).toBe(status === 401);
    });
  }

  it('shows an empty state when no users exist', () => {
    const fixture = TestBed.createComponent(Admin);
    TestBed.inject(HttpTestingController).expectOne(url).flush([]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No users have registered yet.');
  });
});
