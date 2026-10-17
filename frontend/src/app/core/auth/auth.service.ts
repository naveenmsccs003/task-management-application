import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { asApiError } from '../api/api-error';
import { Me, PermissionScope, scopeAtLeast } from './auth.models';

/**
 * Session state for the UI. The server is the security boundary (FR-ROLE-05): permissions here only decide
 * which links and buttons are shown.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  /** undefined = not checked yet; null = signed out. */
  private readonly state = signal<Me | null | undefined>(undefined);

  readonly me = this.state.asReadonly();
  readonly isSignedIn = computed(() => !!this.state());

  /** Called once at start-up: gets an antiforgery token, then the current session (if any). */
  async init(): Promise<void> {
    try {
      await this.refreshAntiforgery();
      this.state.set(await firstValueFrom(this.http.get<Me>('/api/v1/me')));
    } catch {
      this.state.set(null);
    }
  }

  async signIn(email: string, password: string): Promise<Me> {
    const me = await firstValueFrom(this.http.post<Me>('/api/v1/auth/login', { email, password }));
    this.state.set(me);
    // Antiforgery tokens are tied to the signed-in user, so fetch a new one.
    await this.refreshAntiforgery();
    return me;
  }

  async signOut(): Promise<void> {
    try {
      await firstValueFrom(this.http.post('/api/v1/auth/logout', {}));
    } finally {
      this.markSignedOut();
      await this.refreshAntiforgery().catch(() => undefined);
    }
  }

  /** The server rejected the session (expired, signed out elsewhere, deactivated). */
  markSignedOut(): void {
    this.state.set(null);
  }

  async reloadMe(): Promise<void> {
    try {
      this.state.set(await firstValueFrom(this.http.get<Me>('/api/v1/me')));
    } catch (error) {
      if (asApiError(error).status === 401) {
        this.markSignedOut();
      }
    }
  }

  setMe(me: Me): void {
    this.state.set(me);
  }

  hasPermission(code: string, minimumScope: PermissionScope = 'Global'): boolean {
    const grant = this.state()?.permissions.find((p) => p.code === code);
    return !!grant && scopeAtLeast(grant.scope, minimumScope);
  }

  async refreshAntiforgery(): Promise<void> {
    await firstValueFrom(this.http.get('/api/v1/auth/antiforgery'));
  }
}
