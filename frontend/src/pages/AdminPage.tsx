import { useState, useEffect } from "react";
import api from "../services/api";
import toast from "react-hot-toast";
import type { TimeEntry } from "../types";
import type { Employee, EmployeeReport, AuditLog } from "../components/admin/types";
import EmployeeList from "../components/admin/EmployeeList";
import EmployeeReportPanel from "../components/admin/EmployeeReport";
import EditEntryModal from "../components/admin/EditEntryModal";
import AuditModal from "../components/admin/AuditModal";

export default function AdminPage() {
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [selectedEmployee, setSelectedEmployee] = useState<number | null>(null);
  const [report, setReport] = useState<EmployeeReport | null>(null);
  const [reportPage, setReportPage] = useState(1);

  // Date filters
  const [dateFrom, setDateFrom] = useState("");
  const [dateTo, setDateTo] = useState("");

  // Edit modal
  const [editingEntry, setEditingEntry] = useState<TimeEntry | null>(null);

  // Audit modal
  const [auditEntryId, setAuditEntryId] = useState<number | null>(null);
  const [auditLogs, setAuditLogs] = useState<AuditLog[]>([]);
  const [loadingAudit, setLoadingAudit] = useState(false);

  // Loading states
  const [loadingEmployees, setLoadingEmployees] = useState(false);
  const [loadingReport, setLoadingReport] = useState(false);

  useEffect(() => {
    fetchEmployees();
  }, []);

  const fetchEmployees = async () => {
    setLoadingEmployees(true);
    try {
      const { data } = await api.get("/admin/employees");
      setEmployees(data);
    } catch {
      toast.error("Failed to load employees.");
    } finally {
      setLoadingEmployees(false);
    }
  };

  const fetchReport = async (userId: number, page = 1) => {
    setSelectedEmployee(userId);
    setLoadingReport(true);
    try {
      const params: Record<string, string | number> = { page };
      if (dateFrom) params.from = dateFrom;
      if (dateTo) params.to = dateTo;
      const { data } = await api.get<EmployeeReport>(`/admin/reports/${userId}`, { params });
      setReport(data);
      setReportPage(data.currentPage);
    } catch {
      toast.error("Failed to load report.");
    } finally {
      setLoadingReport(false);
    }
  };

  const openAudit = async (entryId: number) => {
    setAuditEntryId(entryId);
    setAuditLogs([]);
    setLoadingAudit(true);
    try {
      const { data } = await api.get(`/admin/attendance/${entryId}/audit`);
      setAuditLogs(data);
    } catch {
      toast.error("Failed to load audit history.");
    } finally {
      setLoadingAudit(false);
    }
  };

  return (
    <div className="flex flex-col gap-5">
      <h2 className="text-xl font-bold tracking-tight text-text sm:text-2xl dark:text-dark-text">
        Admin Dashboard
      </h2>

      <div className="grid gap-5 md:grid-cols-[280px_1fr]">
        <EmployeeList
          employees={employees}
          selectedEmployee={selectedEmployee}
          loadingEmployees={loadingEmployees}
          onSelectEmployee={(id) => fetchReport(id, 1)}
          onEmployeesChanged={fetchEmployees}
        />

        <EmployeeReportPanel
          report={report}
          loadingReport={loadingReport}
          selectedEmployee={selectedEmployee}
          reportPage={reportPage}
          dateFrom={dateFrom}
          dateTo={dateTo}
          onDateFromChange={setDateFrom}
          onDateToChange={setDateTo}
          onFetchReport={fetchReport}
          onEditEntry={setEditingEntry}
          onOpenAudit={openAudit}
        />
      </div>

      <EditEntryModal
        entry={editingEntry}
        onClose={() => setEditingEntry(null)}
        onSaved={() => {
          if (selectedEmployee) fetchReport(selectedEmployee, reportPage);
        }}
      />

      <AuditModal
        entryId={auditEntryId}
        auditLogs={auditLogs}
        loading={loadingAudit}
        onClose={() => setAuditEntryId(null)}
      />
    </div>
  );
}