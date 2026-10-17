import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { SignIn } from './sign-in';

describe('SignIn', () => {
  let auth: { signIn: ReturnType<typeof vi.fn> };
  let navigate: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    auth = { signIn: vi.fn() };
    TestBed.configureTestingModule({
      imports: [SignIn],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), { provide: AuthService, useValue: auth }],
    });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  });

  async function submit(returnUrl?: string) {
    const fixture = TestBed.createComponent(SignIn);
    if (returnUrl) fixture.componentRef.setInput('returnUrl', returnUrl);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    const email = element.querySelector<HTMLInputElement>('input[type=email]')!;
    const password = element.querySelector<HTMLInputElement>('input[type=password]')!;
    email.value = 'dev@example.com';
    email.dispatchEvent(new Event('input'));
    password.value = 'river-copper-lantern-42';
    password.dispatchEvent(new Event('input'));
    element.querySelector<HTMLButtonElement>('button[type=submit]')!.click();
    await fixture.whenStable();
    return fixture;
  }

  it('signs in and returns to the requested page', async () => {
    auth.signIn.mockResolvedValue({});

    await submit('/admin/members');

    expect(auth.signIn).toHaveBeenCalledWith('dev@example.com', 'river-copper-lantern-42');
    expect(navigate).toHaveBeenCalledWith('/admin/members');
  });

  it('never redirects to another site after sign-in', async () => {
    auth.signIn.mockResolvedValue({});

    await submit('//evil.example.com/phish');

    expect(navigate).toHaveBeenCalledWith('/');
  });

  it('shows the server message and clears the password on failure', async () => {
    auth.signIn.mockRejectedValue({ status: 401, code: 'auth.invalid_credentials', message: 'Email or password is incorrect.', fieldErrors: {} });

    const fixture = await submit();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('[role=alert]')?.textContent).toContain('Email or password is incorrect.');
    expect(element.querySelector<HTMLInputElement>('input[type=password]')!.value).toBe('');
    expect(navigate).not.toHaveBeenCalled();
  });
});
