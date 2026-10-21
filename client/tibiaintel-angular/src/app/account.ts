import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize, switchMap } from 'rxjs';

interface AccountUser { id: string; email: string; isAdmin: boolean; defaultWorld?: string | null; worlds?: string[]; }
const accountUrl = 'http://localhost:5267/account';

@Component({
  selector: 'app-account',
  imports: [FormsModule, RouterLink],
  templateUrl: './account.html',
})
export class Account {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly user = signal<AccountUser | null>(null);
  protected readonly checking = signal(true);
  protected readonly sessionError = signal('');
  protected readonly busy = signal(false);
  protected readonly mode = signal<'login' | 'register' | 'account'>(this.route.snapshot.data['mode'] ?? 'login');
  protected readonly error = signal('');
  protected readonly message = signal('');
  protected defaultWorld = '';
  protected readonly savingWorld = signal(false);
  protected email = '';
  protected password = '';

  constructor() {
    if (this.route.snapshot.queryParamMap.has('registered')) this.message.set('Account created. You can now log in.');
    if (this.route.snapshot.queryParamMap.has('loggedOut')) this.message.set('You have logged out.');
    this.loadSession();
  }

  protected loadSession(): void {
    this.checking.set(true);
    this.sessionError.set('');
    this.http.get<AccountUser>(`${accountUrl}/myaccount`, { withCredentials: true })
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.checking.set(false)))
      .subscribe({
        next: user => { this.user.set(user); this.defaultWorld = user.defaultWorld ?? ''; },
        error: (error: HttpErrorResponse) => {
          this.user.set(null);
          if (error.status !== 401) this.sessionError.set('Could not check your session. Make sure the API is running.');
        },
      });
  }

  protected submit(): void {
    if (this.busy() || this.mode() === 'account') return;
    const mode = this.mode();
    const credentials = { email: this.email, password: this.password };
    this.busy.set(true);
    this.error.set('');
    this.message.set('');
    this.write<AccountUser>(mode, credentials)
      .pipe(finalize(() => { this.busy.set(false); this.password = ''; }))
      .subscribe({
        next: user => {
          if (mode === 'login') {
            this.user.set(user);
            void this.router.navigate(['/account']);
          }
          else {
            void this.router.navigate(['/login'], { queryParams: { registered: 'true' } });
          }
        },
        error: (error: HttpErrorResponse) => this.showError(error),
      });
  }

  protected saveDefaultWorld(): void {
    if (this.savingWorld()) return;
    this.savingWorld.set(true);
    this.error.set('');
    this.message.set('');
    this.write<{ defaultWorld: string | null }>('default-world', { world: this.defaultWorld || null })
      .pipe(finalize(() => this.savingWorld.set(false)))
      .subscribe({
        next: result => {
          this.defaultWorld = result.defaultWorld ?? '';
          this.user.update(user => user ? { ...user, defaultWorld: result.defaultWorld } : null);
          this.message.set('Default world saved.');
        },
        error: (error: HttpErrorResponse) => this.showError(error),
      });
  }

  protected logout(): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.write<void>('logout', null)
      .pipe(finalize(() => this.busy.set(false)))
      .subscribe({
        next: () => {
          this.user.set(null);
          void this.router.navigate(['/login'], { queryParams: { loggedOut: 'true' } });
        },
        error: (error: HttpErrorResponse) => {
          if (error.status === 401) this.user.set(null);
          else this.showError(error);
        },
      });
  }

  private write<T>(action: string, body: unknown) {
    // Fetch for each write: antiforgery tokens are tied to the current identity.
    return this.http.get<{ token: string }>(`${accountUrl}/csrf`, { withCredentials: true })
      .pipe(switchMap(({ token }) => this.http.post<T>(`${accountUrl}/${action}`, body, {
        withCredentials: true, headers: { 'X-CSRF-TOKEN': token },
      })), takeUntilDestroyed(this.destroyRef));
  }

  private showError(error: HttpErrorResponse): void {
    if (error.status === 401) {
      this.error.set('Login failed. Check your credentials or try again later.');
      return;
    }
    const errors = error.error?.errors;
    const messages: string[] = Array.isArray(errors)
      ? errors.map((item: { description: string }) => item.description)
      : errors && typeof errors === 'object' ? Object.values(errors).flat() as string[] : [];
    this.error.set(messages.join(' ') || 'The request failed. Please try again.');
  }
}
