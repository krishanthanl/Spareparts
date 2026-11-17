import axios from 'axios';

export type ApiProblem = {
  message: string;
  fieldErrors: Record<string, string[]>;
  signInRequired: boolean;
};

/** Turns API errors into safe, readable text. Never shows raw server output. */
export function describeError(error: unknown): ApiProblem {
  if (axios.isAxiosError(error)) {
    const status = error.response?.status;
    const body = error.response?.data as
      | { detail?: string; title?: string; errors?: Record<string, string[]> }
      | undefined;
    if (status === 401)
      return {
        message: 'Your session has ended. Sign in again.',
        fieldErrors: {},
        signInRequired: true,
      };
    if (status === 403)
      return {
        message:
          body?.detail && body.title === 'Not allowed.'
            ? body.detail
            : 'Your account does not have permission for this.',
        fieldErrors: {},
        signInRequired: true,
      };
    if (status === 429)
      return {
        message: 'Too many attempts. Wait a minute and try again.',
        fieldErrors: {},
        signInRequired: false,
      };
    if (status === 400 && body?.errors)
      return {
        message: 'Check the highlighted fields.',
        fieldErrors: body.errors,
        signInRequired: false,
      };
    if ((status === 404 || status === 409) && body?.detail)
      return { message: body.detail, fieldErrors: {}, signInRequired: false };
    if (!error.response)
      return {
        message:
          'The shop service is not reachable. Check it is running, then try again.',
        fieldErrors: {},
        signInRequired: false,
      };
  }
  return {
    message: 'Something went wrong. Try again.',
    fieldErrors: {},
    signInRequired: false,
  };
}

/** Matches ASP.NET field keys case-insensitively, e.g. "Sku" or "Fitments[0].YearTo". */
export const fieldError = (problem: ApiProblem | null, field: string) => {
  if (!problem) return undefined;
  const key = Object.keys(problem.fieldErrors).find(
    (k) => k.toLowerCase() === field.toLowerCase(),
  );
  return key ? problem.fieldErrors[key][0] : undefined;
};
