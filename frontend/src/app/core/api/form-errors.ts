import { AbstractControl, FormGroup } from '@angular/forms';
import { ApiError } from './api-error';

/**
 * Shows the server's field validation messages on the matching form controls.
 * Returns the messages that did not match any control, so the page can show them as a general error.
 */
export function applyServerErrors(form: FormGroup, error: ApiError): string[] {
  const unmatched: string[] = [];
  for (const [field, messages] of Object.entries(error.fieldErrors)) {
    const control: AbstractControl | null = form.get(field);
    if (control) {
      control.setErrors({ server: messages.join(' ') });
      control.markAsTouched();
    } else {
      unmatched.push(...messages);
    }
  }
  return unmatched;
}

/** Message for a control: the server's text, or a sensible default for client-side validators. */
export function controlError(control: AbstractControl | null, label: string): string | null {
  if (!control?.errors) {
    return null;
  }
  const errors = control.errors;
  if (errors['server']) return errors['server'] as string;
  if (errors['required']) return `${label} is required.`;
  if (errors['email']) return 'Enter a valid email address.';
  if (errors['minlength']) return `${label} must be at least ${errors['minlength'].requiredLength} characters.`;
  if (errors['maxlength']) return `${label} must be at most ${errors['maxlength'].requiredLength} characters.`;
  if (errors['min'] || errors['max']) return `${label} is out of range.`;
  if (errors['mismatch']) return 'The passwords do not match.';
  return `${label} is invalid.`;
}
