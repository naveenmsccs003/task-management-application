import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    title: 'Task Management',
    loadComponent: () => import('./features/home/home').then((m) => m.Home),
  },
  {
    path: '**',
    title: 'Page not found · Task Management',
    loadComponent: () => import('./features/not-found/not-found').then((m) => m.NotFound),
  },
];
