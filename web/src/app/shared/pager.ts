import { Component, input, output } from '@angular/core';

@Component({
  selector: 'app-pager',
  template: `
    @if (total() > pageSize()) {
      <nav class="pager" aria-label="Pagination">
        <button type="button" class="ghost" [disabled]="page() <= 1" (click)="go.emit(page() - 1)">Previous</button>
        <span>Page {{ page() }} of {{ pages() }} · {{ total() }} total</span>
        <button type="button" class="ghost" [disabled]="page() >= pages()" (click)="go.emit(page() + 1)">Next</button>
      </nav>
    }`
})
export class Pager {
  readonly page = input.required<number>();
  readonly pageSize = input.required<number>();
  readonly total = input.required<number>();
  readonly go = output<number>();
  pages = () => Math.max(1, Math.ceil(this.total() / this.pageSize()));
}
