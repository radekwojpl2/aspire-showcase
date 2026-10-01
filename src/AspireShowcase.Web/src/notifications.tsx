import { useEffect, useState } from 'react';

type Notification = {
  id: string;
  createdAt: string;
  kind: string;
  message: string;
};

// What the notifications service recorded: the API tells it about every to-do item added
// or removed, and passes its list on at /api/notifications. Reloads when `refresh` changes.
export function Notifications({ refresh }: { refresh: number }) {
  const [notifications, setNotifications] = useState<Notification[]>([]);
  const [error, setError] = useState<string>();

  useEffect(() => {
    let current = true;
    fetch('/api/notifications')
      .then(async (response) => {
        if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`);
        const list: Notification[] = await response.json();
        if (current) {
          setNotifications(list);
          setError(undefined);
        }
      })
      .catch((err) => {
        if (current) setError(err instanceof Error ? err.message : 'Failed to call the API');
      });
    return () => {
      current = false;
    };
  }, [refresh]);

  return (
    <section className="card todos-card" aria-labelledby="notifications-heading">
      <div className="section-header">
        <h2 id="notifications-heading" className="section-title">Notifications</h2>
      </div>
      {error && (
        <div className="error-message" role="alert">
          <span>{error}</span>
        </div>
      )}
      {!error && notifications.length === 0 && (
        <p className="protected-hint">Add or remove a to-do item to get one.</p>
      )}
      {notifications.length > 0 && (
        <ul className="todo-list" aria-live="polite">
          {notifications.slice(0, 5).map((notification) => (
            <li key={notification.id} className="todo-item notification-item">
              <time className="notification-time" dateTime={notification.createdAt}>
                {new Date(notification.createdAt).toLocaleTimeString()}
              </time>
              <span className="notification-message">{notification.message}</span>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
