import { useEffect, useState } from 'react';
import { useHandleSignInCallback, useLogto } from '@logto/react';

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
