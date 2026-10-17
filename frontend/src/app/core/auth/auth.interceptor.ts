import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

/** When the server says the session is gone (401), clear local state and go to sign-in. */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return next(request).pipe(
    catchError((error: unknown) => {
      const isSessionCheck = request.url.startsWith('/api/v1/auth/') || request.url === '/api/v1/me';
      if (error instanceof HttpErrorResponse && error.status === 401 && !isSessionCheck && auth.isSignedIn()) {
        auth.markSignedOut();
        void router.navigate(['/sign-in'], { queryParams: { returnUrl: router.url, expired: 1 } });
      }
      return throwError(() => error);
    }),
  );
};
