const BOT_NAMES = ["SpaceExpansion", "VendingRush", "MidTierCamp"];
const BOT_COLORS = { SpaceExpansion: "#e8590c", VendingRush: "#2f9e44", MidTierCamp: "#1971c2" };

const state = {
  runId: null,
  pollTimer: null,
  activeBot: "SpaceExpansion",
  lastSummary: null,
  compareSummary: null,
  chart: null,
};

const el = (id) => document.getElementById(id);

function fmtPct(x) { return `${(x * 100).toFixed(1)}%`; }
function fmtSec(x) { return `${x.toFixed(1)}초`; }
function fmtInt(x) { return x.toLocaleString("ko-KR"); }

// ---------------------------------------------------------------------------
// 실행 시작 / 중단
// ---------------------------------------------------------------------------

el("runsPerBot").addEventListener("input", () => {
  const n = parseInt(el("runsPerBot").value || "0", 10);
  el("totalRunsPreview").textContent = fmtInt(n * 3);
});

el("runBtn").addEventListener("click", async () => {
  const runsPerBot = parseInt(el("runsPerBot").value || "500", 10);

  el("runBtn").disabled = true;
  el("stopBtn").disabled = false;
  setBadge("running", "실행 중");
  el("runLabel").textContent = `봇 3종 x ${fmtInt(runsPerBot)}회 준비 중...`;
  el("exportCsv").disabled = true;
  el("exportJson").disabled = true;

  const res = await fetch("/api/run", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ runsPerBot }),
  });
  const data = await res.json();
  state.runId = data.runId;
  el("runId").textContent = `RUN ${data.runId}`;

  startPolling();
});

el("stopBtn").addEventListener("click", async () => {
  if (!state.runId) return;
  await fetch(`/api/stop/${state.runId}`, { method: "POST" });
  stopPolling();
  setBadge("idle", "중단됨");
  el("runBtn").disabled = false;
  el("stopBtn").disabled = true;
});

el("refreshBtn").addEventListener("click", () => {
  if (state.runId) pollOnce();
  loadHistory();
});

// ---------------------------------------------------------------------------
// 폴링
// ---------------------------------------------------------------------------

function startPolling() {
  stopPolling();
  state.pollTimer = setInterval(pollOnce, 500);
  pollOnce();
}

function stopPolling() {
  if (state.pollTimer) clearInterval(state.pollTimer);
  state.pollTimer = null;
}

async function pollOnce() {
  if (!state.runId) return;
  const res = await fetch(`/api/progress/${state.runId}`);
  const p = await res.json();
  renderProgress(p);

  if (p.status === "done") {
    stopPolling();
    el("runBtn").disabled = false;
    el("stopBtn").disabled = true;
    setBadge("done", "완료");
    await loadResult();
    loadHistory();
  } else if (p.status === "error") {
    stopPolling();
    el("runBtn").disabled = false;
    el("stopBtn").disabled = true;
    setBadge("error", "오류");
    el("runLabel").textContent = "시뮬레이터 실행 중 오류가 발생했습니다. 콘솔 로그를 확인하세요.";
    console.error(p.error);
  }
}

function setBadge(kind, text) {
  const badge = el("statusBadge");
  badge.className = `badge badge-${kind}`;
  badge.textContent = text;
}

function renderProgress(p) {
  if (p.status === "starting") {
    el("runLabel").textContent = "시뮬레이터를 시작하는 중...";
    return;
  }

  const percent = Math.round((p.percent || 0) * 100);
  el("progressBar").style.width = `${percent}%`;
  el("progressPercent").textContent = `${percent}%`;
  el("runLabel").textContent = `${p.currentBotName || "-"} 진행 중 · ${fmtInt(p.completedRuns || 0)} / ${fmtInt(p.totalRunsPlanned || 0)}런`;

  el("statBattles").textContent = fmtInt(p.battlesFoughtTotal || 0);
  el("statWinRate").textContent = fmtPct(p.overallWinRateSoFar || 0);
  el("statWinRateSub").textContent = `${fmtInt(p.battlesWonTotal || 0)}승 / ${fmtInt(p.battlesFoughtTotal || 0)}전투`;
  el("statAvgClear").textContent = fmtSec(p.overallAvgClearSecondsSoFar || 0);
  el("statElapsed").textContent = fmtSec(p.elapsedSeconds || 0);
}

// ---------------------------------------------------------------------------
// 결과(summary.json) 로딩 및 표/그래프 렌더링
// ---------------------------------------------------------------------------

async function loadResult() {
  if (!state.runId) return;
  const res = await fetch(`/api/result/${state.runId}`);
  if (res.status !== 200) return;
  const data = await res.json();
  if (!data.ready) return;

  state.lastSummary = data.summary;
  el("exportCsv").disabled = false;
  el("exportJson").disabled = false;
  renderStageTable();
  renderChart();
}

function findBotSummary(summary, botName) {
  if (!summary) return null;
  return (summary.bots || []).find((b) => b.botName === botName) || null;
}

function renderStageTable() {
  const bot = findBotSummary(state.lastSummary, state.activeBot);
  const tbody = el("stageTableBody");
  tbody.innerHTML = "";

  if (!bot || !bot.stageBreakdown || bot.stageBreakdown.length === 0) {
    tbody.innerHTML = `<tr><td colspan="5" class="empty">이 봇은 아직 도달한 스테이지가 없습니다.</td></tr>`;
    return;
  }

  for (const row of bot.stageBreakdown) {
    const tr = document.createElement("tr");
    const winRatePct = row.winRate * 100;
    const barClass = winRatePct >= 80 ? "" : winRatePct >= 40 ? "mid" : "low";

    tr.innerHTML = `
      <td>ST ${row.stage}</td>
      <td>${fmtInt(row.wins)} / ${fmtInt(row.attempts)}</td>
      <td>
        <div class="winrate-cell">
          <span>${fmtPct(row.winRate)}</span>
          <div class="winrate-bar-track"><div class="winrate-bar-fill ${barClass}" style="width:${winRatePct}%"></div></div>
        </div>
      </td>
      <td>${row.avgClearSeconds ? fmtSec(row.avgClearSeconds) : "—"}</td>
      <td>${fmtPct(row.avgRemainingHpRatio)}</td>
    `;
    tbody.appendChild(tr);
  }
}

function renderChart() {
  const ctx = el("stageChart").getContext("2d");
  const datasets = BOT_NAMES.map((name) => {
    const bot = findBotSummary(state.lastSummary, name);
    const points = (bot?.stageBreakdown || []).map((r) => ({ x: r.stage, y: r.winRate * 100 }));
    return {
      label: name,
      data: points,
      borderColor: BOT_COLORS[name],
      backgroundColor: BOT_COLORS[name],
      borderWidth: state.activeBot === name ? 3 : 1.5,
      pointRadius: 2,
      tension: 0.25,
    };
  });

  if (state.compareSummary) {
    const bot = findBotSummary(state.compareSummary, state.activeBot);
    if (bot) {
      datasets.push({
        label: `${state.activeBot} (비교 데이터)`,
        data: (bot.stageBreakdown || []).map((r) => ({ x: r.stage, y: r.winRate * 100 })),
        borderColor: "#adb5bd",
        borderDash: [6, 4],
        pointRadius: 0,
        borderWidth: 2,
      });
    }
  }

  if (state.chart) state.chart.destroy();
  state.chart = new Chart(ctx, {
    type: "line",
    data: { datasets },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      scales: {
        x: { type: "linear", min: 1, max: 30, title: { display: true, text: "스테이지" } },
        y: { min: 0, max: 100, title: { display: true, text: "승률(%)" } },
      },
      plugins: { legend: { position: "bottom" } },
    },
  });
}

document.getElementById("botTabs").addEventListener("click", (e) => {
  const btn = e.target.closest(".bot-tab");
  if (!btn) return;
  document.querySelectorAll(".bot-tab").forEach((b) => b.classList.remove("active"));
  btn.classList.add("active");
  state.activeBot = btn.dataset.bot;
  renderStageTable();
  if (state.lastSummary) renderChart();
});

// ---------------------------------------------------------------------------
// CSV / JSON 내보내기 (현재 선택된 봇의 스테이지 결과)
// ---------------------------------------------------------------------------

function download(filename, content, mime) {
  const blob = new Blob([content], { type: mime });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}

el("exportCsv").addEventListener("click", () => {
  const bot = findBotSummary(state.lastSummary, state.activeBot);
  if (!bot) return;
  const header = "stage,attempts,wins,winRate,avgClearSeconds,avgRemainingHpRatio";
  const rows = bot.stageBreakdown.map((r) =>
    [r.stage, r.attempts, r.wins, r.winRate, r.avgClearSeconds, r.avgRemainingHpRatio].join(",")
  );
  download(`${state.activeBot}_stage_results.csv`, [header, ...rows].join("\n"), "text/csv");
});

el("exportJson").addEventListener("click", () => {
  download(`${state.runId || "result"}_summary.json`, JSON.stringify(state.lastSummary, null, 2), "application/json");
});

// ---------------------------------------------------------------------------
// 실제 플레이 데이터 비교(선택) — 같은 스키마의 summary.json 업로드
// ---------------------------------------------------------------------------

el("compareUpload").addEventListener("change", async (e) => {
  const file = e.target.files[0];
  if (!file) return;
  try {
    state.compareSummary = JSON.parse(await file.text());
    if (state.lastSummary) renderChart();
  } catch (err) {
    alert("JSON 파싱에 실패했습니다: " + err.message);
  }
});

// ---------------------------------------------------------------------------
// 최근 실행 기록
// ---------------------------------------------------------------------------

async function loadHistory() {
  const res = await fetch("/api/runs");
  const rows = await res.json();
  const list = el("runHistory");
  list.innerHTML = "";

  if (rows.length === 0) {
    list.innerHTML = `<li class="history-empty">아직 실행 기록이 없습니다.</li>`;
    return;
  }

  for (const row of rows) {
    const li = document.createElement("li");
    const statusClass = row.status === "done" ? "h-done" : row.status === "error" ? "h-error" : "h-running";
    const statusText = row.status === "done" ? "완료" : row.status === "error" ? "오류" : `${Math.round((row.percent || 0) * 100)}%`;
    li.innerHTML = `<span>${row.runId} · ${row.runsPerBot}회</span><span class="h-status ${statusClass}">${statusText}</span>`;
    li.addEventListener("click", () => {
      state.runId = row.runId;
      el("runId").textContent = `RUN ${row.runId}`;
      pollOnce().then(() => {
        if (row.status === "done") loadResult();
      });
    });
    list.appendChild(li);
  }
}

loadHistory();
