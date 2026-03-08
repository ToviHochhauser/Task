import axios from "axios";

const BASE_URL = import.meta.env.VITE_API_URL || "http://localhost:5000/api";

const api = axios.create({ baseURL: BASE_URL, timeout: 10000 });

// Separate instance for refresh calls — must NOT go through the response interceptor
// to avoid an infinite loop when the refresh token itself is invalid.
const refreshApi = axios.create({ baseURL: BASE_URL, timeout: 10000 });

api.interceptors.request.use((config) => {
  const token = localStorage.getItem("token");
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

// #17: Queue of requests waiting for a token refresh to complete
let isRefreshing = false;
type QueueEntry = { resolve: (token: string) => void; reject: (err: unknown) => void };
let failedQueue: QueueEntry[] = [];

function processQueue(error: unknown, token: string | null) {
  failedQueue.forEach((entry) => {
    if (token) entry.resolve(token);
    else entry.reject(error);
  });
  failedQueue = [];
}

function clearAuthStorage() {
  localStorage.removeItem("token");
  localStorage.removeItem("refreshToken");
  localStorage.removeItem("user");
}

api.interceptors.response.use(
  (response) => response,
  async (error) => {
    // Network timeout detection (12.4)
    if (error.code === "ECONNABORTED") {
      error.isTimeout = true;
      error.friendlyMessage =
        "Request timed out. Your action may or may not have completed. Please check and retry.";
      return Promise.reject(error);
    }

    const originalRequest = error.config;

    if (error.response?.status === 401 && !originalRequest._retry) {
      const storedRefreshToken = localStorage.getItem("refreshToken");

      // No refresh token — clear auth immediately
      if (!storedRefreshToken) {
        clearAuthStorage();
        window.dispatchEvent(new Event("auth:unauthorized"));
        return Promise.reject(error);
      }

      // Another refresh is already in flight — queue this request
      if (isRefreshing) {
        return new Promise<string>((resolve, reject) => {
          failedQueue.push({ resolve, reject });
        })
          .then((newToken) => {
            originalRequest.headers.Authorization = `Bearer ${newToken}`;
            return api(originalRequest);
          })
          .catch((err) => Promise.reject(err));
      }

      originalRequest._retry = true;
      isRefreshing = true;

      try {
        const { data } = await refreshApi.post("/auth/refresh", {
          refreshToken: storedRefreshToken,
        });

        localStorage.setItem("token", data.token);
        localStorage.setItem("refreshToken", data.refreshToken);

        processQueue(null, data.token);
        originalRequest.headers.Authorization = `Bearer ${data.token}`;
        return api(originalRequest);
      } catch (refreshError) {
        processQueue(refreshError, null);
        clearAuthStorage();
        window.dispatchEvent(new Event("auth:unauthorized"));
        return Promise.reject(refreshError);
      } finally {
        isRefreshing = false;
      }
    }

    return Promise.reject(error);
  }
);

export { refreshApi };
export default api;
