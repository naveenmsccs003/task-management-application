import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { PermissionScope } from './auth.models';
import { AuthService } from './auth.service';

/** Only for signed-in members; others go to sign-in and come back afterwards. */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  return auth.isSignedIn() ? true : inject(Router).createUrlTree(['/sign-in'], { queryParams: { returnUrl: state.url } });
};

/** Sign-in and password pages: signed-in members go to the home page. */
export const guestGuard: CanActivateFn = () =>
  inject(AuthService).isSignedIn() ? inject(Router).createUrlTree(['/']) : true;

/** Hides pages the member cannot use. The API enforces the same permission. */
export function permissionGuard(code: string, minimumScope: PermissionScope = 'Global'): CanActivateFn {
  return (_route, state) => {
    const auth = inject(AuthService);
    const router = inject(Router);
    if (!auth.isSignedIn()) {
      return router.createUrlTree(['/sign-in'], { queryParams: { returnUrl: state.url } });
    }
    return auth.hasPermission(code, minimumScope) ? true : router.createUrlTree(['/forbidden']);
  };
}
