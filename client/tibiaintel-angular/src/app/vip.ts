import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Subscription, finalize, switchMap } from 'rxjs';
import { VipEntry, VipGroup, VipLive, VipSnapshot, VipState, groupStatus } from './vip-live';

const api = 'http://localhost:5267/tools/vip';
@Component({ selector: 'app-vip', imports: [FormsModule, RouterLink], providers: [VipLive], templateUrl: './vip.html', styleUrl: './vip.scss' })
export class Vip {
  private readonly http = inject(HttpClient);
  private readonly destroy = inject(DestroyRef);
  private readonly live = inject(VipLive);
  private readonly state = new VipState();
  private liveSubscription?: Subscription;
  protected readonly list = signal<VipSnapshot | null>(null);
  protected readonly busy = signal(false);
  protected readonly loading = signal(true);
  protected readonly needsLogin = signal(false);
  protected readonly error = signal('');
  protected readonly connection = signal<'connecting' | 'connected' | 'reconnecting' | 'closed'>('connecting');
  protected readonly collapsed = signal(new Set<number>());
  protected readonly editing = signal<VipEntry | null>(null);
  protected readonly renaming = signal<number | null>(null);
  protected readonly ungrouped = computed(() => this.list()?.characters.filter(c => !c.groupIds.length) ?? []);
  protected name = '';
  protected groupName = '';
  protected renameName = '';
  protected chosenGroups: number[] = [];

  constructor() { this.load(true); }

  protected load(connect = false): void {
    this.loading.set(true);
    this.error.set('');
    this.http.get<VipSnapshot>(api, { withCredentials: true }).pipe(takeUntilDestroyed(this.destroy),
      finalize(() => this.loading.set(false))).subscribe({ next: value => {
        this.needsLogin.set(false);
        this.list.set(this.state.snapshot(value));
        if (connect) this.connect();
      }, error: error => this.showError(error) });
  }

  protected retry(): void { this.load(true); }

  private connect(): void {
    this.liveSubscription?.unsubscribe();
    this.connection.set('connecting');
    this.liveSubscription = this.live.connect().pipe(takeUntilDestroyed(this.destroy)).subscribe(event => {
      if (event.kind === 'connection') this.connection.set(event.value);
      else if (event.kind === 'snapshot') this.list.set(this.state.snapshot(event.value));
      else this.list.set(this.state.statuses(event.value));
    });
  }

  protected members(group: VipGroup): VipEntry[] { return this.list()?.characters.filter(c => c.groupIds.includes(group.id)) ?? []; }
  protected status(entry: VipEntry): string { return this.connection() === 'connected' ? entry.status : 'unknown'; }
  protected groupStatus(group: VipGroup): string { return groupStatus(this.members(group), this.connection() === 'connected'); }
  protected toggle(id: number): void {
    this.collapsed.update(old => { const next = new Set(old); next.has(id) ? next.delete(id) : next.add(id); return next; });
  }
  protected edit(entry: VipEntry): void { this.editing.set(entry); this.chosenGroups = [...entry.groupIds]; }
  protected choose(id: number, checked: boolean): void {
    this.chosenGroups = checked ? [...this.chosenGroups, id] : this.chosenGroups.filter(g => g !== id);
  }
  protected rename(group: VipGroup): void { this.renaming.set(group.id); this.renameName = group.name; }
  protected add(): void { this.write('POST', '/characters', { name: this.name }, () => { this.name = ''; }); }
  protected addGroup(): void { this.write('POST', '/groups', { name: this.groupName }, () => { this.groupName = ''; }); }
  protected saveRename(): void { this.write('PUT', `/groups/${this.renaming()}`, { name: this.renameName }, () => this.renaming.set(null)); }
  protected deleteGroup(group: VipGroup): void { this.write('DELETE', `/groups/${group.id}`); }
  protected remove(entry: VipEntry): void { this.write('DELETE', `/characters/${entry.id}`); }
  protected saveMemberships(): void {
    const entry = this.editing();
    if (entry) this.write('PUT', `/characters/${entry.id}/groups`, { groupIds: this.chosenGroups }, () => this.editing.set(null));
  }

  private write(method: string, path: string, body?: unknown, done?: () => void): void {
    if (this.busy()) return;
    this.busy.set(true); this.error.set('');
    this.http.get<{ token: string }>('http://localhost:5267/account/csrf', { withCredentials: true }).pipe(
      switchMap(csrf => this.http.request(method, api + path, { body, withCredentials: true, headers: { 'X-CSRF-TOKEN': csrf.token } })),
      switchMap(() => { done?.(); return this.http.get<VipSnapshot>(api, { withCredentials: true }); }),
      takeUntilDestroyed(this.destroy), finalize(() => this.busy.set(false)),
    ).subscribe({ next: value => this.list.set(this.state.snapshot(value)), error: error => this.showError(error) });
  }

  private showError(error: HttpErrorResponse): void {
    this.needsLogin.set(error.status === 401);
    if (error.status === 401) { this.liveSubscription?.unsubscribe(); this.connection.set('closed'); this.list.set(null); }
    this.error.set(error.status === 401 ? 'Log in to manage your private VIP list.'
      : error.error?.message || 'Could not update the VIP list. Please try again.');
  }
}
