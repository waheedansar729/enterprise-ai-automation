// import { useCallback, useEffect, useState } from "react";
// import { api, Metrics, ReviewDetail, ReviewListItem } from "./api";

// const POLL_MS = 10_000;
// const fmtSecs = (s: number | null | undefined) => (s == null ? "–" : s < 60 ? `${s.toFixed(1)}s` : `${(s / 60).toFixed(1)}m`);
// const fmtTime = (iso: string | null) => (iso ? new Date(iso).toLocaleString() : "–");
// const outcomeLabel = (r: string | null) =>
//   r === "needs_attention" ? "Needs attention" : r === "no_issues_found" ? "No issues found" : r === "insufficient_evidence" ? "Insufficient evidence" : "–";

// function Badge({ kind, children }: { kind: string; children: React.ReactNode }) {
//   return <span className={`badge ${kind.toLowerCase()}`}>{children}</span>;
// }

// function Overview({ m, apiReady }: { m: Metrics | null; apiReady: boolean | null }) {
//   const card = (label: string, value: React.ReactNode, warn = false) => (
//     <div className={`card ${warn ? "warn" : ""}`}><div className="v">{value}</div><div className="l">{label}</div></div>
//   );
//   if (!m) return <div className="cards">{card("Loading…", "…")}</div>;
//   const failRate = m.total ? Math.round((m.failed / m.total) * 100) : 0;
//   return (
//     <div className="cards">
//       {card("Reviews (last " + m.windowHours + "h)", m.total)}
//       {card("Queued / running", `${m.queued} / ${m.running}`)}
//       {card("Completed", m.completed)}
//       {card("Processing failures", `${m.failed} (${failRate}%)`, m.failed > 0)}
//       {card("ClickUp write-back failures", m.writeBackFailed, m.writeBackFailed > 0)}
//       {card("Median / p95 duration", `${fmtSecs(m.medianSeconds)} / ${fmtSecs(m.p95Seconds)}`)}
//       {card("Queue depth (oldest)", `${m.queueDepth} (${fmtSecs(m.oldestQueuedAgeSeconds)})`, (m.oldestQueuedAgeSeconds ?? 0) > 300)}
//       {card("API readiness", apiReady == null ? "…" : apiReady ? "Ready" : "Down", apiReady === false)}
//     </div>
//   );
// }

// function Detail({ id, onClose }: { id: string; onClose: () => void }) {
//   const [d, setD] = useState<ReviewDetail | null>(null);
//   useEffect(() => { api.detail(id).then(setD).catch(() => setD(null)); }, [id]);
//   return (
//     <aside className="drawer">
//       <button className="close" onClick={onClose}>Close</button>
//       {!d ? <p>Loading…</p> : (
//         <>
//           <h2>{d.title || d.taskId}</h2>
//           <p><a href={d.taskUrl} target="_blank" rel="noreferrer">Open in ClickUp</a></p>
//           <p><Badge kind={d.status}>{d.status}</Badge> <Badge kind={d.writeBackStatus}>Write-back: {d.writeBackStatus}</Badge> <b>{outcomeLabel(d.overallResult)}</b></p>
//           {d.summary && <p>{d.summary}</p>}
//           {d.error && <p className="err">Error: {d.error}</p>}
//           <h3>Findings ({d.findings.length})</h3>
//           {d.findings.length === 0 && <p className="muted">No findings.</p>}
//           {d.findings.map((f) => (
//             <div key={f.id} className="finding">
//               <Badge kind={f.severity}>{f.severity}</Badge> <b>{f.title}</b> <span className="muted">({f.category}, confidence {f.confidence})</span>
//               <p><i>Evidence:</i> {f.evidence}</p>
//               <p><i>Recommendation:</i> {f.recommendation}</p>
//             </div>
//           ))}
//           <h3>Limitations</h3>
//           <ul>{d.limitations.map((l, i) => <li key={i}>{l}</li>)}</ul>
//           <h3>Delivery attempts</h3>
//           {d.deliveryAttempts.length === 0 ? <p className="muted">None yet.</p> : (
//             <ul>{d.deliveryAttempts.map((a) => (
//               <li key={a.id}>#{a.attemptNumber} {a.destination}: {a.errorCode ?? `HTTP ${a.statusCode}`} <span className="muted">{fmtTime(a.attemptedAtUtc)}</span></li>
//             ))}</ul>
//           )}
//           <p className="muted">Job {d.id}<br />Model {d.model ?? "–"}, prompt {d.promptVersion ?? "–"}, attempts {d.attempts}<br />Correlation ID {d.correlationId}</p>
//         </>
//       )}
//     </aside>
//   );
// }

// export default function App() {
//   const [metrics, setMetrics] = useState<Metrics | null>(null);
//   const [rows, setRows] = useState<ReviewListItem[]>([]);
//   const [apiReady, setApiReady] = useState<boolean | null>(null);
//   const [status, setStatus] = useState("");
//   const [taskId, setTaskId] = useState("");
//   const [selected, setSelected] = useState<string | null>(null);
//   const [error, setError] = useState<string | null>(null);

//   const refresh = useCallback(async () => {
//     try {
//       const [m, l, ready] = await Promise.all([api.metrics(), api.list(status, taskId), api.ready().catch(() => false)]);
//       setMetrics(m); setRows(l.items); setApiReady(ready); setError(null);
//     } catch (e) {
//       setApiReady(false); setError("Cannot reach the API. Is it running?");
//     }
//   }, [status, taskId]);

//   useEffect(() => { refresh(); const t = setInterval(refresh, POLL_MS); return () => clearInterval(t); }, [refresh]);

//   return (
//     <main>
//       <header>
//         <h1>AI Review Dashboard</h1>
//         <p className="muted">AI findings are recommendations. An engineer must verify them. Refreshes every {POLL_MS / 1000}s.</p>
//       </header>
//       {error && <p className="err">{error}</p>}
//       <Overview m={metrics} apiReady={apiReady} />

//       <div className="filters">
//         <select value={status} onChange={(e) => setStatus(e.target.value)} aria-label="Filter by status">
//           <option value="">All statuses</option>
//           {["Queued", "Running", "Completed", "Failed"].map((s) => <option key={s}>{s}</option>)}
//         </select>
//         <input placeholder="Task ID" value={taskId} onChange={(e) => setTaskId(e.target.value)} aria-label="Filter by task ID" />
//       </div>

//       <div className="tablewrap">
//         <table>
//           <thead><tr><th>Task</th><th>Status</th><th>Outcome</th><th>Findings</th><th>Duration</th><th>Write-back</th><th>Submitted</th></tr></thead>
//           <tbody>
//             {rows.length === 0 && <tr><td colSpan={7} className="muted">No reviews yet. Send one with scripts/smoke-test.</td></tr>}
//             {rows.map((r) => (
//               <tr key={r.id} onClick={() => setSelected(r.id)} className="click">
//                 <td>{r.title || r.taskId}<div className="muted">{r.taskId}</div></td>
//                 <td><Badge kind={r.status}>{r.status}</Badge></td>
//                 <td>{outcomeLabel(r.overallResult)}</td>
//                 <td>{r.findingCount}</td>
//                 <td>{fmtSecs(r.durationSeconds)}</td>
//                 <td><Badge kind={r.writeBackStatus}>{r.writeBackStatus}</Badge></td>
//                 <td>{fmtTime(r.createdAtUtc)}</td>
//               </tr>
//             ))}
//           </tbody>
//         </table>
//       </div>
//       {selected && <Detail id={selected} onClose={() => setSelected(null)} />}
//     </main>
//   );
// }

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { api, Metrics, ReviewDetail, ReviewListItem } from "./api";
import "./styles.css"

const POLL_MS = 10_000;
const SEARCH_DEBOUNCE_MS = 350;

const fmtSecs = (s: number | null | undefined) =>
  s == null ? "–" : s < 60 ? `${s.toFixed(1)}s` : `${(s / 60).toFixed(1)}m`;

const fmtTime = (iso: string | null) =>
  iso
    ? new Date(iso).toLocaleString(undefined, {
        dateStyle: "medium",
        timeStyle: "short",
      })
    : "–";

const outcomeLabel = (r: string | null) =>
  r === "needs_attention"
    ? "Needs attention"
    : r === "no_issues_found"
    ? "No issues found"
    : r === "insufficient_evidence"
    ? "Insufficient evidence"
    : "–";

function Badge({ kind, children }: { kind: string; children: React.ReactNode }) {
  return <span className={`badge ${String(kind).toLowerCase()}`}>{children}</span>;
}

/* ---------------- Overview ---------------- */

function Overview({ m, apiReady }: { m: Metrics | null; apiReady: boolean | null }) {
  const card = (label: string, value: React.ReactNode, warn = false) => (
    <div className={`card ${warn ? "warn" : ""}`} role="group" aria-label={label}>
      <div className="v">{value}</div>
      <div className="l">{label}</div>
    </div>
  );

  if (!m) {
    return (
      <div className="cards">
        {Array.from({ length: 8 }).map((_, i) => (
          <div key={i} className="card skeleton" aria-hidden>
            <div className="sk-line sk-v" />
            <div className="sk-line sk-l" />
          </div>
        ))}
      </div>
    );
  }

  const failRate = m.total ? Math.round((m.failed / m.total) * 100) : 0;

  return (
    <div className="cards" aria-live="polite">
      {card(`Reviews (last ${m.windowHours}h)`, m.total)}
      {card("Queued / running", `${m.queued} / ${m.running}`)}
      {card("Completed", m.completed)}
      {card("Processing failures", `${m.failed} (${failRate}%)`, m.failed > 0)}
      {card("ClickUp write-back failures", m.writeBackFailed, m.writeBackFailed > 0)}
      {card(
        "Median / p95 duration",
        `${fmtSecs(m.medianSeconds)} / ${fmtSecs(m.p95Seconds)}`
      )}
      {card(
        "Queue depth (oldest)",
        `${m.queueDepth} (${fmtSecs(m.oldestQueuedAgeSeconds)})`,
        (m.oldestQueuedAgeSeconds ?? 0) > 300
      )}
      {card(
        "API readiness",
        apiReady == null ? "…" : apiReady ? "Ready" : "Down",
        apiReady === false
      )}
    </div>
  );
}

/* ---------------- Detail Drawer ---------------- */

function Detail({ id, onClose }: { id: string; onClose: () => void }) {
  const [d, setD] = useState<ReviewDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState<string | null>(null);
  const closeRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    let cancel = false;
    setLoading(true);
    setErr(null);
    api
      .detail(id)
      .then((res) => !cancel && setD(res))
      .catch(() => !cancel && setErr("Failed to load review detail."))
      .finally(() => !cancel && setLoading(false));
    return () => {
      cancel = true;
    };
  }, [id]);

  // Esc closes drawer
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", onKey);
    closeRef.current?.focus();
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  return (
    <>
      <div className="backdrop" onClick={onClose} aria-hidden />
      <aside
        className="drawer"
        role="dialog"
        aria-modal="true"
        aria-label="Review detail"
      >
        <button ref={closeRef} className="close" onClick={onClose} aria-label="Close">
          ✕
        </button>

        {loading && (
          <div className="drawer-skel" aria-hidden>
            <div className="sk-line" style={{ width: "70%", height: 20 }} />
            <div className="sk-line" style={{ width: "40%" }} />
            <div className="sk-line" style={{ width: "100%" }} />
            <div className="sk-line" style={{ width: "90%" }} />
          </div>
        )}

        {err && <p className="err">{err}</p>}

        {d && !loading && (
          <>
            <h2>{d.title || d.taskId}</h2>
            <p>
              <a href={d.taskUrl} target="_blank" rel="noreferrer">
                Open in ClickUp ↗
              </a>
            </p>
            <p>
              <Badge kind={d.status}>{d.status}</Badge>{" "}
              <Badge kind={d.writeBackStatus}>Write-back: {d.writeBackStatus}</Badge>{" "}
              <b>{outcomeLabel(d.overallResult)}</b>
            </p>
            {d.summary && <p>{d.summary}</p>}
            {d.error && <p className="err">Error: {d.error}</p>}

            <h3>Findings ({d.findings.length})</h3>
            {d.findings.length === 0 && <p className="muted">No findings.</p>}
            {d.findings.map((f) => (
              <div key={f.id} className="finding">
                <Badge kind={f.severity}>{f.severity}</Badge> <b>{f.title}</b>{" "}
                <span className="muted">
                  ({f.category}, confidence {f.confidence})
                </span>
                <p>
                  <i>Evidence:</i> {f.evidence}
                </p>
                <p>
                  <i>Recommendation:</i> {f.recommendation}
                </p>
              </div>
            ))}

            <h3>Limitations</h3>
            {d.limitations.length === 0 ? (
              <p className="muted">None.</p>
            ) : (
              <ul>
                {d.limitations.map((l, i) => (
                  <li key={i}>{l}</li>
                ))}
              </ul>
            )}

            <h3>Delivery attempts</h3>
            {d.deliveryAttempts.length === 0 ? (
              <p className="muted">None yet.</p>
            ) : (
              <ul>
                {d.deliveryAttempts.map((a) => (
                  <li key={a.id}>
                    #{a.attemptNumber} {a.destination}:{" "}
                    {a.errorCode ?? `HTTP ${a.statusCode}`}{" "}
                    <span className="muted">{fmtTime(a.attemptedAtUtc)}</span>
                  </li>
                ))}
              </ul>
            )}

            <p className="muted">
              Job {d.id}
              <br />
              Model {d.model ?? "–"}, prompt {d.promptVersion ?? "–"}, attempts{" "}
              {d.attempts}
              <br />
              Correlation ID {d.correlationId}
            </p>
          </>
        )}
      </aside>
    </>
  );
}

/* ---------------- App ---------------- */

export default function App() {
  const [metrics, setMetrics] = useState<Metrics | null>(null);
  const [rows, setRows] = useState<ReviewListItem[]>([]);
  const [apiReady, setApiReady] = useState<boolean | null>(null);
  const [status, setStatus] = useState("");
  const [taskIdInput, setTaskIdInput] = useState("");
  const [taskId, setTaskId] = useState(""); // debounced
  const [selected, setSelected] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  // debounce taskId input
  useEffect(() => {
    const t = setTimeout(() => setTaskId(taskIdInput.trim()), SEARCH_DEBOUNCE_MS);
    return () => clearTimeout(t);
  }, [taskIdInput]);

  const refresh = useCallback(async () => {
    try {
      const [m, l, ready] = await Promise.all([
        api.metrics(),
        api.list(status, taskId),
        api.ready().catch(() => false),
      ]);
      setMetrics(m);
      setRows(l.items);
      setApiReady(ready);
      setError(null);
    } catch {
      setApiReady(false);
      setError("Cannot reach the API. Is it running?");
    } finally {
      setLoading(false);
    }
  }, [status, taskId]);

  useEffect(() => {
    refresh();
    const t = setInterval(refresh, POLL_MS);
    return () => clearInterval(t);
  }, [refresh]);

  const hasFilters = status !== "" || taskId !== "";
  const showEmpty = !loading && rows.length === 0;

  const emptyMessage = useMemo(
    () =>
      hasFilters
        ? "No reviews match the current filters."
        : "No reviews yet. Send one with scripts/smoke-test.",
    [hasFilters]
  );

  return (
    <main>
      <header>
        <h1>AI Review Dashboard</h1>
        <p className="muted">
          AI findings are recommendations. An engineer must verify them. Refreshes
          every {POLL_MS / 1000}s.
        </p>
      </header>

      {error && (
        <p className="err" role="alert">
          {error}
        </p>
      )}

      <Overview m={metrics} apiReady={apiReady} />

      <div className="filters">
        <select
          value={status}
          onChange={(e) => setStatus(e.target.value)}
          aria-label="Filter by status"
        >
          <option value="">All statuses</option>
          {["Queued", "Running", "Completed", "Failed"].map((s) => (
            <option key={s} value={s}>
              {s}
            </option>
          ))}
        </select>

        <input
          placeholder="Task ID"
          value={taskIdInput}
          onChange={(e) => setTaskIdInput(e.target.value)}
          aria-label="Filter by task ID"
        />

        {hasFilters && (
          <button
            className="btn-clear"
            onClick={() => {
              setStatus("");
              setTaskIdInput("");
            }}
          >
            Clear
          </button>
        )}
      </div>

      <div className="tablewrap">
        <table>
          <thead>
            <tr>
              <th>Task</th>
              <th>Status</th>
              <th>Outcome</th>
              <th>Findings</th>
              <th>Duration</th>
              <th>Write-back</th>
              <th>Submitted</th>
            </tr>
          </thead>
          <tbody aria-live="polite">
            {loading && rows.length === 0 && (
              <tr>
                <td colSpan={7} className="muted">
                  Loading reviews…
                </td>
              </tr>
            )}
            {showEmpty && (
              <tr>
                <td colSpan={7} className="muted">
                  {emptyMessage}
                </td>
              </tr>
            )}
            {rows.map((r) => (
              <tr
                key={r.id}
                onClick={() => setSelected(r.id)}
                className="click"
                tabIndex={0}
                onKeyDown={(e) =>
                  (e.key === "Enter" || e.key === " ") && setSelected(r.id)
                }
              >
                <td>
                  {r.title || r.taskId}
                  <div className="muted">{r.taskId}</div>
                </td>
                <td>
                  <Badge kind={r.status}>{r.status}</Badge>
                </td>
                <td>{outcomeLabel(r.overallResult)}</td>
                <td>{r.findingCount}</td>
                <td>{fmtSecs(r.durationSeconds)}</td>
                <td>
                  <Badge kind={r.writeBackStatus}>{r.writeBackStatus}</Badge>
                </td>
                <td>{fmtTime(r.createdAtUtc)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {selected && <Detail id={selected} onClose={() => setSelected(null)} />}
    </main>
  );
}