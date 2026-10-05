import { signInUrl, useSession } from './session.ts';

// Sign-in link, or the signed-in user's name with a sign-out button.
export function Account() {
  const { signInEnabled, user } = useSession();
  if (!signInEnabled) return null;

  if (!user) {
    return (
      <div className="account">
        <a className="button button-secondary" href={signInUrl(window.location.pathname)}>
          Sign in
        </a>
      </div>
    );
  }

  return (
    <div className="account">
      <span className="account-name">
        {user.name}
        {user.isOwner && <span className="badge">Owner</span>}
      </span>
      {/* A form post, so the browser follows bff's redirect to Logto's sign-out. */}
      <form method="post" action="/bff/logout">
        <button className="button button-secondary" type="submit">
          Sign out
        </button>
      </form>
    </div>
  );
}
