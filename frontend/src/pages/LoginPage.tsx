import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "../context/AuthContext";
import api from "../services/api";
import { getApiError } from "../utils/formatters";
import { Clock, Eye, EyeOff, Loader2 } from "lucide-react";

export default function LoginPage() {
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);
  const { login } = useAuth();
  const navigate = useNavigate();

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError("");
    setLoading(true);

    try {
      const { data } = await api.post("/auth/login", { username, password });
      login(data.token, data.refreshToken, {
        username: data.username,
        fullName: data.fullName,
        role: data.role,
      });
      navigate("/");
    } catch (err: unknown) {
      setError(getApiError(err, "Login failed. Please try again."));
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="flex min-h-screen items-center justify-center bg-bg px-4 dark:bg-dark-bg">
      <div className="w-full max-w-[400px] rounded-2xl border border-border bg-card p-8 shadow-md sm:p-10 dark:border-dark-border dark:bg-dark-card">
        {/* Logo */}
        <div className="mb-8 flex items-center gap-3">
          <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-primary text-white shadow-[0_4px_12px_rgba(13,148,136,0.3)]">
            <Clock className="h-6 w-6" />
          </div>
          <div>
            <h1 className="text-xl font-bold tracking-tight text-text dark:text-dark-text">
              TimeClock
            </h1>
            <p className="text-xs text-text-muted dark:text-dark-text-muted">
              Workforce time tracking
            </p>
          </div>
        </div>

        <h2 className="mb-1 text-[1.375rem] font-bold tracking-tight text-text dark:text-dark-text">
          Welcome back
        </h2>
        <p className="mb-7 text-sm text-text-muted dark:text-dark-text-muted">
          Sign in to your account to continue
        </p>

        {error && (
          <div
            role="alert"
            className="mb-4 rounded-lg border border-red-300 bg-danger-light px-4 py-3 text-sm text-danger dark:border-red-800 dark:bg-red-950 dark:text-red-400"
          >
            {error}
          </div>
        )}

        <form onSubmit={handleSubmit}>
          <div className="mb-4">
            <label
              htmlFor="username"
              className="mb-1.5 block text-[0.8125rem] font-semibold text-text dark:text-dark-text"
            >
              Username
            </label>
            <input
              id="username"
              type="text"
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              placeholder="Enter your username"
              required
              maxLength={50}
              autoFocus
              className="w-full rounded-lg border-[1.5px] border-border bg-white px-3.5 py-2.5 text-[0.9rem] text-text outline-none transition-all placeholder:text-gray-400 focus:border-primary focus:ring-2 focus:ring-primary/15 dark:border-dark-border dark:bg-dark-bg dark:text-dark-text dark:placeholder:text-gray-500 dark:focus:border-primary dark:focus:ring-primary/25"
            />
          </div>
          <div className="mb-6">
            <label
              htmlFor="password"
              className="mb-1.5 block text-[0.8125rem] font-semibold text-text dark:text-dark-text"
            >
              Password
            </label>
            <div className="relative">
              <input
                id="password"
                type={showPassword ? "text" : "password"}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="Enter your password"
                required
                maxLength={128}
                className="w-full rounded-lg border-[1.5px] border-border bg-white px-3.5 py-2.5 pr-10 text-[0.9rem] text-text outline-none transition-all placeholder:text-gray-400 focus:border-primary focus:ring-2 focus:ring-primary/15 dark:border-dark-border dark:bg-dark-bg dark:text-dark-text dark:placeholder:text-gray-500 dark:focus:border-primary dark:focus:ring-primary/25"
              />
              <button
                type="button"
                onClick={() => setShowPassword(!showPassword)}
                className="absolute right-3 top-1/2 -translate-y-1/2 text-text-muted transition-colors hover:text-text dark:text-dark-text-muted dark:hover:text-dark-text"
                aria-label={showPassword ? "Hide password" : "Show password"}
              >
                {showPassword ? (
                  <EyeOff className="h-4 w-4" />
                ) : (
                  <Eye className="h-4 w-4" />
                )}
              </button>
            </div>
          </div>
          <button
            type="submit"
            className="flex w-full items-center justify-center gap-2 rounded-lg bg-primary px-4 py-3 text-[0.9375rem] font-semibold text-white shadow-[0_1px_4px_rgba(13,148,136,0.25)] transition-all hover:bg-primary-dark hover:shadow-[0_4px_12px_rgba(13,148,136,0.35)] disabled:cursor-not-allowed disabled:opacity-55"
            disabled={loading}
          >
            {loading && <Loader2 className="h-4 w-4 animate-spin" />}
            {loading ? "Signing in..." : "Sign In"}
          </button>
        </form>
      </div>
    </div>
  );
}
