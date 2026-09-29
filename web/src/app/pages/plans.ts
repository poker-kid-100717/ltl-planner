import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../core/api';
import { dateTime, tone } from '../core/format';
import { load } from '../shared/load';
import { Pager } from '../shared/pager';
import { State } from '../shared/state';

@Component({
  selector: 'app-plans',
  imports: [RouterLink, State, Pager],
  template: `
    <header class="page-head">
      <div><p class="eyebrow">Planning</p><h1>Plans</h1><p class="muted">Every plan the planner has built, with what happened to it.</p></div>
      <div class="head-actions"><a class="button" routerLink="/plans/new">Build a plan</a></div>
    </header>
    <div class="tabs" role="tablist" aria-label="Plan status">
      @for (s of statuses; track s) {
        <button type="button" role="tab" [attr.aria-selected]="status() === s" [class.active]="status() === s" (click)="status.set(s); page.set(1)">{{ s || 'All' }}</button>
      }
    </div>
    <app-state [loading]="data.loading()" [error]="data.error()" [hasValue]="!!data.value()" (retry)="data.reload()" />
    @if (data.value(); as p) {
      <div class="table-wrap">
        <table>
          <thead><tr><th scope="col">Built</th><th scope="col">Status</th><th scope="col" class="num">Trucks</th><th scope="col" class="num">Orders placed</th>
            <th scope="col" class="num">Not placed</th><th scope="col">Decided</th></tr></thead>
          <tbody>
            @for (plan of p.items; track plan.id) {
              <tr>
                <td><a class="strong-link" [routerLink]="['/plans', plan.id]">{{ dateTime(plan.createdAt) }}</a></td>
                <td><span class="badge" [attr.data-tone]="tone(plan.status)">{{ plan.status }}</span></td>
                <td class="num">{{ plan.truckCount }}</td><td class="num">{{ plan.plannedOrders }}</td><td class="num">{{ plan.unassignedOrders }}</td>
                <td>{{ dateTime(plan.decidedAt) }}</td>
              </tr>
            } @empty { <tr><td colspan="6" class="empty-row">No plans yet. <a routerLink="/plans/new">Build the first one</a>.</td></tr> }
          </tbody>
        </table>
      </div>
      <app-pager [page]="p.page" [pageSize]="p.pageSize" [total]="p.total" (go)="page.set($event)" />
    }`
})
export class PlansPage {
  private readonly api = inject(Api);
  readonly statuses = ['', 'Draft', 'Committed', 'Discarded'];
  readonly status = signal('');
  readonly page = signal(1);
  readonly data = load(() => this.api.plans({ status: this.status(), page: this.page(), pageSize: 25 }));
  readonly dateTime = dateTime;
  readonly tone = tone;
}
