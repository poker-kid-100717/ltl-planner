import { Component, input } from '@angular/core';
import { num } from '../core/format';
import { Plan } from '../core/models';

/** One planner result: each truck with its orders, utilization and explanation, then the unplaced orders and why. */
@Component({
  selector: 'app-plan-view',
  template: `
    <div class="plan-grid">
      @for (t of plan().trucks; track t.truckId) {
        <article class="card">
          <div class="deal-top"><h3>{{ t.truckId }}</h3><span class="badge" data-tone="neutral">{{ t.equipment }}</span></div>
          <div class="util" [attr.aria-label]="t.utilization + '% utilized'">
            <span class="small muted">{{ t.usedPallets }}/{{ t.palletCapacity }} pallets · {{ num(t.usedWeight) }}/{{ num(t.weightCapacity) }} lb · {{ t.utilization }}%</span>
            <span class="util-track"><span class="util-fill" [class.full]="t.utilization >= 85" [style.width.%]="t.utilization"></span></span>
          </div>
          <ul class="compact-list">
            @for (o of t.orders; track o.id) {
              <li><strong>{{ o.id }}</strong><div>{{ o.customer }}<div class="muted small">{{ o.origin }} → {{ o.destination }} · {{ o.pallets }} pallets · {{ num(o.weight) }} lb · priority {{ o.priority }}</div></div></li>
            }
          </ul>
          <ul class="explain">@for (e of t.explanations; track $index) { <li>{{ e }}</li> }</ul>
        </article>
      } @empty {
        <p class="empty-row">No orders could be placed on the selected trucks.</p>
      }
    </div>

    @if ((plan().unassigned ?? []).length) {
      <section class="card">
        <h2>Not placed ({{ plan().unassigned!.length }})</h2>
        <ul class="task-list">
          @for (u of plan().unassigned!; track u.order.id) {
            <li><div><p><strong>{{ u.order.id }}</strong> · {{ u.order.customer }} · {{ u.order.equipment }} · {{ u.order.pallets }} pallets</p>
              <p class="muted small">{{ u.reason }}</p></div></li>
          }
        </ul>
      </section>
    }
    <p class="muted small">{{ plan().algorithm }}</p>`
})
export class PlanView {
  readonly plan = input.required<Plan>();
  readonly num = num;
}
