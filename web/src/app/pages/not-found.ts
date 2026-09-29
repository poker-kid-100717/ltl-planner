import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-not-found',
  imports: [RouterLink],
  template: `
    <section class="card not-found">
      <h1>Page not found</h1>
      <p class="muted">That page doesn't exist. It may have moved, or the link may be wrong.</p>
      <a class="button" routerLink="/dashboard">Go to the dashboard</a>
    </section>`
})
export class NotFoundPage {}
