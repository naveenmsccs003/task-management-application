import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Shell } from './shell';

describe('Shell', () => {
  it('renders the application name, a skip link and the main landmark', async () => {
    TestBed.configureTestingModule({ imports: [Shell], providers: [provideRouter([])] });

    const fixture = TestBed.createComponent(Shell);
    await fixture.whenStable();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('.brand')?.textContent).toContain('Task Management');
    expect(element.querySelector('a.skip-link')?.getAttribute('href')).toBe('#main-content');
    expect(element.querySelector('main#main-content')).not.toBeNull();
  });
});
