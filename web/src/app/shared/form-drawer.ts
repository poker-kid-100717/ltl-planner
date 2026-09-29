import { Component, ElementRef, effect, inject, signal, viewChild } from '@angular/core';
import { ApiError } from '../core/api';
import { Field, FormRequest, Forms } from '../core/form';
import { Icon } from './icon';

type Values = Record<string, unknown>;

/** Renders whichever form Forms.current holds, in an accessible side drawer. */
@Component({
  selector: 'app-form-drawer',
  imports: [Icon],
  template: `
    @if (forms.loading()) {
      <div class="drawer-backdrop"><div class="drawer-loading" role="status">Loading form…</div></div>
    }
    @if (forms.current(); as form) {
      <div class="drawer-backdrop" (click)="close()"></div>
      <aside class="drawer" role="dialog" aria-modal="true" [attr.aria-labelledby]="'drawer-title'" (keydown.escape)="close()" #panel>
        <header class="drawer-head">
          <div>
            <h2 id="drawer-title">{{ form.title }}</h2>
            @if (form.description) { <p class="muted">{{ form.description }}</p> }
          </div>
          <button type="button" class="icon-button" (click)="close()" aria-label="Close"><app-icon name="close" /></button>
        </header>

        <form class="drawer-form" (submit)="submit($event, form)" novalidate>
          @if (error()) { <div class="alert" role="alert">{{ error() }}</div> }
          <div class="field-grid">
            @for (field of visible(form); track field.key) {
              <div class="field" [class.wide]="field.wide || field.type === 'textarea' || field.type === 'checks'">
                @switch (field.type) {
                  @case ('checkbox') {
                    <label class="check">
                      <input type="checkbox" [id]="'f-' + field.key" [checked]="values()[field.key] === true"
                             (change)="set(field, $any($event.target).checked)" />
                      {{ field.label }}
                    </label>
                  }
                  @case ('checks') {
                    <fieldset>
                      <legend>{{ field.label }}@if (field.required) {<span class="req" aria-hidden="true"> *</span>}</legend>
                      <div class="check-row">
                        @for (option of field.options ?? []; track option.value) {
                          <label class="check">
                            <input type="checkbox" [checked]="has(field.key, option.value)" (change)="toggle(field, option.value)" />
                            {{ option.label }}
                          </label>
                        }
                      </div>
                    </fieldset>
                  }
                  @default {
                    <label [for]="'f-' + field.key">{{ field.label }}@if (field.required) {<span class="req" aria-hidden="true"> *</span>}</label>
                    @switch (field.type) {
                      @case ('select') {
                        <select [id]="'f-' + field.key" [value]="str(field.key)" (change)="set(field, $any($event.target).value)"
                                [attr.aria-invalid]="!!errors()[field.key]" [attr.aria-describedby]="describedBy(field)">
                          @for (option of field.options ?? []; track option.value) {
                            <option [value]="option.value" [selected]="option.value === str(field.key)">{{ option.label }}</option>
                          }
                        </select>
                      }
                      @case ('textarea') {
                        <textarea [id]="'f-' + field.key" rows="4" [value]="str(field.key)" [placeholder]="field.placeholder ?? ''"
                                  (input)="set(field, $any($event.target).value)" [attr.aria-invalid]="!!errors()[field.key]"
                                  [attr.aria-describedby]="describedBy(field)"></textarea>
                      }
                      @default {
                        <div [class.money]="field.type === 'money'">
                          <input [id]="'f-' + field.key" [type]="inputType(field)" [value]="str(field.key)" [placeholder]="field.placeholder ?? ''"
                                 [attr.min]="field.min ?? null" [attr.max]="field.max ?? null" [attr.step]="field.type === 'money' ? '0.01' : null"
                                 [attr.inputmode]="field.type === 'money' || field.type === 'number' ? 'decimal' : null"
                                 (input)="set(field, $any($event.target).value)" [attr.aria-invalid]="!!errors()[field.key]"
                                 [attr.aria-describedby]="describedBy(field)" />
                        </div>
                      }
                    }
                  }
                }
                @if (field.hint) { <small class="hint" [id]="'h-' + field.key">{{ field.hint }}</small> }
                @if (errors()[field.key]; as messages) { <small class="field-error" [id]="'e-' + field.key">{{ messages.join(' ') }}</small> }
              </div>
            }
          </div>

          <footer class="drawer-actions">
            <button type="button" class="ghost" (click)="close()">Cancel</button>
            <button type="submit" [disabled]="saving()">{{ saving() ? 'Saving…' : (form.submitLabel ?? 'Save') }}</button>
          </footer>
        </form>
      </aside>
    }`
})
export class FormDrawer {
  readonly forms = inject(Forms);
  readonly values = signal<Values>({});
  readonly errors = signal<Record<string, string[]>>({});
  readonly error = signal<string | null>(null);
  readonly saving = signal(false);
  private readonly panel = viewChild<ElementRef<HTMLElement>>('panel');
  private returnFocus: HTMLElement | null = null;

  constructor() {
    // Reset state and move focus into the drawer whenever a new form opens.
    effect(() => {
      const form = this.forms.current();
      this.values.set({ ...(form?.initial ?? {}) });
      this.errors.set({});
      this.error.set(null);
      this.saving.set(false);
      if (form) {
        this.returnFocus = document.activeElement as HTMLElement | null;
        setTimeout(() => this.panel()?.nativeElement.querySelector<HTMLElement>('input, select, textarea')?.focus());
      }
    });
  }

  visible = (form: FormRequest) => form.fields.filter(f => !f.when || f.when(this.values()));
  str = (key: string) => { const v = this.values()[key]; return v === null || v === undefined ? '' : String(v); };
  has = (key: string, value: string) => ((this.values()[key] as string[] | undefined) ?? []).includes(value);
  inputType = (f: Field) => ({ money: 'number', datetime: 'datetime-local' } as Record<string, string>)[f.type] ?? f.type;
  describedBy = (f: Field) => [f.hint ? `h-${f.key}` : '', this.errors()[f.key] ? `e-${f.key}` : ''].filter(Boolean).join(' ') || null;

  async set(field: Field, value: unknown): Promise<void> {
    this.values.update(v => ({ ...v, [field.key]: value }));
    this.errors.update(e => { const { [field.key]: _, ...rest } = e; return rest; });
    const patch = await field.onChange?.(value, this.values());
    if (patch) this.values.update(v => ({ ...v, ...patch }));
  }

  toggle(field: Field, option: string): void {
    const current = (this.values()[field.key] as string[] | undefined) ?? [];
    void this.set(field, current.includes(option) ? current.filter(x => x !== option) : [...current, option]);
  }

  async submit(event: Event, form: FormRequest): Promise<void> {
    event.preventDefault();
    const missing: Record<string, string[]> = {};
    for (const field of this.visible(form)) {
      const value = this.values()[field.key];
      const empty = value === null || value === undefined || value === '' || (Array.isArray(value) && value.length === 0);
      if (field.required && empty) missing[field.key] = ['Required.'];
    }
    if (Object.keys(missing).length) {
      this.errors.set(missing);
      this.error.set('Fill in the highlighted fields.');
      return;
    }

    this.saving.set(true);
    this.error.set(null);
    try {
      const result = await form.submit(this.values());
      this.forms.close();
      this.returnFocus?.focus();
      form.saved?.(result);
    } catch (e) {
      const failure = ApiError.from(e);
      this.errors.set(failure.fieldErrors);
      this.error.set(Object.keys(failure.fieldErrors).length ? 'Check the highlighted fields.' : failure.message);
    } finally {
      this.saving.set(false);
    }
  }

  close(): void {
    if (this.saving()) return;
    this.forms.close();
    this.returnFocus?.focus();
  }
}
