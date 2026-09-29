import { Component, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api, ApiError } from '../core/api';
import { dateTime, tone } from '../core/format';
import { Session } from '../core/session';
import { load } from '../shared/load';
import { PlanView } from '../shared/plan-view';
import { State } from '../shared/state';

@Component({
  selector: 'app-plan-detail',
  imports: [RouterLink, State, PlanView],
  template: `
    <nav class="breadcrumbs" aria-label="Breadcrumb"><a routerLink="/plans">Plans</a><span aria-hidden="true">/</span><span aria-current="page">Plan</span></nav>
    <app-state [loading]="data.loading()" [error]="data.error()" [hasValue]="!!data.value()" (retry)="data.reload()" />
    @if (error()) { <div class="alert" role="alert">{{ error() }}</div> }
    @if (data.value(); as plan) {
      <section class="card">
        <div class="card-head">
          <div><h1>Plan built {{ dateTime(plan.createdAt) }}</h1>
            <p class="muted">@if (plan.decidedAt) { {{ plan.status }} {{ dateTime(plan.decidedAt) }} · } {{ plan.trucks.length }} trucks · {{ plan.unassignedOrders.length }} orders not placed</p></div>
          <span class="badge large" [attr.data-tone]="tone(plan.status)">{{ plan.status }}</span>
        </div>
        @if (plan.status === 'Draft') {
          <div class="button-row">
            <button type="button" (click)="act('commit')" [disabled]="busy()">Commit plan</button>
            <button type="button" class="ghost" (click)="act('discard')" [disabled]="busy()">Discard</button>
          </div>
        }
      </section>
      <app-plan-view [plan]="plan" />
    }`
})
export class PlanDetailPage {
  private readonly api = inject(Api);
  private readonly session = inject(Session);
  readonly id = input.required<string>();
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly data = load(() => this.api.plan(this.id()));
  readonly dateTime = dateTime;
  readonly tone = tone;

  async act(action: 'commit' | 'discard'): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      if (action === 'commit') await this.api.commitPlan(this.id()); else await this.api.discardPlan(this.id());
      this.session.changed();
    } catch (e) { this.error.set(ApiError.from(e).message); } finally { this.busy.set(false); }
  }
}
