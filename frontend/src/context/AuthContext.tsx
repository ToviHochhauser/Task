import { createContext, useContext, useState, useEffect, useCallback, type ReactNode } from "react";
import api, { refreshApi } from "../services/api";

interface User {
  username: string;
  fullName: string;
  role: string;
}

interface AuthContextType {
  user: User | null;
  token: string | null;
  login: (newToken: string, newRefreshToken: string, newUser: User) => void;
  logout: () => void;
  isAdmin: boolean;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

// Decode JWT payload to check expiry (8.4)
function getTokenExp(token: string): number | null {
  try {
    const payload = JSON.parse(atob(token.split(".")[1]));
    return payload.exp ?? null;
  } catch {
    return null;
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [token, setToken] = useState<string | null>(localStorage.getItem("token"));
  const [user, setUser] = useState<User | null>(() => {
    try {
      const saved = localStorage.getItem("user");
      return saved ? JSON.parse(saved) : null;
    } catch {
      localStorage.removeItem("token");
      localStorage.removeItem("refreshToken");
      localStorage.removeItem("user");
      return null;
    }
  });

  const clearAuth = useCallback(() => {
    setToken(null);
    setUser(null);
  }, []);

  // Listen for 401 events dispatched by the Axios interceptor (after refresh also fails)
  useEffect(() => {
    window.addEventListener("auth:unauthorized", clearAuth);
    return () => window.removeEventListener("auth:unauthorized", clearAuth);
  }, [clearAuth]);

  // Multi-tab sync: listen for storage changes from other tabs (8.2)
  useEffect(() => {
    const handleStorage = (e: StorageEvent) => {
      if (e.key === "token") {
        setToken(e.newValue);
        if (!e.newValue) setUser(null);
      }
      if (e.key === "refreshToken" && !e.newValue) {
        // Refresh token removed in another tab — mirror logout
        setToken(null);
        setUser(null);
      }
      if (e.key === "user") {
        try {
          setUser(e.newValue ? JSON.parse(e.newValue) : null);
        } catch {
          setUser(null);
        }
      }
    };
    window.addEventListener("storage", handleStorage);
    return () => window.removeEventListener("storage", handleStorage);
  }, []);

  // #17: Proactive silent refresh — attempt when access token has < 2 minutes left (8.4)
  useEffect(() => {
    if (!token) return;

    const exp = getTokenExp(token);
    if (!exp) return;

    const checkExpiry = async () => {
      const nowSec = Math.floor(Date.now() / 1000);
      const remainingSec = exp - nowSec;

      if (remainingSec > 120) return; // Still plenty of time

      const storedRefreshToken = localStorage.getItem("refreshToken");
      if (!storedRefreshToken) {
        // No refresh token — log out now
        localStorage.removeItem("token");
        localStorage.removeItem("user");
        clearAuth();
        window.dispatchEvent(new CustomEvent("auth:expired"));
        return;
      }

      try {
        const { data } = await refreshApi.post("/auth/refresh", { refreshToken: storedRefreshToken });
        localStorage.setItem("token", data.token);
        localStorage.setItem("refreshToken", data.refreshToken);
        setToken(data.token);
      } catch {
        // Silent refresh failed — log out
        localStorage.removeItem("token");
        localStorage.removeItem("refreshToken");
        localStorage.removeItem("user");
        clearAuth();
        window.dispatchEvent(new CustomEvent("auth:expired"));
      }
    };

    checkExpiry();
    const interval = setInterval(checkExpiry, 30_000);
    return () => clearInterval(interval);
  }, [token, clearAuth]);

  const login = (newToken: string, newRefreshToken: string, newUser: User) => {
    localStorage.setItem("token", newToken);
    localStorage.setItem("refreshToken", newRefreshToken);
    localStorage.setItem("user", JSON.stringify(newUser));
    setToken(newToken);
    setUser(newUser);
  };

  const logout = () => {
    const storedRefreshToken = localStorage.getItem("refreshToken");
    localStorage.removeItem("token");
    localStorage.removeItem("refreshToken");
    localStorage.removeItem("user");
    setToken(null);
    setUser(null);

    // Fire-and-forget: revoke the refresh token server-side
    if (storedRefreshToken) {
      api.post("/auth/logout", { refreshToken: storedRefreshToken }).catch(() => {
        // Ignore errors — logout is best-effort on the server side
      });
    }
  };

  const isAdmin = user?.role === "Admin";

  return (
    <AuthContext.Provider value={{ user, token, login, logout, isAdmin }}>
      {children}
    </AuthContext.Provider>
  );
}

// eslint-disable-next-line react-refresh/only-export-components
export const useAuth = (): AuthContextType => {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used within an AuthProvider");
  return ctx;
};
