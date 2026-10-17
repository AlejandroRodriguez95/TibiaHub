import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { finalize, switchMap } from 'rxjs';

interface AccountUser { id: string; email: string; }
const accountUrl = 'http://localhost:5267/account';

@Component({
  selector: 'app-account',
  imports: [FormsModule],
  template: `
    <section aria-labelledby="account-title">
      <h2 id="account-title">Your account</h2>
      @if (checking()) {
        <p role="status">Checking your session…</p>
      } @else if (sessionError()) {
        <p role="alert">{{ sessionError() }}</p>
        <button type="button" (click)="loadSession()">Retry</button>
      } @else if (user(); as user) {
        <p>Signed in as {{ user.email }}</p>
        <button type="button" (click)="logout()" [disabled]="busy()">Log out</button>
      } @else {
        <nav aria-label="Account options">
          <button type="button" (click)="setMode('login')" [disabled]="busy()"
            [attr.aria-pressed]="mode() === 'login'">Log in</button>
          <button type="button" (click)="setMode('register')" [disabled]="busy()"
            [attr.aria-pressed]="mode() === 'register'">Register</button>
        </nav>
        <form #form="ngForm" (ngSubmit)="submit()">
          <label for="account-email">Email</label>
          <input id="account-email" name="email" type="email" required email
            autocomplete="username" [(ngModel)]="email" [disabled]="busy()" />
          <label for="account-password">Password</label>
          <input id="account-password" name="password" type="password" required
            [attr.autocomplete]="mode() === 'register' ? 'new-password' : 'current-password'"
            [(ngModel)]="password" [disabled]="busy()" />
          @if (mode() === 'register') {
            <p>Use at least 6 characters, including uppercase, lowercase, a number, and a symbol.</p>
          }
          <button type="submit" [disabled]="form.invalid || busy()">
            {{ busy() ? 'Please wait…' : mode() === 'login' ? 'Log in' : 'Create account' }}
          </button>
        </form>
      }
      @if (message()) { <p role="status">{{ message() }}</p> }
      @if (error()) { <p role="alert">{{ error() }}</p> }
    </section>
  `,
  styles: `
    :host { display: block; margin-top: 28px; border-top: 1px solid #c9bfad; padding-top: 24px; }
    section, form { display: grid; gap: 12px; }
    h2, p { margin: 0; }
    nav { display: flex; gap: 10px; }
    input, button { font: inherit; padding: 10px 14px; border-radius: 6px; }
    input { min-width: 0; border: 1px solid #c9bfad; background: #fffdf8; }
    button { border: 0; color: white; background: #315c3c; cursor: pointer; }
    button:disabled { opacity: .65; cursor: default; }
    [aria-pressed="true"] { outline: 2px solid #315c3c; outline-offset: 2px; }
    [role="alert"] { color: #8a2f20; }
  `,
})
export class Account {
  private readonly http = inject(HttpClient);
  protected readonly user = signal<AccountUser | null>(null);
  protected readonly checking = signal(true);
  protected readonly sessionError = signal('');
  protected readonly busy = signal(false);
  protected readonly mode = signal<'login' | 'register'>('login');
  protected readonly error = signal('');
  protected readonly message = signal('');
  protected email = '';
  protected password = '';

  constructor() { this.loadSession(); }

  protected loadSession(): void {
    this.checking.set(true);
    this.sessionError.set('');
    this.http.get<AccountUser>(`${accountUrl}/me`, { withCredentials: true })
      .pipe(finalize(() => this.checking.set(false)))
      .subscribe({
        next: user => this.user.set(user),
        error: (error: HttpErrorResponse) => {
          this.user.set(null);
          if (error.status !== 401) this.sessionError.set('Could not check your session. Make sure the API is running.');
        },
      });
  }

  protected setMode(mode: 'login' | 'register'): void {
    this.mode.set(mode);
    this.password = '';
    this.error.set('');
    this.message.set('');
  }

  protected submit(): void {
    if (this.busy()) return;
    const mode = this.mode();
    const credentials = { email: this.email, password: this.password };
    this.busy.set(true);
    this.error.set('');
    this.message.set('');
    this.write<AccountUser>(mode, credentials)
      .pipe(finalize(() => { this.busy.set(false); this.password = ''; }))
      .subscribe({
        next: user => {
          if (mode === 'login') this.user.set(user);
          else {
            this.mode.set('login');
            this.message.set('Account created. You can now log in.');
          }
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
        next: () => { this.user.set(null); this.message.set('You have logged out.'); },
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
      })));
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
