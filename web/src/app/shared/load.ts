import { effect, inject, signal } from '@angular/core';
import { ApiError } from '../core/api';
import { Session } from '../core/session';

/**
 * Loads data for a page and reloads it whenever a signal read while building the request changes
 * (filters, paging, "viewing as"), or when any form saves (Session.version).
 * Call from a field initializer so it runs in an injection context.
 */
export function load<T>(fetch: () => Promise<T>) {
  const session = inject(Session);
  const value = signal<T | undefined>(undefined);
  const loading = signal(true);
  const error = signal<string | null>(null);
  let latest = 0;

  const run = (request: Promise<T>) => {
    const ticket = ++latest;
    loading.set(true);
    error.set(null);
    request.then(
      result => { if (ticket === latest) { value.set(result); loading.set(false); } },
      failure => { if (ticket === latest) { error.set(ApiError.from(failure).message); loading.set(false); } }
    );
  };

  // Signals read synchronously while building the request become dependencies of this effect.
  effect(() => {
    session.version();
    run(fetch());
  });

  return { value, loading, error, reload: () => run(fetch()) };
}
