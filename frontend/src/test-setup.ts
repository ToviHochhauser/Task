// Extend Vitest's expect with @testing-library/jest-dom matchers
// (toBeInTheDocument, toHaveValue, toBeDisabled, etc.)
import "@testing-library/jest-dom/vitest";

import { afterEach, beforeEach, vi } from "vitest";
import { cleanup } from "@testing-library/react";

// RTL does not auto-cleanup when globals:false — wire it up explicitly.
afterEach(cleanup);

// ── localStorage / sessionStorage stub ────────────────────────────────────────
// jsdom provides localStorage, but we reset it before each test to avoid
// state bleed between tests.
beforeEach(() => {
  localStorage.clear();
  sessionStorage.clear();

  // Clear all vi.fn() call history so assertions stay isolated.
  vi.clearAllMocks();
});

// ── window.matchMedia stub ─────────────────────────────────────────────────────
// jsdom does not implement matchMedia. Components that call it (e.g. for
// dark-mode detection) would throw without this stub.
Object.defineProperty(window, "matchMedia", {
  writable: true,
  value: vi.fn().mockImplementation((query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: vi.fn(),
    removeListener: vi.fn(),
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    dispatchEvent: vi.fn(),
  })),
});
