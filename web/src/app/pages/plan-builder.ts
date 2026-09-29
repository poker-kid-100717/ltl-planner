import { Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Api, ApiError } from '../core/api';
import { num } from '../core/format';
import { Order, Plan, Truck } from '../core/models';
import { Session } from '../core/session';
import { load } from '../shared/load';
import { PlanView } from '../shared/plan-view';
import { State } from '../shared/state';

@Component({
  selector: 'app-plan-builder',
  imports: [RouterLink, State, PlanView],
  template: `
    <header class="page-head">
      <div><p class="eyebrow">Planning</p><h1>Plan Builder</h1>
        <p class="muted">Choose open orders and active trucks, build a draft, review why each order went where it did, then commit it.</p></div>
    </header>

    <app-state [loading]="data.loading()" [error]="data.error()" [hasValue]="!!data.value()" (retry)="data.reload()" />
    @if (error()) { <div class="alert" role="alert">{{ error() }}</div> }

    @if (data.value(); as d) {
      @if (!draft()) {
        <div class="two-col">
          <section class="card">
            <div class="card-head"><h2>Open orders ({{ selectedOrders().size }}/{{ d.orders.length }})</h2>
              <button type="button" class="ghost small" (click)="toggleAll('orders', d.orders)">{{ selectedOrders().size === d.orders.length ? 'Select none' : 'Select all' }}</button></div>
            <ul class="pick-list">
              @for (o of d.orders; track o.id) {
                <li><label><input type="checkbox" [checked]="selectedOrders().has(o.id)" (change)="toggle('orders', o.id)" />
                  <span><strong>{{ o.id }}</strong> · {{ o.customer }} · <span class="badge" data-tone="neutral">{{ o.equipment }}</span>
                    <span class="muted small" style="display:block">{{ o.origin }} → {{ o.destination }} · {{ o.pallets }} pallets · {{ num(o.weight) }} lb · priority {{ o.priority }}</span></span></label></li>
              } @empty { <li class="empty-row">No open orders. <a routerLink="/orders">Add an order</a>.</li> }
            </ul>
          </section>
          <section class="card">
            <div class="card-head"><h2>Active trucks ({{ selectedTrucks().size }}/{{ d.trucks.length }})</h2>
              <button type="button" class="ghost small" (click)="toggleAll('trucks', d.trucks)">{{ selectedTrucks().size === d.trucks.length ? 'Select none' : 'Select all' }}</button></div>
            <ul class="pick-list">
              @for (t of d.trucks; track t.id) {
                <li><label><input type="checkbox" [checked]="selectedTrucks().has(t.id)" (change)="toggle('trucks', t.id)" />
                  <span><strong>{{ t.id }}</strong> · {{ t.equipment }}
                    <span class="muted small" style="display:block">{{ t.palletCapacity }} pallets · {{ num(t.weightCapacity) }} lb · {{ t.currentLocation }}</span></span></label></li>
              } @empty { <li class="empty-row">No active trucks. <a routerLink="/trucks">Add a truck</a>.</li> }
            </ul>
          </section>
        </div>
        <div class="sticky-actions">
          <button type="button" (click)="build()" [disabled]="busy() || !selectedOrders().size || !selectedTrucks().size">{{ busy() ? 'Building…' : 'Build draft plan' }}</button>
          <span class="muted small">Selected: {{ selectedPallets() }} pallets across {{ selectedOrders().size }} orders; {{ selectedCapacity() }} pallets of truck capacity.</span>
        </div>
      } @else {
        <section class="card">
          <div class="card-head">
            <div><h2>Draft plan</h2><p class="muted">{{ plannedCount() }} orders on {{ draft()!.trucks.length }} trucks; {{ draft()!.unassignedOrders.length }} not placed. Nothing changes until you commit.</p></div>
            <div class="button-row">
              <button type="button" (click)="commit()" [disabled]="busy() || !draft()!.trucks.length">Commit plan</button>
              <button type="button" class="ghost" (click)="discard()" [disabled]="busy()">Discard</button>
            </div>
          </div>
        </section>
        <app-plan-view [plan]="draft()!" />
      }
    }`
})
export class PlanBuilderPage {
  private readonly api = inject(Api);
  private readonly session = inject(Session);
  private readonly router = inject(Router);
  readonly selectedOrders = signal(new Set<string>());
  readonly selectedTrucks = signal(new Set<string>());
  readonly draft = signal<Plan | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly num = num;

  readonly data = load(async () => {
    const [orders, trucks] = await Promise.all([this.api.orders({ status: 'Open', pageSize: 100 }), this.api.trucks(true)]);
    this.selectedOrders.set(new Set(orders.items.map(o => o.id)));
    this.selectedTrucks.set(new Set(trucks.map(t => t.id)));
    return { orders: orders.items, trucks };
  });

  readonly selectedPallets = computed(() => (this.data.value()?.orders ?? []).filter(o => this.selectedOrders().has(o.id)).reduce((s, o) => s + o.pallets, 0));
  readonly selectedCapacity = computed(() => (this.data.value()?.trucks ?? []).filter(t => this.selectedTrucks().has(t.id)).reduce((s, t) => s + t.palletCapacity, 0));
  readonly plannedCount = computed(() => this.draft()?.trucks.reduce((s, t) => s + t.orders.length, 0) ?? 0);

  toggle(kind: 'orders' | 'trucks', id: string): void {
    const target = kind === 'orders' ? this.selectedOrders : this.selectedTrucks;
    target.update(set => { const next = new Set(set); next.has(id) ? next.delete(id) : next.add(id); return next; });
  }

  toggleAll(kind: 'orders' | 'trucks', items: (Order | Truck)[]): void {
    const target = kind === 'orders' ? this.selectedOrders : this.selectedTrucks;
    target.set(target().size === items.length ? new Set() : new Set(items.map(i => i.id)));
  }

  private async run(work: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try { await work(); } catch (e) { this.error.set(ApiError.from(e).message); } finally { this.busy.set(false); }
  }

  build = () => this.run(async () => {
    this.draft.set(await this.api.buildPlan([...this.selectedOrders()], [...this.selectedTrucks()]));
  });

  commit = () => this.run(async () => {
    const plan = await this.api.commitPlan(this.draft()!.id);
    this.session.changed();
    await this.router.navigate(['/plans', plan.id]);
  });

  discard = () => this.run(async () => {
    await this.api.discardPlan(this.draft()!.id);
    this.draft.set(null);
    this.session.changed();
  });
}
