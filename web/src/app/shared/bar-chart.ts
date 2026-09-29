import { Component, computed, input } from '@angular/core';

export interface Bar { label: string; value: number; display: string; }

/** Horizontal bar chart in plain SVG-free markup: readable at any width and by screen readers. */
@Component({
  selector: 'app-bar-chart',
  template: `
    <ul class="bars" [attr.aria-label]="label()">
      @for (bar of bars(); track bar.label) {
        <li>
          <span class="bar-label">{{ bar.label }}</span>
          <span class="bar-track"><span class="bar-fill" [style.width.%]="percent(bar.value)"></span></span>
          <span class="bar-value">{{ bar.display }}</span>
        </li>
      }
    </ul>`
})
export class BarChart {
  readonly bars = input.required<Bar[]>();
  readonly label = input('Chart');
  private readonly max = computed(() => Math.max(...this.bars().map(b => b.value), 0));
  percent = (value: number) => (this.max() > 0 ? Math.max((value / this.max()) * 100, value > 0 ? 1.5 : 0) : 0);
}
