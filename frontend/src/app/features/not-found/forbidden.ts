import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-forbidden',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>No access</h1>
    <p>You do not have permission to open this page. Ask an administrator if you need access.</p>
    <a routerLink="/">Go to the home page</a>
  `,
})
export class Forbidden {}
