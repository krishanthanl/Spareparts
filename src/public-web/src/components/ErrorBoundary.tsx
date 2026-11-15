import { Component, type ReactNode } from 'react';
export class ErrorBoundary extends Component<
  { children: ReactNode },
  { failed: boolean }
> {
  state = { failed: false };
  static getDerivedStateFromError() {
    return { failed: true };
  }
  render() {
    return this.state.failed ? (
      <main className="error-page" role="alert">
        <h1>This page could not be displayed</h1>
        <p>Reload the page to try again.</p>
        <button onClick={() => window.location.reload()}>Reload page</button>
      </main>
    ) : (
      this.props.children
    );
  }
}
