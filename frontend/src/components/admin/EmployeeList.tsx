import { useState } from "react";
import api from "../../services/api";
import toast from "react-hot-toast";
import { UserPlus, X, ChevronDown, ChevronUp, Users, Loader2, Power, DollarSign, Check } from "lucide-react";
import clsx from "clsx";
import { getApiError } from "../../utils/formatters";
import type { Employee } from "./types";
import { INPUT_CLASSES } from "./types";

interface Props {
  employees: Employee[];
  selectedEmployee: number | null;
  loadingEmployees: boolean;
  onSelectEmployee: (id: number) => void;
  onEmployeesChanged: () => void;
}

export default function EmployeeList({
  employees,
  selectedEmployee,
  loadingEmployees,
  onSelectEmployee,
  onEmployeesChanged,
}: Props) {
  const [showForm, setShowForm] = useState(false);
  const [newUsername, setNewUsername] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [newFullName, setNewFullName] = useState("");
  const [newRole, setNewRole] = useState("Employee");
  const [editingRateFor, setEditingRateFor] = useState<number | null>(null);
  const [rateInput, setRateInput] = useState("");
  const [togglingStatus, setTogglingStatus] = useState<number | null>(null);
  const [showEmployeeList, setShowEmployeeList] = useState(true);

  const handleCreateEmployee = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      await api.post("/admin/employees", {
        username: newUsername,
        password: newPassword,
        fullName: newFullName,
        role: newRole,
      });
      toast.success("Employee created successfully.");
      setShowForm(false);
      setNewUsername("");
      setNewPassword("");
      setNewFullName("");
      setNewRole("Employee");
      onEmployeesChanged();
    } catch (err: unknown) {
      toast.error(getApiError(err, "Failed to create employee."));
    }
  };

  const handleToggleStatus = async (emp: Employee) => {
    setTogglingStatus(emp.id);
    try {
      await api.put(`/admin/users/${emp.id}/status`, { isActive: !emp.isActive });
      toast.success(`${emp.fullName} ${emp.isActive ? "deactivated" : "activated"}.`);
      onEmployeesChanged();
    } catch (err: unknown) {
      toast.error(getApiError(err, "Failed to update status."));
    } finally {
      setTogglingStatus(null);
    }
  };

  const handleSaveRate = async (empId: number) => {
    const rate = parseFloat(rateInput);
    if (isNaN(rate) || rate < 0) {
      toast.error("Enter a valid hourly rate.");
      return;
    }
    try {
      await api.put(`/admin/users/${empId}/hourly-rate`, { hourlyRate: rate });
      toast.success("Hourly rate updated.");
      setEditingRateFor(null);
      setRateInput("");
      onEmployeesChanged();
    } catch (err: unknown) {
      toast.error(getApiError(err, "Failed to update rate."));
    }
  };

  return (
    <div className="h-fit rounded-xl border border-border bg-card p-5 shadow-sm dark:border-dark-border dark:bg-dark-card">
      {/* Mobile toggle */}
      <button
        onClick={() => setShowEmployeeList(!showEmployeeList)}
        className="flex w-full items-center justify-between md:hidden"
      >
        <h3 className="flex items-center gap-2 text-[0.9375rem] font-bold text-text dark:text-dark-text">
          <Users className="h-4 w-4" aria-hidden="true" />
          Employees
        </h3>
        {showEmployeeList ? (
          <ChevronUp className="h-4 w-4 text-text-muted" />
        ) : (
          <ChevronDown className="h-4 w-4 text-text-muted" />
        )}
      </button>
      {/* Desktop header */}
      <div className="mb-4 hidden items-center justify-between md:flex">
        <h3 className="text-[0.9375rem] font-bold text-text dark:text-dark-text">Employees</h3>
        <button
          className="inline-flex items-center gap-1.5 rounded-md bg-primary px-3 py-1.5 text-xs font-semibold text-white shadow-sm transition-colors hover:bg-primary-dark"
          onClick={() => setShowForm(!showForm)}
        >
          {showForm ? (
            <>
              <X className="h-3.5 w-3.5" /> Cancel
            </>
          ) : (
            <>
              <UserPlus className="h-3.5 w-3.5" /> Add
            </>
          )}
        </button>
      </div>

      <div className={clsx("mt-3 md:mt-0", !showEmployeeList && "hidden md:block")}>
        {/* Mobile add button */}
        <button
          className="mb-3 inline-flex w-full items-center justify-center gap-1.5 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-white shadow-sm transition-colors hover:bg-primary-dark md:hidden"
          onClick={() => setShowForm(!showForm)}
        >
          {showForm ? (
            <>
              <X className="h-3.5 w-3.5" /> Cancel
            </>
          ) : (
            <>
              <UserPlus className="h-3.5 w-3.5" /> Add Employee
            </>
          )}
        </button>

        {showForm && (
          <form className="mb-4 flex flex-col gap-2 border-b border-border pb-4 dark:border-dark-border" onSubmit={handleCreateEmployee}>
            <input className={INPUT_CLASSES} placeholder="Username" value={newUsername} onChange={(e) => setNewUsername(e.target.value)} required minLength={3} maxLength={50} pattern="[a-zA-Z0-9_]+" title="Letters, digits, and underscores only" />
            <input className={INPUT_CLASSES} placeholder="Password (min 8 chars, A-Z + 0-9)" type="password" value={newPassword} onChange={(e) => setNewPassword(e.target.value)} required minLength={8} maxLength={128} />
            <input className={INPUT_CLASSES} placeholder="Full Name" value={newFullName} onChange={(e) => setNewFullName(e.target.value)} required maxLength={200} />
            <select className={INPUT_CLASSES} value={newRole} onChange={(e) => setNewRole(e.target.value)}>
              <option value="Employee">Employee</option>
              <option value="Admin">Admin</option>
            </select>
            <button type="submit" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-white transition-colors hover:bg-primary-dark">
              Create
            </button>
          </form>
        )}

        {loadingEmployees ? (
          <div className="flex items-center justify-center py-6">
            <Loader2 className="h-5 w-5 animate-spin text-primary" />
          </div>
        ) : (
          <ul className="flex flex-col gap-1">
            {employees.map((emp) => (
              <li
                key={emp.id}
                className={clsx(
                  "rounded-lg transition-colors",
                  selectedEmployee === emp.id
                    ? "bg-primary/10 dark:bg-primary/20"
                    : "hover:bg-primary-xlight dark:hover:bg-dark-border/50"
                )}
              >
                <div
                  className="flex cursor-pointer items-center justify-between px-3 py-2.5"
                  onClick={() => onSelectEmployee(emp.id)}
                  role="button"
                  tabIndex={0}
                  onKeyDown={(e) => e.key === "Enter" && onSelectEmployee(emp.id)}
                >
                  <div className="flex flex-col">
                    <strong className={clsx(
                      "text-sm",
                      selectedEmployee === emp.id
                        ? "text-primary-darker dark:text-primary-light"
                        : "text-text dark:text-dark-text",
                      !emp.isActive && "line-through opacity-60"
                    )}>
                      {emp.fullName}
                    </strong>
                    {!emp.isActive && (
                      <span className="text-[0.625rem] font-semibold uppercase text-text-muted dark:text-dark-text-muted">
                        Inactive
                      </span>
                    )}
                    {editingRateFor === emp.id ? (
                      <div className="mt-1 flex items-center gap-1" onClick={(e) => e.stopPropagation()}>
                        <input
                          type="number"
                          min="0"
                          step="0.01"
                          value={rateInput}
                          onChange={(e) => setRateInput(e.target.value)}
                          className="w-16 rounded border border-border px-1 py-0.5 text-xs dark:border-dark-border dark:bg-dark-bg dark:text-dark-text"
                          placeholder="/h"
                          autoFocus
                          onKeyDown={(e) => { if (e.key === "Enter") handleSaveRate(emp.id); if (e.key === "Escape") { setEditingRateFor(null); setRateInput(""); } }}
                        />
                        <button onClick={() => handleSaveRate(emp.id)} className="rounded p-0.5 text-success hover:bg-green-100 dark:hover:bg-green-900/30">
                          <Check className="h-3 w-3" />
                        </button>
                        <button onClick={() => { setEditingRateFor(null); setRateInput(""); }} className="rounded p-0.5 text-text-muted hover:bg-border-light dark:hover:bg-dark-border">
                          <X className="h-3 w-3" />
                        </button>
                      </div>
                    ) : (
                      <button
                        className="mt-0.5 flex items-center gap-0.5 text-[0.625rem] text-text-muted hover:text-primary dark:text-dark-text-muted dark:hover:text-primary-light"
                        onClick={(e) => { e.stopPropagation(); setEditingRateFor(emp.id); setRateInput(emp.hourlyRate?.toString() ?? ""); }}
                        title="Set hourly rate"
                      >
                        <DollarSign className="h-2.5 w-2.5" />
                        {emp.hourlyRate != null ? `${emp.hourlyRate}/h` : "Set rate"}
                      </button>
                    )}
                  </div>
                  <div className="flex items-center gap-1.5">
                    <span
                      className={clsx(
                        "rounded-full px-2 py-0.5 text-[0.625rem] font-bold uppercase tracking-wide",
                        selectedEmployee === emp.id
                          ? "bg-primary/15 text-primary-dark dark:bg-primary/30 dark:text-primary-light"
                          : "bg-border-light text-text-muted dark:bg-dark-border dark:text-dark-text-muted"
                      )}
                    >
                      {emp.role}
                    </span>
                    <button
                      title={emp.isActive ? "Deactivate user" : "Activate user"}
                      onClick={(e) => { e.stopPropagation(); handleToggleStatus(emp); }}
                      disabled={togglingStatus === emp.id}
                      className={clsx(
                        "rounded p-1 transition-colors",
                        emp.isActive
                          ? "text-text-muted hover:bg-red-100 hover:text-red-600 dark:hover:bg-red-900/30 dark:hover:text-red-400"
                          : "text-success hover:bg-green-100 dark:hover:bg-green-900/30"
                      )}
                    >
                      {togglingStatus === emp.id
                        ? <Loader2 className="h-3.5 w-3.5 animate-spin" />
                        : <Power className="h-3.5 w-3.5" aria-hidden="true" />
                      }
                    </button>
                  </div>
                </div>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}
