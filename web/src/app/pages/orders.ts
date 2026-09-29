import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Actions } from '../core/actions';
import { Api, ApiError } from '../core/api';
import { day, num, tone } from '../core/format';
import { Order } from '../core/models';
import { Session } from '../core/session';
import { load } from '../shared/load';
import { Pager } from '../shared/pager';
import { State } from '../shared/state';

@Component({
  selector: 'app-orders',
  imports: [RouterLink, State, Pager],
  template: `
    <header class="page-head">
      <div><p class="eyebrow">Planning</p><h1>Orders</h1><p class="muted">LTL shipments waiting for a truck. Open orders can be edited; planned orders can be dispatched.</p></div>
      <div class="head-actions"><button type="button" (click)="actions.newOrder()">New order</button></div>
    </header>
    <div class="toolbar">
      <div class="tabs" role="tablist" aria-label="Order status">
        @for (s of statuses; track s) {
          <button type="button" role="tab" [attr.aria-selected]="status() === s" [class.active]="status() === s" (click)="status.set(s); page.set(1)">{{ s || 'All' }}</button>
        }
      </div>
      <label class="filter"><span>Equipment</span>
        <select (change)="equipment.set($any($event.target).value); page.set(1)">
          <option value="">All</option>
          @for (e of session.meta()?.equipmentTypes ?? []; track e) { <option [value]="e">{{ e }}</option> }
        </select></label>
      <label class="filter"><span class="sr-only">Search orders</span>
        <input type="search" placeholder="Search order, customer or city" [value]="search()" (input)="search.set($any($event.target).value); page.set(1)" /></label>
    </div>

    <app-state [loading]="data.loading()" [error]="data.error()" [hasValue]="!!data.value()" (retry)="data.reload()" />
    @if (actionError()) { <div class="alert" role="alert">{{ actionError() }}</div> }
    @if (data.value(); as p) {
      <div class="table-wrap">
        <table>
          <thead><tr><th scope="col">Order</th><th scope="col">Customer</th><th scope="col">Route</th><th scope="col">Equipment</th>
            <th scope="col" class="num">Pallets</th><th scope="col" class="num">Weight</th><th scope="col" class="num">Priority</th>
            <th scope="col">Ready</th><th scope="col">Status</th><th scope="col"><span class="sr-only">Actions</span></th></tr></thead>
          <tbody>
            @for (o of p.items; track o.id) {
              <tr>
                <td><strong>{{ o.id }}</strong></td><td>{{ o.customer }}</td>
                <td>{{ o.origin }} → {{ o.destination }}</td><td>{{ o.equipment }}</td>
                <td class="num">{{ o.pallets }}</td><td class="num">{{ num(o.weight) }}</td><td class="num">{{ o.priority }}</td>
                <td>{{ day(o.readyOn) }}</td>
                <td><span class="badge" [attr.data-tone]="tone(o.status)">{{ o.status }}</span>
                  @if (o.planId) { <div class="small"><a [routerLink]="['/plans', o.planId]">{{ o.truckId }}</a></div> }</td>
                <td class="row-actions">
                  @if (o.status === 'Open') {
                    <button type="button" class="ghost small" (click)="actions.editOrder(o)">Edit</button>
                    <button type="button" class="ghost small danger" (click)="move(o, 'cancel')">Cancel</button>
                  }
                  @if (o.status === 'Planned') { <button type="button" class="small" (click)="move(o, 'dispatch')">Dispatch</button> }
                </td>
              </tr>
            } @empty { <tr><td colspan="10" class="empty-row">No orders match.</td></tr> }
          </tbody>
        </table>
      </div>
      <app-pager [page]="p.page" [pageSize]="p.pageSize" [total]="p.total" (go)="page.set($event)" />
    }`
})
export class OrdersPage {
  private readonly api = inject(Api);
  readonly actions = inject(Actions);
  readonly session = inject(Session);
  readonly statuses = ['', 'Open', 'Planned', 'Dispatched', 'Cancelled'];
  readonly status = signal('Open');
  readonly equipment = signal('');
  readonly search = signal('');
  readonly page = signal(1);
  readonly actionError = signal<string | null>(null);
  readonly data = load(() => this.api.orders({ status: this.status(), equipment: this.equipment(), search: this.search(), page: this.page(), pageSize: 25 }));
  readonly num = num;
  readonly day = day;
  readonly tone = tone;

  async move(o: Order, action: 'cancel' | 'dispatch'): Promise<void> {
    if (action === 'cancel' && !confirm(`Cancel ${o.id} for ${o.customer}?`)) return;
    this.actionError.set(null);
    try { await this.actions.moveOrder(o, action); } catch (e) { this.actionError.set(ApiError.from(e).message); }
  }
}
