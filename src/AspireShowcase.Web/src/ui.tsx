// An error the user should see, announced to screen readers.
export function ErrorMessage({ message }: { message: string }) {
  return (
    <div className="error-message" role="alert">
      <span>{message}</span>
    </div>
  );
}
