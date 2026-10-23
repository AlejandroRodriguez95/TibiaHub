import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';

interface ScanResult {
  world: string; character: string; windowStart: string; historyStart: string | null;
  lastObservation: string | null; observedLogouts: number; usableLogouts: number;
  candidates: { name: string; score: number; firstMinuteMatches: number; secondMinuteMatches: number }[];
}
const scanUrl = '/api/tools/character-scan';

@Component({ selector: 'app-character-scan', imports: [FormsModule, RouterLink, DatePipe], templateUrl: './character-scan.html' })
export class CharacterScan {
  private readonly http = inject(HttpClient);
  private readonly destroy = inject(DestroyRef);
  protected readonly busy = signal(false);
  protected readonly needsLogin = signal(false);
  protected readonly error = signal('');
  protected readonly result = signal<ScanResult | null>(null);
  protected name = '';

  protected scan(): void {
    if (this.busy() || !this.name.trim()) return;
    this.busy.set(true);
    this.error.set('');
    this.result.set(null);
    this.http.get<ScanResult>(scanUrl, { withCredentials: true, params: { name: this.name.trim() } })
      .pipe(takeUntilDestroyed(this.destroy), finalize(() => this.busy.set(false)))
      .subscribe({ next: result => this.result.set(result), error: (error: HttpErrorResponse) => this.showError(error) });
  }

  private showError(error: HttpErrorResponse): void {
    this.needsLogin.set(error.status === 401);
    this.error.set(error.status === 401 ? 'Log in to scan a character.' : error.error?.message || 'Could not load scan data. Please try again.');
  }
}
