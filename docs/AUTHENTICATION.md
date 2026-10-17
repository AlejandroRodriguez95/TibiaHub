# Browser Authentication

TibiaHub uses ASP.NET Core Identity with cookies for Angular. Identity continues to manage password hashing, user storage, and lockout. JWT issuance and refresh tokens are not part of the current implementation. Future non-browser clients can use another mechanism when required.

## Endpoints

| Method | Path | Behavior |
| --- | --- | --- |
| GET | `/account/csrf` | Sets an antiforgery cookie and returns `{ token }`. |
| POST | `/account/register` | Creates a user; does not sign them in. |
| POST | `/account/login` | Validates credentials, sets the Identity cookie, and returns `{ id, email }`. |
| GET | `/account/me` | Returns `{ id, email }` for the current user, or 401. |
| POST | `/account/logout` | Requires authentication, clears the cookie, and returns 204. |

All account POST requests require the antiforgery cookie and the corresponding token in `X-CSRF-TOKEN`. Missing or invalid tokens return 400. Fetch a new token after authentication changes: tokens are bound to the current identity. Angular fetches a fresh token before each write. Account responses are marked `no-store`.

The browser stores the HttpOnly authentication cookie; Angular does not store credentials or tokens in localStorage. The session cookie is not persistent, and Identity controls ticket expiration and renewal. Logout clears the browser cookie; it does not revoke a copied cookie held elsewhere. Account disabling and immediate revocation need additional policy if introduced.

## Local Development

Start the API with the HTTP launch profile on `http://localhost:5267` and Angular on `http://localhost:4200`. Use `localhost` consistently rather than mixing it with `127.0.0.1`.

Angular sends account requests with `withCredentials: true`. Development CORS permits that exact Angular origin and credentials. Authentication cookies use `SameSite=Lax`, `HttpOnly`, and HTTPS-only transport outside Development. Anonymous API requests return 401 rather than redirecting to a login page; forbidden requests return 403.

To try the API with an HTTP client, keep its cookie jar enabled, fetch `/account/csrf`, then include the returned token header when registering or logging in. After login, `/account/me` should return your user. Fetch a new CSRF token, log out, and verify `/account/me` returns 401 again.

## Deployment

Use HTTPS. Prefer serving Angular and the API from the same origin, with production API URLs configured for that deployment. The current Angular API URLs target local development. Production CORS is not enabled by the development policy. Hosting on different sites requires deliberate SameSite, Secure, CORS, and browser third-party-cookie consideration.

Persist ASP.NET Core Data Protection keys across restarts and share them between API instances when deploying multiple instances. Add email confirmation and password recovery before opening registration publicly.
