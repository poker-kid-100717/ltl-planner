// Shapes returned by the LTL Planner API (camelCase JSON).

export interface Paged<T> { items: T[]; total: number; page: number; pageSize: number; }

export interface Order {
  id: string; customer: string; origin: string; destination: string; pallets: number; weight: number; equipment: string;
  priority: number; assigned: boolean; status: string; readyOn: string | null; planId: string | null; truckId: string | null;
}

export interface Truck {
  id: string; equipment: string; palletCapacity: number; weightCapacity: number; currentLocation: string; active: boolean;
}

export interface PlannedTruck {
  truckId: string; equipment: string; orders: Order[]; usedPallets: number; palletCapacity: number;
  usedWeight: number; weightCapacity: number; utilization: number; explanations: string[];
}

export interface Plan {
  id: string; createdAt: string; trucks: PlannedTruck[]; unassignedOrders: Order[]; algorithm: string;
  unassigned: { order: Order; reason: string }[] | null; status: string; decidedAt: string | null;
}

export interface PlanSummary {
  id: string; createdAt: string; status: string; decidedAt: string | null; truckCount: number; plannedOrders: number; unassignedOrders: number;
}

export interface YardEvent { eventId: string; eventType: string; trailerNumber: string; occurredAt: string; details: string | null; schemaVersion: number; }
export interface YardTrailer { trailerNumber: string; status: string; lastEventType: string; lastEventAt: string; }

export interface Dashboard {
  openOrders: number; openPallets: number; openWeight: number; plannedOrders: number; dispatchedOrders: number;
  activeTrucks: number; fleetPallets: number; draftPlans: number; committedPlans: number; trailersOnYard: number; trailersReady: number;
  byEquipment: { equipment: string; openOrders: number; openPallets: number; trucks: number; palletCapacity: number }[];
}

export interface Meta {
  equipmentTypes: string[]; orderStatuses: string[]; planStatuses: string[];
  storage: { mode: string; persistent: boolean; ready: boolean };
  demoReset: { scheduled: boolean; schedule: string };
  yardIntegration: { signingConfigured: boolean };
  integration: { provider: string; mode: string; configured: boolean };
}

export interface ExternalLoad {
  loadNumber: string; customerName: string; status: string; scheduledPickupAt?: string; scheduledDeliveryAt?: string;
  requiredEquipment: string[]; weight?: number; source: string;
}
export interface LoadResult { provider: string; live: boolean; degraded: boolean; degradedReason?: string; loads: ExternalLoad[]; }
