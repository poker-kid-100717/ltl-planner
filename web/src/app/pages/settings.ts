import { Component, inject } from '@angular/core';
import { Session } from '../core/session';

@Component({
  selector: 'app-settings',
  template: `
    <header class="page-head">
      <div><p class="eyebrow">Admin</p><h1>Settings</h1><p class="muted">How this demo is configured.</p></div>
    </header>
    @if (session.meta(); as meta) {
      <div class="settings-grid">
        <section class="card">
          <h2>Data storage</h2>
          <p><span class="badge" [attr.data-tone]="meta.storage.persistent ? 'good' : 'warn'">{{ meta.storage.mode }}</span></p>
          <p class="muted">{{ meta.storage.persistent ? 'Changes are saved to PostgreSQL.'
            : 'No database is configured, so the app uses a temporary demo database that resets when the server restarts.' }}</p>
          <p class="muted">Demo reset: {{ meta.demoReset.scheduled ? meta.demoReset.schedule : 'not scheduled' }}. A reset restores orders and trucks; Yard events are kept.</p>
        </section>
        <section class="card">
          <h2>Yard Ops integration</h2>
          <p><span class="badge" [attr.data-tone]="meta.yardIntegration.signingConfigured ? 'good' : 'bad'">{{ meta.yardIntegration.signingConfigured ? 'Signing key set' : 'No signing key' }}</span></p>
          <p class="muted">Contract v1 under /api/integrations/v1/yard: candidate lookups and HMAC-SHA256 signed events, stored idempotently by event id.</p>
        </section>
        <section class="card">
          <h2>Planning rules</h2>
          <p class="muted">Equipment, pallet and weight limits are hard constraints. Among trucks that fit, the planner prefers higher-priority orders, trucks already in the order's origin market, and the tightest remaining capacity. Committing a draft fails if any order or truck changed after it was built.</p>
        </section>
        <section class="card">
          <h2>TMS integration</h2>
          <p><span class="badge" [attr.data-tone]="meta.integration.configured ? 'good' : 'neutral'">{{ meta.integration.mode }}</span> {{ meta.integration.provider }}</p>
          <p class="muted">Read-only. Credentials stay on the server.</p>
        </section>
        <section class="card wide">
          <h2>About this app</h2>
          <p class="muted">A clean-room portfolio LTL planner built with .NET 10, EF Core, PostgreSQL and Angular 22, hosted on Cloudflare Workers and Containers.
            All data is fictional. It is not modelled on any employer's product and contains no employer code, data or screens.</p>
        </section>
      </div>
    } @else if (session.metaError()) {
      <div class="alert" role="alert">{{ session.metaError() }}</div>
    } @else { <div class="loading" role="status">Loading…</div> }`
})
export class SettingsPage {
  readonly session = inject(Session);
}
