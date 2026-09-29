import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../core/api';
import { num } from '../core/format';
import { Bar, BarChart } from '../shared/bar-chart';
import { load } from '../shared/load';
import { State } from '../shared/state';

@Component({
  selector: 'app-dashboard',
  imports: [RouterLink, State, BarChart],
  template: `
    <header class="page-head">
      <div><p class="eyebrow">Overview</p><h1>Dashboard</h1>
        <p class="muted">Open freight against available capacity, and what Yard Ops reports on the ground.</p></div>
      <div class="head-actions"><a class="button" routerLink="/plans/new">Build a plan</a></div>
    </header>

    <app-state [loading]="data.loading()" [error]="data.error()" [hasValue]="!!data.value()" (retry)="data.reload()" />
    @if (data.value(); as d) {
      <section class="stat-grid" aria-label="Key numbers">
        <a class="stat" routerLink="/orders"><span>Open orders</span><strong>{{ d.openOrders }}</strong><small>{{ d.openPallets }} pallets · {{ num(d.openWeight) }} lb</small></a>
        <a class="stat" routerLink="/trucks"><span>Active trucks</span><strong>{{ d.activeTrucks }}</strong><small>{{ d.fleetPallets }} pallet capacity</small></a>
        <a class="stat" routerLink="/plans"><span>Draft plans</span><strong>{{ d.draftPlans }}</strong><small>{{ d.committedPlans }} committed</small></a>
        <a class="stat" routerLink="/orders"><span>Planned orders</span><strong>{{ d.plannedOrders }}</strong><small>{{ d.dispatchedOrders }} dispatched</small></a>
        <a class="stat" routerLink="/yard"><span>Trailers on yard</span><strong>{{ d.trailersOnYard }}</strong><small>{{ d.trailersReady }} ready for loading</small></a>
      </section>

      <section class="card">
        <div class="card-head"><div><h2>Open pallets vs. capacity by equipment</h2>
          <p class="muted">Where demand exceeds the active fleet, some orders will not fit in one plan.</p></div></div>
        <app-bar-chart [bars]="bars()" label="Open pallets and capacity by equipment" />
      </section>
    }`
})
export class DashboardPage {
  private readonly api = inject(Api);
  readonly data = load(() => this.api.dashboard());
  readonly num = num;
  readonly bars = computed<Bar[]>(() => (this.data.value()?.byEquipment ?? []).flatMap(e => [
    { label: `${e.equipment} · open`, value: e.openPallets, display: `${e.openPallets} pallets (${e.openOrders} orders)` },
    { label: `${e.equipment} · capacity`, value: e.palletCapacity, display: `${e.palletCapacity} pallets (${e.trucks} trucks)` }
  ]));
}
