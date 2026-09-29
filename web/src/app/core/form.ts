import { Injectable, signal } from '@angular/core';

export type FieldType = 'text' | 'email' | 'tel' | 'number' | 'money' | 'date' | 'datetime' | 'textarea' | 'select' | 'checks' | 'checkbox';

export interface Option { value: string; label: string; }

export interface Field {
  key: string;
  label: string;
  type: FieldType;
  required?: boolean;
  options?: Option[];
  min?: number;
  max?: number;
  hint?: string;
  placeholder?: string;
  /** Hide and skip unless this returns true for the current values. */
  when?: (values: Record<string, unknown>) => boolean;
  /** Called when this field changes; returns values to patch into other fields. */
  onChange?: (value: unknown, values: Record<string, unknown>) =>
    Record<string, unknown> | void | Promise<Record<string, unknown> | void>;
  wide?: boolean;
}

export interface FormRequest {
  title: string;
  description?: string;
  fields: Field[];
  initial?: Record<string, unknown>;
  submitLabel?: string;
  submit: (values: Record<string, unknown>) => Promise<unknown>;
  saved?: (result: unknown) => void;
}

/** Holds the form currently shown in the side drawer. */
@Injectable({ providedIn: 'root' })
export class Forms {
  readonly current = signal<FormRequest | null>(null);
  readonly loading = signal(false);

  open(request: FormRequest): void {
    this.current.set(request);
  }

  /** For forms that need data (for example the customer list) before they can open. */
  async openAsync(build: () => Promise<FormRequest>): Promise<void> {
    this.loading.set(true);
    try { this.current.set(await build()); } finally { this.loading.set(false); }
  }

  close(): void {
    this.current.set(null);
  }
}

export const options = (values: readonly string[]): Option[] => values.map(v => ({ value: v, label: v }));
