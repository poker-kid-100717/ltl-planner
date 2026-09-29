import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
  { path: 'dashboard', title: 'Dashboard', loadComponent: () => import('./pages/dashboard').then(m => m.DashboardPage) },
  { path: 'orders', title: 'Orders', loadComponent: () => import('./pages/orders').then(m => m.OrdersPage) },
  { path: 'trucks', title: 'Trucks', loadComponent: () => import('./pages/trucks').then(m => m.TrucksPage) },
  { path: 'plans/new', title: 'Plan Builder', loadComponent: () => import('./pages/plan-builder').then(m => m.PlanBuilderPage) },
  { path: 'plans', title: 'Plans', loadComponent: () => import('./pages/plans').then(m => m.PlansPage) },
  { path: 'plans/:id', title: 'Plan', loadComponent: () => import('./pages/plan-detail').then(m => m.PlanDetailPage) },
  { path: 'yard', title: 'Yard Feed', loadComponent: () => import('./pages/yard').then(m => m.YardPage) },
  { path: 'loads', title: 'Loads', loadComponent: () => import('./pages/loads').then(m => m.LoadsPage) },
  { path: 'settings', title: 'Settings', loadComponent: () => import('./pages/settings').then(m => m.SettingsPage) },
  { path: '**', title: 'Not found', loadComponent: () => import('./pages/not-found').then(m => m.NotFoundPage) }
];
