import { useState } from "react";
import { Link, useNavigate, useLocation } from "react-router-dom";
import { useAuth } from "../context/AuthContext";
import { useTheme } from "../context/ThemeContext";
import { Menu, X, Moon, Sun, LogOut, Clock, Shield } from "lucide-react";
import clsx from "clsx";

function getInitials(name: string) {
  return name
    .split(" ")
    .map((n) => n[0])
    .join("")
    .slice(0, 2)
    .toUpperCase();
}

export default function Navbar() {
  const { user, logout, isAdmin } = useAuth();
  const { theme, toggleTheme } = useTheme();
  const navigate = useNavigate();
  const location = useLocation();
  const [mobileOpen, setMobileOpen] = useState(false);

  const handleLogout = () => {
    logout();
    navigate("/login");
  };

  if (!user) return null;

  const isActive = (path: string) => location.pathname === path;

  return (
    <nav className="sticky top-0 z-50 border-b border-primary-darker/30 bg-primary-darker shadow-md dark:border-dark-border dark:bg-dark-card">
      <div className="mx-auto flex h-14 max-w-7xl items-center justify-between px-4 sm:h-[60px] sm:px-6">
        {/* Brand */}
        <Link
          to="/"
          className="flex items-center gap-2 text-lg font-bold tracking-tight text-white no-underline"
        >
          <Clock className="h-5 w-5" aria-hidden="true" />
          TimeClock
        </Link>

        {/* Desktop nav links */}
        <div className="hidden items-center gap-1 md:flex">
          <Link
            to="/"
            className={clsx(
              "rounded-md px-3.5 py-1.5 text-sm font-medium transition-colors",
              isActive("/")
                ? "bg-white/15 text-white"
                : "text-white/75 hover:bg-white/10 hover:text-white"
            )}
          >
            Clock In/Out
          </Link>
          {isAdmin && (
            <Link
              to="/admin"
              className={clsx(
                "flex items-center gap-1.5 rounded-md px-3.5 py-1.5 text-sm font-medium transition-colors",
                isActive("/admin")
                  ? "bg-white/15 text-white"
                  : "text-white/75 hover:bg-white/10 hover:text-white"
              )}
            >
              <Shield className="h-3.5 w-3.5" aria-hidden="true" />
              Admin
            </Link>
          )}
        </div>

        {/* Right side: theme toggle + user + mobile menu button */}
        <div className="flex items-center gap-2 sm:gap-3">
          {/* Theme toggle */}
          <button
            onClick={toggleTheme}
            className="rounded-md p-2 text-white/70 transition-colors hover:bg-white/10 hover:text-white"
            aria-label={`Switch to ${theme === "light" ? "dark" : "light"} mode`}
          >
            {theme === "light" ? (
              <Moon className="h-4 w-4" />
            ) : (
              <Sun className="h-4 w-4" />
            )}
          </button>

          {/* Desktop user info */}
          <div className="hidden items-center gap-3 md:flex">
            <div className="flex items-center gap-2.5">
              <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full border-2 border-white/35 bg-white/15 text-xs font-bold text-white">
                {getInitials(user.fullName)}
              </div>
              <div className="leading-tight">
                <div className="text-[0.8125rem] font-medium text-white/90">{user.fullName}</div>
                <div className="text-[0.6875rem] text-white/50">{user.role}</div>
              </div>
            </div>
            <button
              onClick={handleLogout}
              className="flex items-center gap-1.5 rounded-md border border-white/25 px-3 py-1.5 text-[0.8rem] font-medium text-white/80 transition-colors hover:border-white/50 hover:bg-white/10 hover:text-white"
            >
              <LogOut className="h-3.5 w-3.5" aria-hidden="true" />
              Sign out
            </button>
          </div>

          {/* Mobile menu button */}
          <button
            onClick={() => setMobileOpen(!mobileOpen)}
            className="rounded-md p-2 text-white/80 transition-colors hover:bg-white/10 md:hidden"
            aria-label={mobileOpen ? "Close menu" : "Open menu"}
            aria-expanded={mobileOpen}
          >
            {mobileOpen ? <X className="h-5 w-5" /> : <Menu className="h-5 w-5" />}
          </button>
        </div>
      </div>

      {/* Mobile dropdown */}
      {mobileOpen && (
        <div className="border-t border-white/10 bg-primary-darker px-4 pb-4 pt-2 dark:bg-dark-card md:hidden">
          <div className="flex flex-col gap-1">
            <Link
              to="/"
              onClick={() => setMobileOpen(false)}
              className={clsx(
                "rounded-md px-3 py-2.5 text-sm font-medium transition-colors",
                isActive("/")
                  ? "bg-white/15 text-white"
                  : "text-white/75 hover:bg-white/10 hover:text-white"
              )}
            >
              Clock In/Out
            </Link>
            {isAdmin && (
              <Link
                to="/admin"
                onClick={() => setMobileOpen(false)}
                className={clsx(
                  "flex items-center gap-2 rounded-md px-3 py-2.5 text-sm font-medium transition-colors",
                  isActive("/admin")
                    ? "bg-white/15 text-white"
                    : "text-white/75 hover:bg-white/10 hover:text-white"
                )}
              >
                <Shield className="h-3.5 w-3.5" aria-hidden="true" />
                Admin Dashboard
              </Link>
            )}
          </div>

          {/* Mobile user section */}
          <div className="mt-3 border-t border-white/10 pt-3">
            <div className="flex items-center gap-2.5 px-3 pb-3">
              <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full border-2 border-white/35 bg-white/15 text-xs font-bold text-white">
                {getInitials(user.fullName)}
              </div>
              <div className="leading-tight">
                <div className="text-sm font-medium text-white/90">{user.fullName}</div>
                <div className="text-xs text-white/50">{user.role}</div>
              </div>
            </div>
            <button
              onClick={() => {
                setMobileOpen(false);
                handleLogout();
              }}
              className="flex w-full items-center gap-2 rounded-md px-3 py-2.5 text-sm font-medium text-white/75 transition-colors hover:bg-white/10 hover:text-white"
            >
              <LogOut className="h-4 w-4" aria-hidden="true" />
              Sign out
            </button>
          </div>
        </div>
      )}
    </nav>
  );
}
