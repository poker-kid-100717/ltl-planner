import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { Api } from './api';
import { Field, Forms, options } from './form';
import { Meta, Order, Truck } from './models';
import { Session } from './session';
import { todayIso } from './format';

type Values = Record<string, unknown>;
const text = (v: unknown) => (typeof v === 'string' && v.trim() !== '' ? v.trim() : null);
const number = (v: unknown) => (v === null || v === '' || v === undefined ? 0 : Number(v));

/** Create/edit flows as drawer forms, shared by pages and the "+ New" menu. */
@Injectable({ providedIn: 'root' })
export class Actions {
  private readonly api = inject(Api);
  private readonly forms = inject(Forms);
  private readonly session = inject(Session);
  private readonly router = inject(Router);

  private get meta(): Meta {
    const meta = this.session.meta();
    if (!meta) throw new Error('Settings are still loading.');
    return meta;
  }

  private done = (navigateTo?: string) => () => {
    this.session.changed();
    if (navigateTo) void this.router.navigateByUrl(navigateTo);
  };

  // ---------- orders ----------

  private orderFields(): Field[] {
    return [
      { key: 'customer', label: 'Customer', type: 'text', required: true, wide: true },
      { key: 'origin', label: 'Origin', type: 'text', required: true, placeholder: 'City, ST' },
      { key: 'destination', label: 'Destination', type: 'text', required: true, placeholder: 'City, ST' },
      { key: 'equipment', label: 'Equipment', type: 'select', required: true, options: options(this.meta.equipmentTypes) },
      { key: 'readyOn', label: 'Ready to ship', type: 'date', required: true },
      { key: 'pallets', label: 'Pallets', type: 'number', required: true, min: 1, max: 30 },
      { key: 'weight', label: 'Weight (lb)', type: 'number', required: true, min: 1, max: 48000 },
      { key: 'priority', label: 'Priority (1–100)', type: 'number', required: true, min: 1, max: 100, hint: 'Higher priority orders are placed first.' }
    ];
  }

  private orderBody = (v: Values) => ({
    customer: text(v['customer']), origin: text(v['origin']), destination: text(v['destination']), equipment: v['equipment'],
    readyOn: v['readyOn'], pallets: number(v['pallets']), weight: number(v['weight']), priority: number(v['priority'])
  });

  newOrder(): void {
    this.forms.open({
      title: 'New order',
      fields: this.orderFields(),
      initial: { equipment: 'Dry Van', readyOn: todayIso(), pallets: 6, weight: 6000, priority: 50 },
      submitLabel: 'Create order',
      submit: v => this.api.post('/api/orders', this.orderBody(v)),
      saved: this.done('/orders')
    });
  }

  editOrder(o: Order): void {
    this.forms.open({
      title: `Edit ${o.id}`,
      fields: this.orderFields(),
      initial: { ...o },
      submitLabel: 'Save order',
      submit: v => this.api.put(`/api/orders/${o.id}`, this.orderBody(v)),
      saved: this.done()
    });
  }

  async moveOrder(o: Order, action: 'cancel' | 'dispatch'): Promise<void> {
    await this.api.post(`/api/orders/${o.id}/${action}`);
    this.session.changed();
  }

  // ---------- trucks ----------

  private truckFields(): Field[] {
    return [
      { key: 'equipment', label: 'Equipment', type: 'select', required: true, options: options(this.meta.equipmentTypes) },
      { key: 'currentLocation', label: 'Current location', type: 'text', required: true, placeholder: 'City, ST' },
      { key: 'palletCapacity', label: 'Pallet capacity', type: 'number', required: true, min: 1, max: 30 },
      { key: 'weightCapacity', label: 'Weight capacity (lb)', type: 'number', required: true, min: 1000, max: 48000 },
      { key: 'active', label: 'Available for planning', type: 'checkbox', wide: true }
    ];
  }

  private truckBody = (v: Values) => ({
    equipment: v['equipment'], currentLocation: text(v['currentLocation']), palletCapacity: number(v['palletCapacity']),
    weightCapacity: number(v['weightCapacity']), active: v['active'] === true
  });

  newTruck(): void {
    this.forms.open({
      title: 'New truck',
      description: 'An id is assigned from the equipment type.',
      fields: this.truckFields(),
      initial: { equipment: 'Dry Van', palletCapacity: 26, weightCapacity: 44000, active: true },
      submitLabel: 'Add truck',
      submit: v => this.api.post('/api/trucks', this.truckBody(v)),
      saved: this.done('/trucks')
    });
  }

  editTruck(t: Truck): void {
    this.forms.open({
      title: `Edit ${t.id}`,
      fields: this.truckFields(),
      initial: { ...t },
      submitLabel: 'Save truck',
      submit: v => this.api.put(`/api/trucks/${t.id}`, this.truckBody(v)),
      saved: this.done()
    });
  }
}
