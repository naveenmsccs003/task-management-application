import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, provideRouter, Router, RouterStateSnapshot, UrlTree } from '@angular/router';
import { authGuard, guestGuard, permissionGuard } from './auth.guards';
import { Me } from './auth.models';
import { AuthService } from './auth.service';

const member: Me = {
  id: '1',
  email: 'a@example.com',
  fullName: 'A',
  timeZone: 'UTC',
  globalRole: { id: 'r', name: 'Developer' },
  permissions: [{ code: 'profile.manage', scope: 'Own' }],
};

describe('auth guards', () => {
  const route = {} as ActivatedRouteSnapshot;
  const state = { url: '/admin/members?page=2' } as RouterStateSnapshot;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
  });

  const run = (guard: typeof authGuard) => TestBed.runInInjectionContext(() => guard(route, state));
  const serialize = (result: unknown) => TestBed.inject(Router).serializeUrl(result as UrlTree);

  it('sends signed-out visitors to sign-in with a return URL', () => {
    TestBed.inject(AuthService).markSignedOut();

    expect(serialize(run(authGuard))).toBe('/sign-in?returnUrl=%2Fadmin%2Fmembers%3Fpage%3D2');
  });

  it('lets signed-in members through and keeps them off the sign-in page', () => {
    TestBed.inject(AuthService).setMe(member);

    expect(run(authGuard)).toBe(true);
    expect(serialize(run(guestGuard))).toBe('/');
  });

  it('sends members without the permission to the no-access page', () => {
    TestBed.inject(AuthService).setMe(member);

    expect(serialize(run(permissionGuard('members.view')))).toBe('/forbidden');
    expect(run(permissionGuard('profile.manage', 'Own'))).toBe(true);
  });
});
