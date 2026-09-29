import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Dashboard, LoadResult, Meta, Order, Paged, Plan, PlanSummary, Truck, YardEvent, YardTrailer } from './models';

type Query = Record<string, string | number | boolean | null | undefined>;

/** An API failure with the server's message and any per-field validation errors. */
export class ApiError extends Error {
  constructor(message: string, readonly status: number, readonly fieldErrors: Record<string, string[]> = {}) {
    super(message);
  }

  static from(error: unknown): ApiError {
    if (error instanceof ApiError) return error;
    if (error instanceof HttpErrorResponse) {
      const body = error.error as { title?: string; detail?: string; errors?: Record<string, string[]> } | null;
      if (error.status === 0) return new ApiError('Could not reach the server. Check your connection and try again.', 0);
      if (error.status === 429) return new ApiError('Too many changes in a short time. Wait a minute and try again.', 429);
      const message = body?.detail ?? body?.title ?? `Request failed (${error.status}).`;
      return new ApiError(message, error.status, body?.errors ?? {});
    }
    return new ApiError('Something went wrong.', -1);
  }
}

@Injectable({ providedIn: 'root' })
export class Api {
  private readonly http = inject(HttpClient);

  private params(query: Query = {}): HttpParams {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(query)) {
      if (value !== null && value !== undefined && value !== '') params = params.set(key, String(value));
    }
    return params;
  }

  private async run<T>(request: Promise<T>): Promise<T> {
    try { return await request; } catch (error) { throw ApiError.from(error); }
  }

  get<T>(url: string, query?: Query): Promise<T> {
    return this.run(firstValueFrom(this.http.get<T>(url, { params: this.params(query) })));
  }
  post<T = unknown>(url: string, body: unknown = {}): Promise<T> {
    return this.run(firstValueFrom(this.http.post<T>(url, body)));
  }
  put(url: string, body: unknown): Promise<unknown> {
    return this.run(firstValueFrom(this.http.put(url, body)));
  }
  delete(url: string): Promise<unknown> {
    return this.run(firstValueFrom(this.http.delete(url)));
  }

  meta = () => this.get<Meta>('/api/meta');
  dashboard = () => this.get<Dashboard>('/api/dashboard');
  orders = (q: Query) => this.get<Paged<Order>>('/api/orders', q);
  order = (id: string) => this.get<Order>(`/api/orders/${id}`);
  trucks = (active?: boolean) => this.get<Truck[]>('/api/trucks', { active });
  plans = (q: Query) => this.get<Paged<PlanSummary>>('/api/plans', q);
  plan = (id: string) => this.get<Plan>(`/api/plans/${id}`);
  buildPlan = (orderIds: string[], truckIds: string[]) => this.post<Plan>('/api/plans/build', { orderIds, truckIds });
  commitPlan = (id: string) => this.post<Plan>(`/api/plans/${id}/commit`);
  discardPlan = (id: string) => this.post(`/api/plans/${id}/discard`);
  yardTrailers = () => this.get<YardTrailer[]>('/api/yard/trailers');
  yardEvents = () => this.get<YardEvent[]>('/api/integrations/v1/yard/events');
  loads = () => this.get<LoadResult>('/api/alvys/loads');
}
