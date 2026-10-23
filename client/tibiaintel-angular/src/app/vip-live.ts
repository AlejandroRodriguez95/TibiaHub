import { Injectable } from '@angular/core';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { Observable } from 'rxjs';

export type VipStatus = 'online' | 'offline' | 'unknown';
export interface VipEntry { id: number; name: string; world: string; groupIds: number[]; status: VipStatus; lastObservedAt: string | null; }
export interface VipGroup { id: number; name: string; }
export interface VipSnapshot { capturedAt: string; maxCharacters: number; maxGroups: number; characters: VipEntry[]; groups: VipGroup[]; }
export interface VipStatusUpdate { capturedAt: string; characters: Pick<VipEntry, 'id' | 'status' | 'lastObservedAt'>[]; }
export type VipEvent = { kind: 'snapshot'; value: VipSnapshot } | { kind: 'statuses'; value: VipStatusUpdate }
  | { kind: 'connection'; value: 'connected' | 'reconnecting' | 'closed' };

export function groupStatus(entries: VipEntry[], connected = true): VipStatus {
  if (!connected || !entries.length) return 'unknown';
  if (entries.some(c => c.status === 'online')) return 'online';
  return entries.some(c => c.status === 'unknown') ? 'unknown' : 'offline';
}

// A reconnect snapshot and a status event can arrive in either order.
export class VipState {
  value: VipSnapshot | null = null;
  private readonly latest = new Map<number, { at: string; status: VipStatus; lastObservedAt: string | null }>();

  snapshot(next: VipSnapshot): VipSnapshot {
    if (this.value && next.capturedAt < this.value.capturedAt) return this.value;
    const ids = new Set(next.characters.map(c => c.id));
    for (const id of this.latest.keys()) if (!ids.has(id)) this.latest.delete(id);
    this.value = { ...next, characters: next.characters.map(c => {
      const newer = this.latest.get(c.id);
      if (newer && newer.at > next.capturedAt) return { ...c, status: newer.status, lastObservedAt: newer.lastObservedAt };
      this.latest.set(c.id, { at: next.capturedAt, status: c.status, lastObservedAt: c.lastObservedAt });
      return c;
    }) };
    return this.value;
  }

  statuses(update: VipStatusUpdate): VipSnapshot | null {
    for (const c of update.characters) {
      const last = this.latest.get(c.id);
      if (!last || update.capturedAt >= last.at) this.latest.set(c.id, { at: update.capturedAt, ...c });
    }
    if (this.value) this.value = { ...this.value, characters: this.value.characters.map(c => {
      const last = this.latest.get(c.id)!;
      return { ...c, status: last.status, lastObservedAt: last.lastObservedAt };
    }) };
    return this.value;
  }
}

@Injectable()
export class VipLive {
  connect(): Observable<VipEvent> {
    return new Observable(observer => {
      let disposed = false;
      const connection = new HubConnectionBuilder()
        .withUrl('http://localhost:5267/hubs/vip', { withCredentials: true })
        .withAutomaticReconnect().configureLogging(LogLevel.Error).build();
      const snapshot = async () => {
        const value = await connection.invoke<VipSnapshot>('Snapshot');
        if (!disposed) {
          observer.next({ kind: 'snapshot', value });
          observer.next({ kind: 'connection', value: 'connected' });
        }
      };
      connection.on('Snapshot', (value: VipSnapshot) => observer.next({ kind: 'snapshot', value }));
      connection.on('Statuses', (value: VipStatusUpdate) => observer.next({ kind: 'statuses', value }));
      connection.onreconnecting(() => observer.next({ kind: 'connection', value: 'reconnecting' }));
      connection.onreconnected(() => { void snapshot().catch(() => observer.next({ kind: 'connection', value: 'closed' })); });
      connection.onclose(() => observer.next({ kind: 'connection', value: 'closed' }));
      void connection.start().then(async () => {
        if (disposed) { await connection.stop(); return; }
        await snapshot();
      }).catch(() => { if (!disposed) observer.next({ kind: 'connection', value: 'closed' }); });
      return () => { disposed = true; void connection.stop(); };
    });
  }
}
