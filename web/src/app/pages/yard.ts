import { Component, inject } from '@angular/core';
import { Api } from '../core/api';
import { dateTime, tone } from '../core/format';
import { load } from '../shared/load';
import { State } from '../shared/state';

@Component({
  selector: 'app-yard',
  imports: [State],
  template: `
    <header class="page-head">
      <div><p class="eyebrow">Yard</p><h1>Yard Feed</h1>
        <p class="muted">Yard Ops sends signed events here when trailers gate in, gate out or pass inspection. Each event is stored once, and the latest status per trailer is shown below.</p></div>
      <div class="head-actions"><button type="button" class="ghost" (click)="data.reload()" [disabled]="data.loading()">Refresh</button></div>
    </header>
    <app-state [loading]="data.loading()" [error]="data.error()" [hasValue]="!!data.value()" (retry)="data.reload()" />
    @if (data.value(); as d) {
      <div class="two-col">
        <section class="card">
          <h2>Trailers</h2>
          <div class="table-wrap flat">
            <table>
              <thead><tr><th scope="col">Trailer</th><th scope="col">Status</th><th scope="col">Last update</th></tr></thead>
              <tbody>
                @for (t of d.trailers; track t.trailerNumber) {
                  <tr><td><strong>{{ t.trailerNumber }}</strong></td><td><span class="badge" [attr.data-tone]="tone(t.status)">{{ t.status }}</span></td>
                    <td>{{ dateTime(t.lastEventAt) }}</td></tr>
                } @empty { <tr><td colspan="3" class="empty-row">No trailers reported yet. Gate a trailer in on Yard Ops and it appears here.</td></tr> }
              </tbody>
            </table>
          </div>
        </section>
        <section class="card">
          <h2>Latest events</h2>
          <ul class="timeline">
            @for (e of d.events; track e.eventId) {
              <li><span class="badge" data-tone="neutral">{{ e.trailerNumber }}</span>
                <div><p><strong>{{ e.eventType }}</strong>@if (e.details) { — {{ e.details }} }</p><p class="muted small">{{ dateTime(e.occurredAt) }} · schema v{{ e.schemaVersion }}</p></div></li>
            } @empty { <li class="empty-row">No events received yet.</li> }
          </ul>
        </section>
      </div>
    }`
})
export class YardPage {
  private readonly api = inject(Api);
  readonly data = load(async () => {
    const [trailers, events] = await Promise.all([this.api.yardTrailers(), this.api.yardEvents()]);
    return { trailers, events };
  });
  readonly dateTime = dateTime;
  readonly tone = tone;
}
