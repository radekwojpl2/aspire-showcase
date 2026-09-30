import { useEffect, useState } from 'react';
import {
  decodeAccessToken,
  useHandleSignInCallback,
  useLogto,
  type AccessTokenClaims,
} from '@logto/react';

// Where Logto sends the browser back after sign-in. It has to be listed under
// "Redirect URIs" on the Logto application, and the site root under "Post sign-out redirect URIs".
export const callbackPath = '/callback';

// Finishes the sign-in, then goes back to the home page.
export function SignInCallback() {
  const { error } = useHandleSignInCallback(() => window.location.replace('/'));

  return (
    <p className="auth-status" role="status">
      {error ? `Sign-in failed: ${error.message}` : 'Signing in...'}
    </p>
  );
}

type CurrentUser = {
  id: string | null;
  clientId: string | null;
  scopes: string | null;
};

// Calls the protected /api/me endpoint with an access token for the API resource,
// and shows the token it sent: the raw JWT and its decoded claims.
export function ProtectedData({ apiResource }: { apiResource: string }) {
  const { isAuthenticated, getAccessToken } = useLogto();
  const [user, setUser] = useState<CurrentUser>();
  const [token, setToken] = useState<string>();
  const [claims, setClaims] = useState<AccessTokenClaims>();
  const [error, setError] = useState<string>();

  const callApi = async () => {
    setError(undefined);
    try {
      const token = await getAccessToken(apiResource);
      setToken(token);
      setClaims(token ? decodeAccessToken(token) : undefined);
      const response = await fetch('/api/me', {
        headers: token ? { Authorization: `Bearer ${token}` } : {},
      });
      if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`);
      setUser(await response.json());
    } catch (err) {
      setUser(undefined);
      setError(err instanceof Error ? err.message : 'Failed to call the API');
    }
  };

  return (
    <section className="card protected-card" aria-labelledby="protected-heading">
      <div className="section-header">
        <h2 id="protected-heading" className="section-title">Protected endpoint</h2>
        <button className="account-button" onClick={() => void callApi()} type="button">
          Call /api/me
        </button>
      </div>
      {!isAuthenticated && <p className="protected-hint">Sign in first, or the API answers 401.</p>}
      {error && (
        <div className="error-message" role="alert">
          <span>{error}</span>
        </div>
      )}
      {user && (
        <>
          <h3 className="protected-label">Response</h3>
          <pre className="protected-result">{JSON.stringify(user, null, 2)}</pre>
        </>
      )}
      {claims && (
        <>
          <h3 className="protected-label">Access token claims</h3>
          <pre className="protected-result">{JSON.stringify(claims, null, 2)}</pre>
        </>
      )}
      {token && (
        <>
          <h3 className="protected-label">Access token (JWT)</h3>
          <pre className="protected-result protected-token">{token}</pre>
        </>
      )}
    </section>
  );
}

// Sign-in button, or the signed-in user's name with a sign-out button.
export function Account() {
  const { isAuthenticated, signIn, signOut, getIdTokenClaims } = useLogto();
  const [name, setName] = useState<string>();

  useEffect(() => {
    if (!isAuthenticated) return;
    void getIdTokenClaims().then((claims) =>
      setName(claims?.name ?? claims?.username ?? claims?.email ?? claims?.sub),
    );
  }, [isAuthenticated, getIdTokenClaims]);

  if (!isAuthenticated) {
    return (
      <div className="account">
        <button
          className="account-button"
          onClick={() => void signIn(window.location.origin + callbackPath)}
          type="button"
        >
          Sign in
        </button>
      </div>
    );
  }

  return (
    <div className="account">
      <span className="account-name">{name}</span>
      <button
        className="account-button"
        onClick={() => void signOut(window.location.origin)}
        type="button"
      >
        Sign out
      </button>
    </div>
  );
}
