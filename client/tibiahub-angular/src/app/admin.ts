import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';

interface AdminUser { id: string; email: string | null; }

@Component({
  selector: 'app-admin',
  imports: [RouterLink],
  templateUrl: './admin.html',
})
export class Admin {
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly users = signal<AdminUser[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal('');
  protected readonly status = signal(0);

  constructor() { this.loadUsers(); }

  protected loadUsers(): void {
    this.loading.set(true);
    this.error.set('');
    this.status.set(0);
    this.users.set([]);
    this.http.get<AdminUser[]>('http://localhost:5267/admin/users', { withCredentials: true })
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.loading.set(false)))
      .subscribe({
        next: users => this.users.set(users),
        error: (error: HttpErrorResponse) => {
          this.status.set(error.status);
          this.error.set(error.status === 401 ? 'Log in with an admin account to view this panel.'
            : error.status === 403 ? 'Your account does not have admin access.'
            : 'Could not load registered users. Please try again.');
        },
      });
  }
}
