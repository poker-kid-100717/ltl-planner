import { HttpClient } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

type Order = { id:string; customer:string; origin:string; destination:string; pallets:number; weight:number; equipment:string; priority:number; status:string; readyDate?:string; planId?:string };
type Truck = { id:string; equipment:string; palletCapacity:number; weightCapacity:number; currentLocation:string; active:boolean };
type PlannedTruck = { truckId:string; equipment:string; orders:Order[]; usedPallets:number; palletCapacity:number; usedWeight:number; weightCapacity:number; utilization:number; explanations:string[] };
type Unassigned = { order:Order; reason:string };
type Plan = { id:string; createdAt:string; status:string; trucks:PlannedTruck[]; unassignedOrders:Unassigned[]; algorithm:string };
type YardEvent = { eventId:string; eventType:string; trailerNumber:string; occurredAt:string; details?:string };

@Component({ selector:'app-root', standalone:true, templateUrl:'./app.component.html' })
export class AppComponent implements OnInit {
  private readonly http=inject(HttpClient);
  readonly orders=signal<Order[]>([]);
  readonly trucks=signal<Truck[]>([]);
  readonly plans=signal<Plan[]>([]);
  readonly plan=signal<Plan|null>(null);
  readonly yardEvents=signal<YardEvent[]>([]);
  readonly selectedOrders=signal(new Set<string>());
  readonly selectedTrucks=signal(new Set<string>());
  readonly busy=signal(false);
  readonly message=signal('');

  async ngOnInit():Promise<void>{ await Promise.all([this.refreshCore(),this.refreshPlans(),this.refreshYardEvents()]); }

  async refreshCore():Promise<void>{
    const [orders,trucks]=await Promise.all([
      firstValueFrom(this.http.get<Order[]>('/api/orders?status=Open')),
      firstValueFrom(this.http.get<Truck[]>('/api/trucks'))
    ]);
    this.orders.set(orders); this.trucks.set(trucks.filter(x=>x.active));
  }
  async refreshPlans():Promise<void>{ this.plans.set(await firstValueFrom(this.http.get<Plan[]>('/api/plans'))); }
  async refreshYardEvents():Promise<void>{ this.yardEvents.set(await firstValueFrom(this.http.get<YardEvent[]>('/api/integrations/v1/yard/events'))); }

  toggleOrder(id:string):void{ const next=new Set(this.selectedOrders()); next.has(id)?next.delete(id):next.add(id); this.selectedOrders.set(next); }
  toggleTruck(id:string):void{ const next=new Set(this.selectedTrucks()); next.has(id)?next.delete(id):next.add(id); this.selectedTrucks.set(next); }
  selected(set:Set<string>,id:string):boolean{return set.has(id);}

  async buildPlan():Promise<void>{
    this.busy.set(true); this.message.set('');
    try{
      const result=await firstValueFrom(this.http.post<Plan>('/api/plans/build',{
        orderIds:[...this.selectedOrders()], truckIds:[...this.selectedTrucks()]
      }));
      this.plan.set(result); this.message.set('Draft plan saved. Review the assignments, then commit or discard it.');
      await this.refreshPlans();
    } finally { this.busy.set(false); }
  }

  async transition(action:'commit'|'discard'):Promise<void>{
    const current=this.plan(); if(!current)return;
    this.busy.set(true);
    try{
      const result=await firstValueFrom(this.http.post<Plan>(`/api/plans/${current.id}/${action}`,{}));
      this.plan.set(result); this.message.set(action==='commit'?'Plan committed and orders moved to Planned.':'Draft discarded.');
      await Promise.all([this.refreshCore(),this.refreshPlans()]);
    } finally { this.busy.set(false); }
  }
}
