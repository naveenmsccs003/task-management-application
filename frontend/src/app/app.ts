import { ChangeDetectionStrategy, Component } from '@angular/core';
import { Shell } from './core/layout/shell';

@Component({
  selector: 'app-root',
  imports: [Shell],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<app-shell />',
})
export class App {}
