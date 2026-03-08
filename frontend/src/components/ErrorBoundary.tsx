import { Component, type ErrorInfo, type ReactNode } from "react";

interface Props {
  children: ReactNode;
}

interface State {
  hasError: boolean;
}

export default class ErrorBoundary extends Component<Props, State> {
  state: State = { hasError: false };

  static getDerivedStateFromError(): State {
    return { hasError: true };
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    console.error("Uncaught error:", error, errorInfo);
  }

  render() {
    if (this.state.hasError) {
      return (
        <div className="flex min-h-screen items-center justify-center bg-bg dark:bg-dark-bg">
          <div className="mx-4 max-w-md rounded-lg border border-border bg-card p-8 text-center shadow-md dark:border-dark-border dark:bg-dark-card">
            <div className="mb-4 text-4xl">Something went wrong</div>
            <p className="mb-6 text-text-muted dark:text-dark-text-muted">
              An unexpected error occurred. Please try again.
            </p>
            <div className="flex items-center justify-center gap-3">
              <button
                onClick={() => this.setState({ hasError: false })}
                className="rounded-md border border-border px-6 py-2.5 font-semibold text-text transition-colors hover:bg-border-light dark:border-dark-border dark:text-dark-text dark:hover:bg-dark-border"
              >
                Try Again
              </button>
              <button
                onClick={() => window.location.reload()}
                className="rounded-md bg-primary px-6 py-2.5 font-semibold text-white transition-colors hover:bg-primary-dark"
              >
                Refresh Page
              </button>
            </div>
          </div>
        </div>
      );
    }
    return this.props.children;
  }
}
