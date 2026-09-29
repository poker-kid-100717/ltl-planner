import { Component, input, output } from '@angular/core';

/** Loading and error states shared by every page. */
@Component({
  selector: 'app-state',
  template: `
    @if (error()) {
      <div class="alert" role="alert">
        <span>{{ error() }}</span>
        <button type="button" class="ghost" (click)="retry.emit()">Try again</button>
      </div>
    } @else if (loading() && !hasValue()) {
      <div class="loading" role="status"><span class="spinner" aria-hidden="true"></span> Loading…</div>
    }`
})
export class State {
  readonly loading = input(false);
  readonly error = input<string | null>(null);
  readonly hasValue = input(false);
  readonly retry = output<void>();
}
