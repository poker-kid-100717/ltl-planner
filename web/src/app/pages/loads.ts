import { Component, inject } from '@angular/core';
import { Api } from '../core/api';
import { dateTime, num } from '../core/format';
import { load } from '../shared/load';
import { State } from '../shared/state';

@Component({
  selector: 'app-loads',
  imports: [State],
  template: `
    <header class="page-head">
      <div><p class="eyebrow">Yard</p><h1>Loads</h1>
        <p class="muted">Freight in motion, read from the transportation-management system through a server-side adapter.</p></div>
      <div class="head-actions"><button type="button" class="ghost" (click)="data.reload()" [disabled]="data.loading()">Refresh</button></div>
    </header>

    <app-state [loading]="data.loading()" [error]="data.error()" [hasValue]="!!data.value()" (retry)="data.reload()" />
    @if (data.value(); as r) {
      <section class="card integration-card">
        <div><h2>{{ r.live ? 'Live read from ' + r.provider : 'Demo data' }}</h2>
          <p class="muted">{{ r.live ? 'Read-only. Credentials and tokens stay on the server; the browser only talks to this app.'
            : 'No TMS credentials are configured, so this page shows synthetic loads. With credentials it reads live data, read-only.' }}</p></div>
        <span class="badge" [attr.data-tone]="r.live ? 'good' : 'neutral'">{{ r.live ? 'Live' : 'Demo' }}</span>
      </section>
      @if (r.degraded) { <div class="alert" role="alert">{{ r.degradedReason }}</div> }

      <div class="load-grid">
        @for (l of r.loads; track l.loadNumber) {
          <article class="card load-card">
            <div class="deal-top"><strong>{{ l.loadNumber }}</strong><span class="badge" data-tone="warn">{{ l.status }}</span></div>
            <h2>{{ l.customerName }}</h2>
            <dl class="facts">
              <div><dt>Pickup</dt><dd>{{ dateTime(l.scheduledPickupAt) }}</dd></div>
              <div><dt>Delivery</dt><dd>{{ dateTime(l.scheduledDeliveryAt) }}</dd></div>
              <div><dt>Equipment</dt><dd>{{ l.requiredEquipment.join(', ') || '—' }}</dd></div>
              <div><dt>Weight</dt><dd>{{ l.weight ? num(l.weight) + ' lb' : '—' }}</dd></div>
            </dl>
          </article>
        } @empty { <p class="empty-row">No loads to show.</p> }
      </div>
    }`
})
export class LoadsPage {
  private readonly api = inject(Api);
  readonly data = load(() => this.api.loads());
  readonly dateTime = dateTime;
  readonly num = num;
}
