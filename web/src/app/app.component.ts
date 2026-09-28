import { HttpClient } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

type Order = { id: string; customer: string; origin: string; destination: string; pallets: number; weight: number; equipment: string; priority: number; assigned: boolean };
type Truck = { id: string; equipment: string; palletCapacity: number; weightCapacity: number; currentLocation: string };
type PlannedTruck = { truckId: string; equipment: string; orders: Order[]; usedPallets: number; palletCapacity: number; usedWeight: number; weightCapacity: number; utilization: number; explanations: string[] };
type Plan = { id: string; createdAt: string; trucks: PlannedTruck[]; unassignedOrders: Order[]; algorithm: string };
type YardEvent = { eventId: string; eventType: string; trailerNumber: string; occurredAt: string; details?: string };

@Component({ selector: 'app-root', standalone: true, templateUrl: './app.component.html' })
export class AppComponent implements OnInit {
  private readonly http = inject(HttpClient);
  readonly orders = signal<Order[]>([]);
  readonly trucks = signal<Truck[]>([]);
  readonly plan = signal<Plan | null>(null);
  readonly yardEvents = signal<YardEvent[]>([]);
  readonly building = signal(false);

  async ngOnInit(): Promise<void> {
    await Promise.all([this.refreshCore(), this.refreshYardEvents()]);
  }

  async refreshCore(): Promise<void> {
    const [orders, trucks] = await Promise.all([
      firstValueFrom(this.http.get<Order[]>('/api/orders')),
      firstValueFrom(this.http.get<Truck[]>('/api/trucks'))
    ]);
    this.orders.set(orders);
    this.trucks.set(trucks);
  }

  async buildPlan(): Promise<void> {
    this.building.set(true);
    try {
      this.plan.set(await firstValueFrom(this.http.post<Plan>('/api/plans/build', { orderIds: [], truckIds: [] })));
    } finally {
      this.building.set(false);
    }
  }

  async refreshYardEvents(): Promise<void> {
    this.yardEvents.set(await firstValueFrom(this.http.get<YardEvent[]>('/api/integrations/v1/yard/events')));
  }

  orderIds(orders: Order[]): string {
    return orders.map(order => order.id).join(', ');
  }
}
