import { FormControl, FormGroup } from '@angular/forms';
import { applyServerErrors, controlError } from './form-errors';

describe('form errors', () => {
  it('puts server messages on matching controls and returns the rest', () => {
    const form = new FormGroup({ email: new FormControl(''), fullName: new FormControl('') });

    const unmatched = applyServerErrors(form, {
      status: 400,
      code: 'validation_failed',
      message: 'Invalid',
      fieldErrors: { email: ['Enter a valid email address.'], other: ['Something else.'] },
    });

    expect(controlError(form.controls.email, 'Email')).toBe('Enter a valid email address.');
    expect(form.controls.email.touched).toBe(true);
    expect(form.controls.fullName.errors).toBeNull();
    expect(unmatched).toEqual(['Something else.']);
  });

  it('describes client-side validation errors', () => {
    expect(controlError(new FormControl('', { validators: () => ({ required: true }) }), 'Name')).toBe('Name is required.');
    expect(controlError(new FormControl('ok'), 'Name')).toBeNull();
  });
});
