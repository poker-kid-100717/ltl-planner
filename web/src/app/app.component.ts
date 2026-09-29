import { Component, HostListener, OnInit, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { Actions } from './core/actions';
import { Session } from './core/session';
import { FormDrawer } from './shared/form-drawer';
import { Icon } from './shared/icon';

interface NavItem { label: string; path: string; icon: string; exact?: boolean; }
interface NavGroup { label: string; items: NavItem[]; }

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Icon, FormDrawer],
  templateUrl: './app.component.html'
})
export class AppComponent implements OnInit {
  readonly session = inject(Session);
  readonly actions = inject(Actions);
  private readonly router = inject(Router);

  readonly groups: NavGroup[] = [
    { label: 'Overview', items: [{ label: 'Dashboard', path: '/dashboard', icon: 'dashboard' }] },
    { label: 'Planning', items: [
      { label: 'Orders', path: '/orders', icon: 'order' },
      { label: 'Trucks', path: '/trucks', icon: 'truck' },
      { label: 'Plan Builder', path: '/plans/new', icon: 'plan' },
      { label: 'Plans', path: '/plans', icon: 'history', exact: true }
    ] },
    { label: 'Yard', items: [
      { label: 'Yard Feed', path: '/yard', icon: 'yard' },
      { label: 'Loads', path: '/loads', icon: 'load' }
    ] }
  ];

  readonly collapsed = signal(readFlag('ltl-planner.nav-collapsed'));
  readonly mobileOpen = signal(false);
  readonly newOpen = signal(false);

  ngOnInit(): void {
    void this.session.loadMeta();
    this.router.events.pipe(filter(e => e instanceof NavigationEnd)).subscribe(() => {
      this.mobileOpen.set(false);
      this.newOpen.set(false);
      document.getElementById('main')?.focus({ preventScroll: true });
    });
  }

  toggleCollapsed(): void {
    this.collapsed.update(v => !v);
    try { localStorage.setItem('ltl-planner.nav-collapsed', String(this.collapsed())); } catch { /* ignore */ }
  }

  create(kind: 'order' | 'truck'): void {
    this.newOpen.set(false);
    if (!this.session.meta()) return;
    if (kind === 'order') this.actions.newOrder(); else this.actions.newTruck();
  }

  @HostListener('document:keydown.escape')
  escape(): void { this.newOpen.set(false); this.mobileOpen.set(false); }

  @HostListener('document:click', ['$event'])
  outside(event: MouseEvent): void {
    if (!(event.target as HTMLElement).closest('.new-menu')) this.newOpen.set(false);
  }
}

function readFlag(key: string): boolean {
  try { return localStorage.getItem(key) === 'true'; } catch { return false; }
}
