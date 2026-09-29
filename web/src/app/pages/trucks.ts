import { Component, inject } from '@angular/core';
import { Actions } from '../core/actions';
import { Api } from '../core/api';
import { num } from '../core/format';
import { load } from '../shared/load';
import { State } from '../shared/state';

@Component({
  selector: 'app-trucks',
  imports: [State],
  template: `
    <header class="page-head">
      <div><p class="eyebrow">Planning</p><h1>Trucks</h1><p class="muted">The fleet the planner can use. Inactive trucks are left out of new plans.</p></div>
      <div class="head-actions"><button type="button" (click)="actions.newTruck()">New truck</button></div>
    </header>
    <app-state [loading]="data.loading()" [error]="data.error()" [hasValue]="!!data.value()" (retry)="data.reload()" />
    @if (data.value(); as trucks) {
      <div class="table-wrap">
        <table>
          <thead><tr><th scope="col">Truck</th><th scope="col">Equipment</th><th scope="col" class="num">Pallets</th><th scope="col" class="num">Weight (lb)</th>
            <th scope="col">Location</th><th scope="col">Status</th><th scope="col"><span class="sr-only">Actions</span></th></tr></thead>
          <tbody>
            @for (t of trucks; track t.id) {
              <tr>
                <td><strong>{{ t.id }}</strong></td><td>{{ t.equipment }}</td><td class="num">{{ t.palletCapacity }}</td>
                <td class="num">{{ num(t.weightCapacity) }}</td><td>{{ t.currentLocation }}</td>
                <td><span class="badge" [attr.data-tone]="t.active ? 'good' : 'neutral'">{{ t.active ? 'Active' : 'Inactive' }}</span></td>
                <td class="row-actions"><button type="button" class="ghost small" (click)="actions.editTruck(t)">Edit</button></td>
              </tr>
            } @empty { <tr><td colspan="7" class="empty-row">No trucks yet.</td></tr> }
          </tbody>
        </table>
      </div>
    }`
})
export class TrucksPage {
  private readonly api = inject(Api);
  readonly actions = inject(Actions);
  readonly data = load(() => this.api.trucks());
  readonly num = num;
}
