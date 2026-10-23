import { HttpClient } from '@angular/common/http';
import { Component, computed, inject, OnDestroy, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';

interface PurchaseItem {
  name: string;
  npcPrice: number;
  averageMarketValue: number | null;
  isMarketHigher: boolean;
  marketSourceUpdatedAt: string | null;
  iconUrl: string | null;
  isDeliveryTask: boolean;
}
interface NpcPurchases { name: string; items: PurchaseItem[]; }
interface Catalog {
  updatedAt: string;
  isStale: boolean;
  marketValuesArePlaceholders: boolean;
  worlds: string[];
  selectedWorld: string | null;
  marketAvailable: boolean;
  marketUpdatedAt: string | null;
  marketSourceOldestAt: string | null;
  marketIsStale: boolean;
  marketRefreshPending: boolean;
  marketAverageBasis: 'buy' | 'sell';
  npcs: NpcPurchases[];
}

@Component({
  selector: 'app-tools',
  imports: [FormsModule, DecimalPipe],
  templateUrl: './tools.html',
})
export class Tools implements OnDestroy {
  private readonly http = inject(HttpClient);
  private request?: Subscription;
  protected readonly catalog = signal<Catalog | null>(null);
  protected readonly selectedName = signal('');
  protected readonly selectedWorld = signal('');
  protected readonly loading = signal(false);
  protected readonly error = signal('');
  protected readonly brokenIcons = signal(new Set<string>());
  protected readonly selectedNpc = computed(() => this.catalog()?.npcs.find(n => n.name === this.selectedName()));

  protected readonly deliverySort = signal<'none' | 'first' | 'last'>('none');
  protected readonly sortedItems = computed(() => {
    const items = this.selectedNpc()?.items ?? [];
    const order = this.deliverySort();
    if (order === 'none') return items;
    return [...items].sort((a, b) => order === 'first'
      ? Number(b.isDeliveryTask) - Number(a.isDeliveryTask)
      : Number(a.isDeliveryTask) - Number(b.isDeliveryTask));
  });

  protected toggleDeliverySort(): void {
    this.deliverySort.update(order => order === 'first' ? 'last' : 'first');
  }

  constructor() { this.load(); }
  ngOnDestroy(): void { this.request?.unsubscribe(); }

  protected load(): void {
    if (this.loading()) return;
    this.loading.set(true);
    this.error.set('');
    const world = this.selectedWorld();
    this.request = this.http.get<Catalog>('/api/tools/npc-selling', {
      withCredentials: true,
      params: world ? { world } : {},
    }).subscribe({
      next: catalog => {
        this.catalog.set(catalog);
        this.selectedWorld.set(catalog.selectedWorld ?? '');
        if (!catalog.npcs.some(n => n.name === this.selectedName())) this.selectedName.set('');
        this.loading.set(false);
      },
      error: response => {
        this.error.set(response.status === 503
          ? 'NPC prices are not available yet. The collector is fetching data or waiting to retry. Try again shortly.'
          : 'We could not load NPC prices. Please try again.');
        this.loading.set(false);
      },
    });
  }

  protected changeWorld(world: string): void {
    this.selectedWorld.set(world);
    // Clear the old world's prices immediately, including during failed requests.
    this.catalog.update(catalog => catalog ? { ...catalog, selectedWorld: null, marketAvailable: false,
      npcs: catalog.npcs.map(npc => ({ ...npc, items: npc.items.map(item => ({ ...item, averageMarketValue: null, isMarketHigher: false, marketSourceUpdatedAt: null })) })) } : null);
    this.load();
  }

  protected daysAgo(value: string | null): string {
    if (!value) return 'unknown';
    const timestamp = Date.parse(value);
    if (!Number.isFinite(timestamp)) return 'unknown';
    const days = Math.max(0, Math.floor((Date.now() - timestamp) / 86_400_000));
    return days + (days === 1 ? ' day ago' : ' days ago');
  }

  protected iconFailed(name: string): void {
    this.brokenIcons.update(current => new Set([...current, name]));
  }
}
