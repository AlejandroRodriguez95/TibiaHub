import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Tools } from './tools';

describe('NPC selling tool', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [Tools], providers: [provideHttpClient(), provideHttpClientTesting()],
  }));

  it('waits for selection and supports delivery and market indicators together', async () => {
    const fixture = TestBed.createComponent(Tools);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('http://localhost:5267/tools/npc-selling').flush({
      updatedAt: '2026-10-20T08:30:00Z', isStale: false, marketValuesArePlaceholders: false,
      worlds: ['Antica'], selectedWorld: 'Antica', marketAvailable: true, marketAverageBasis: 'buy',
      npcs: [{ name: 'Rashid', items: [
        { name: 'Golden Legs', npcPrice: 30000, averageMarketValue: 35000, iconUrl: null, isDeliveryTask: true, isMarketHigher: true },
        { name: 'Other Item', npcPrice: 100, averageMarketValue: null, iconUrl: null, isDeliveryTask: false, isMarketHigher: false },
      ] }],
    });
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('tbody')).toBeNull();
    expect(element.textContent).toContain('Monthly buy average');
    const select = element.querySelector<HTMLSelectElement>('#npc-select')!;
    select.value = 'Rashid';
    select.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(element.querySelectorAll('tbody tr')).toHaveLength(2);
    expect(element.querySelectorAll('.delivery-task')).toHaveLength(1);
    expect(element.querySelector('.delivery-task')?.textContent).toContain('Golden Legs');
    expect(element.querySelector('.task-badge')?.textContent).toContain('Delivery task');
    expect(element.querySelector('.delivery-task .market-higher')?.textContent).toContain('35,000');
    expect(element.querySelector('tbody')?.textContent).toContain('Unavailable');
    expect(element.querySelector('tbody')?.textContent).toContain('30,000');
    http.verify();
  });

  it('reloads cached values for a new world and clears previous comparisons on failure', async () => {
    const fixture = TestBed.createComponent(Tools);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('http://localhost:5267/tools/npc-selling').flush({ updatedAt: '2026-10-20T08:30:00Z', worlds: ['Antica', 'Secura'], selectedWorld: 'Antica', marketAvailable: true, npcs: [] });
    fixture.detectChanges();
    const select = (fixture.nativeElement as HTMLElement).querySelector<HTMLSelectElement>('#world-select')!;
    select.value = 'Secura';
    select.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    http.expectOne('http://localhost:5267/tools/npc-selling?world=Secura').flush({}, { status: 500, statusText: 'Failed' });
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('[role="alert"]')).toBeTruthy();
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Monthly buy average');
    http.verify();
  });

  it('explains initial collection and permits retry', () => {
    const fixture = TestBed.createComponent(Tools);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('http://localhost:5267/tools/npc-selling').flush({}, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('[role="alert"]')?.textContent).toContain('not available yet');
    element.querySelector('button')!.click();
    http.expectOne('http://localhost:5267/tools/npc-selling').flush({ updatedAt: '2026-10-20T08:30:00Z', npcs: [], isStale: false });
    fixture.detectChanges();
    expect(element.textContent).toContain('No NPCs have been configured');
    http.verify();
  });
});
