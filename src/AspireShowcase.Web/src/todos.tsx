import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { useLogto } from '@logto/react';

type Todo = {
  id: number;
  title: string;
  isDone: boolean;
  createdAt: string;
};

// The /api/todos endpoints only answer signed-in users, so without sign-in there is no list.
export function TodosUnavailable() {
  return (
    <section className="card todos-card" aria-labelledby="todos-heading">
      <div className="section-header">
        <h2 id="todos-heading" className="section-title">To-do list</h2>
      </div>
      <p className="protected-hint">The to-do list needs sign-in, which isn't set up yet.</p>
    </section>
  );
}

// To-do list kept in the app's PostgreSQL database, through the /api/todos CRUD endpoints,
// called with an access token for the API resource.
// onChanged runs after every add, tick and delete, so the notifications card can reload.
export function Todos({ apiResource, onChanged }: { apiResource: string; onChanged: () => void }) {
  const { isAuthenticated, getAccessToken } = useLogto();
  const [todos, setTodos] = useState<Todo[]>([]);
  const [title, setTitle] = useState('');
  const [error, setError] = useState<string>();

  const request = useCallback(
    async <T,>(path: string, method = 'GET', body?: unknown): Promise<T | undefined> => {
      const token = await getAccessToken(apiResource);
      const response = await fetch(path, {
        method,
        headers: {
          ...(token ? { Authorization: `Bearer ${token}` } : {}),
          ...(body ? { 'Content-Type': 'application/json' } : {}),
        },
        body: body ? JSON.stringify(body) : undefined,
      });
      if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`);
      return response.status === 204 ? undefined : await response.json();
    },
    [getAccessToken, apiResource],
  );

  // Runs a change against the API, then reloads the list so it shows what the database holds.
  const run = async (change?: () => Promise<unknown>) => {
    setError(undefined);
    try {
      await change?.();
      if (change) onChanged();
      setTodos((await request<Todo[]>('/api/todos')) ?? []);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to call the API');
    }
  };

  useEffect(() => {
    if (!isAuthenticated) {
      setTodos([]);
      return;
    }
    request<Todo[]>('/api/todos')
      .then((list) => setTodos(list ?? []))
      .catch((err) => setError(err instanceof Error ? err.message : 'Failed to call the API'));
  }, [isAuthenticated, request]);

  const add = (event: FormEvent) => {
    event.preventDefault();
    if (!title.trim()) return;
    void run(() => request('/api/todos', 'POST', { title })).then(() => setTitle(''));
  };

  if (!isAuthenticated) {
    return (
      <section className="card todos-card" aria-labelledby="todos-heading">
        <div className="section-header">
          <h2 id="todos-heading" className="section-title">To-do list</h2>
        </div>
        <p className="protected-hint">Sign in to see and change the to-do list.</p>
      </section>
    );
  }

  return (
    <section className="card todos-card" aria-labelledby="todos-heading">
      <div className="section-header">
        <h2 id="todos-heading" className="section-title">To-do list</h2>
      </div>
      <form className="todo-form" onSubmit={add}>
        <input
          className="todo-input"
          value={title}
          onChange={(event) => setTitle(event.target.value)}
          placeholder="What needs doing?"
          aria-label="New to-do"
          maxLength={200}
        />
        <button className="account-button" type="submit" disabled={!title.trim()}>
          Add
        </button>
      </form>
      {error && (
        <div className="error-message" role="alert">
          <span>{error}</span>
        </div>
      )}
      {todos.length > 0 && (
        <ul className="todo-list">
          {todos.map((todo) => (
            <li key={todo.id} className="todo-item">
              <label className={`todo-label ${todo.isDone ? 'done' : ''}`}>
                <input
                  type="checkbox"
                  checked={todo.isDone}
                  onChange={() =>
                    void run(() =>
                      request(`/api/todos/${todo.id}`, 'PUT', { title: todo.title, isDone: !todo.isDone }),
                    )
                  }
                />
                <span>{todo.title}</span>
              </label>
              <button
                className="account-button"
                onClick={() => void run(() => request(`/api/todos/${todo.id}`, 'DELETE'))}
                aria-label={`Delete ${todo.title}`}
                type="button"
              >
                Delete
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
