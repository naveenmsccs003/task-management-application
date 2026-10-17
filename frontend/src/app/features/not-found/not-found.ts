import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-not-found',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>Page not found</h1>
    <p>The page you asked for does not exist, or you do not have access to it.</p>
    <a routerLink="/">Go to the home page</a>
  `,
})
export class NotFound {}
