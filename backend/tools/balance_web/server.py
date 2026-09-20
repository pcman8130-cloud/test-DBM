"""
DungeonVM 밸런스 라이브 러너 — 배치 시뮬레이션을 실행하고 진행 상황을 실시간으로 보여주는 로컬 웹 서버.

Streamlit 대시보드(balance_dashboard)는 "값 조정 -> 버튼 -> 끝날 때까지 대기 -> 결과"인 반면,
이 서버는 시뮬레이터가 도는 동안 진행률/누적 승률/스테이지별 결과를 실시간으로 폴링해서 보여준다.
(참고 UI: 메이플 랩 배치 플레이테스트 화면 — 좌측 실행 설정 / 우측 실시간 현황 + 스테이지 결과 표)

실행:
    pip install -r requirements.txt
    python server.py
    -> http://127.0.0.1:8788
"""

from __future__ import annotations

import json
import shutil
import subprocess
import sys
import time
import uuid
from pathlib import Path

if sys.platform == "win32":
    # Windows 콘솔 코드페이지(cp949 등)와 무관하게 한글 출력이 항상 UTF-8로 나가도록 강제한다.
    for _stream in (sys.stdout, sys.stderr):
        try:
            _stream.reconfigure(encoding="utf-8")
        except (AttributeError, ValueError):
            pass

from flask import Flask, jsonify, request, send_from_directory

BACKEND_ROOT = Path(__file__).resolve().parents[2]
SIMULATOR_PROJECT = BACKEND_ROOT / "DungeonVM.Simulator" / "DungeonVM.Simulator.csproj"
DEFAULT_BALANCE_JSON = BACKEND_ROOT / "DungeonVM.Core" / "Balance" / "DefaultBalance.json"

RUNS_DIR = Path(__file__).resolve().parent / ".runs"
RUNS_DIR.mkdir(exist_ok=True)

MAX_STAGE = 30  # StageLoop.MaxStage 기본값(대시보드에서 바꿨다면 progress/summary가 실제 값을 담고 있음)

app = Flask(__name__, static_folder="static", static_url_path="")

# run_id -> {"process": Popen, "started_at": float, "runs_per_bot": int}
_RUNS: dict[str, dict] = {}


def _run_dir(run_id: str) -> Path:
    d = RUNS_DIR / run_id
    d.mkdir(parents=True, exist_ok=True)
    return d


@app.get("/")
def index():
    return send_from_directory(app.static_folder, "index.html")


@app.post("/api/run")
def start_run():
    body = request.get_json(silent=True) or {}
    runs_per_bot = int(body.get("runsPerBot", 500))
    runs_per_bot = max(10, min(runs_per_bot, 5000))

    run_id = time.strftime("%Y%m%d-%H%M%S-") + uuid.uuid4().hex[:6]
    run_dir = _run_dir(run_id)
    progress_path = run_dir / "progress.json"
    summary_path = run_dir / "summary.json"

    cmd = [
        "dotnet", "run", "--project", str(SIMULATOR_PROJECT), "-c", "Release", "--",
        str(runs_per_bot),
        "--progress", str(progress_path),
        "--summary", str(summary_path),
        "--skip-llm",
    ]
    log_path = run_dir / "console.log"
    log_file = open(log_path, "w", encoding="utf-8")
    proc = subprocess.Popen(cmd, cwd=BACKEND_ROOT, stdout=log_file, stderr=subprocess.STDOUT, text=True)

    (run_dir / "meta.json").write_text(json.dumps({
        "runId": run_id,
        "runsPerBot": runs_per_bot,
        "startedAt": time.time(),
    }), encoding="utf-8")

    _RUNS[run_id] = {"process": proc, "log_file": log_file, "started_at": time.time(), "runs_per_bot": runs_per_bot}
    return jsonify({"runId": run_id, "runsPerBot": runs_per_bot})


@app.get("/api/progress/<run_id>")
def get_progress(run_id: str):
    run_dir = RUNS_DIR / run_id
    progress_path = run_dir / "progress.json"
    entry = _RUNS.get(run_id)

    if not progress_path.exists():
        return jsonify({"status": "starting"})

    try:
        data = json.loads(progress_path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError):
        # 시뮬레이터가 파일을 쓰는 도중 읽었을 수 있음 — 다음 폴링에 다시 시도
        return jsonify({"status": "starting"})

    if entry is not None and entry["process"].poll() is not None and data.get("status") != "done":
        data["status"] = "error"
        data["error"] = _tail_log(run_dir, 40)

    return jsonify(data)


@app.get("/api/result/<run_id>")
def get_result(run_id: str):
    summary_path = RUNS_DIR / run_id / "summary.json"
    if not summary_path.exists():
        return jsonify({"ready": False}), 202

    try:
        data = json.loads(summary_path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError):
        return jsonify({"ready": False}), 202

    return jsonify({"ready": True, "summary": data})


@app.post("/api/stop/<run_id>")
def stop_run(run_id: str):
    entry = _RUNS.get(run_id)
    if entry is None:
        return jsonify({"stopped": False, "reason": "unknown run"}), 404

    proc: subprocess.Popen = entry["process"]
    if proc.poll() is None:
        proc.terminate()
    return jsonify({"stopped": True})


@app.get("/api/runs")
def list_runs():
    """최근 실행 기록(왼쪽 사이드바 표시용). 각 런의 meta.json + 마지막 progress 상태를 함께 반환."""
    rows = []
    for d in sorted(RUNS_DIR.iterdir(), key=lambda p: p.stat().st_mtime, reverse=True):
        if not d.is_dir():
            continue
        meta_path = d / "meta.json"
        if not meta_path.exists():
            continue
        try:
            meta = json.loads(meta_path.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, OSError):
            continue

        progress_path = d / "progress.json"
        status = "running"
        percent = 0.0
        if progress_path.exists():
            try:
                p = json.loads(progress_path.read_text(encoding="utf-8"))
                status = p.get("status", "running")
                percent = p.get("percent", 0.0)
            except (json.JSONDecodeError, OSError):
                pass

        rows.append({**meta, "status": status, "percent": percent})
        if len(rows) >= 20:
            break

    return jsonify(rows)


def _tail_log(run_dir: Path, lines: int) -> str:
    log_path = run_dir / "console.log"
    if not log_path.exists():
        return ""
    try:
        content = log_path.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return ""
    return "\n".join(content.splitlines()[-lines:])


@app.get("/api/default-balance")
def get_default_balance():
    return jsonify(json.loads(DEFAULT_BALANCE_JSON.read_text(encoding="utf-8")))


if __name__ == "__main__":
    print("DungeonVM 밸런스 라이브 러너 - http://127.0.0.1:8788")
    app.run(host="127.0.0.1", port=8788, debug=False, threaded=True)
