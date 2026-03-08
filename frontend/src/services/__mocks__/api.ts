/**
 * Manual Vitest mock for src/services/api.ts.
 *
 * Activated in a test file with:
 *   vi.mock("../services/api");   // or the relative path to api.ts
 *
 * Every method returns a resolved promise with undefined by default.
 * Override per-test with:
 *   import api from "../services/api";
 *   vi.mocked(api.get).mockResolvedValueOnce({ data: { ... } });
 */
import { vi } from "vitest";

const api = {
  get: vi.fn(),
  post: vi.fn(),
  put: vi.fn(),
  delete: vi.fn(),
  patch: vi.fn(),
};

export default api;
