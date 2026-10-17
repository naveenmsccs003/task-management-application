import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Me } from '../auth/auth.models';
import { AuthService } from '../auth/auth.service';
import { Shell } from './shell';

const admin: Me = {
  id: '1',
  email: 'admin@example.com',
  fullName: 'Ada Admin',
  timeZone: 'UTC',
  globalRole: { id: 'r', name: 'Admin' },
  permissions: [
    { code: 'members.view', scope: 'Global' },
    { code: 'audit.view', scope: 'Global' },
  ],
};

describe('Shell', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Shell],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
  });

  it('renders the application name, a skip link and the main landmark', async () => {
    const fixture = TestBed.createComponent(Shell);
    await fixture.whenStable();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('.brand')?.textContent).toContain('Task Management');
    expect(element.querySelector('a.skip-link')?.getAttribute('href')).toBe('#main-content');
    expect(element.querySelector('main#main-content')).not.toBeNull();
    expect(element.querySelector('nav')).toBeNull();
  });

  it('shows only the navigation the member has permission for', async () => {
    TestBed.inject(AuthService).setMe(admin);
    const fixture = TestBed.createComponent(Shell);
    await fixture.whenStable();

    const links = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('nav a')).map((a) => a.textContent?.trim());
    expect(links).toEqual(['Members', 'Audit log']);
  });
});
