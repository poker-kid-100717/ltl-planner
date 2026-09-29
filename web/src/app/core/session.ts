import { Injectable, inject, signal } from '@angular/core';
import { Api } from './api';
import { Meta } from './models';

/** App-wide state: reference data and a counter pages watch to reload after saves. */
@Injectable({ providedIn: 'root' })
export class Session {
  private readonly api = inject(Api);

  readonly meta = signal<Meta | null>(null);
  readonly metaError = signal<string | null>(null);
  readonly version = signal(0);

  async loadMeta(): Promise<void> {
    try {
      this.meta.set(await this.api.meta());
      this.metaError.set(null);
    } catch {
      this.metaError.set('Could not load settings from the server.');
    }
  }

  /** Signal every page that data changed. */
  changed(): void {
    this.version.update(v => v + 1);
  }
}
