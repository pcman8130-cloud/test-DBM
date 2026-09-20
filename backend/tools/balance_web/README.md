# balance_web

배치 시뮬레이션을 돌리는 동안 진행률·승률·스테이지별 결과를 실시간으로 보여주는 로컬 웹 러너입니다.
`balance_dashboard`(Streamlit)가 "값 조정 → 버튼 → 끝날 때까지 대기 → 결과"라면, 이 도구는
시뮬레이터가 도는 도중에도 진행 상황을 계속 폴링해서 보여줍니다 — "배치 플레이테스트" 같은
QA 화면에 가깝습니다.

## 설치 및 실행

```bash
pip install -r requirements.txt
python server.py
```

브라우저에서 `http://127.0.0.1:8788` 접속. (`.claude/launch.json`에 `balance-web` 설정이 있어
Claude Code 프리뷰로도 바로 띄울 수 있습니다.)

## 화면 구성

1. **실행 설정** — 봇당 실행 횟수만 정하면 됩니다(3개 봇 SpaceExpansion/VendingRush/MidTierCamp를
   항상 동시에 비교 실행). 밸런스 수치 자체를 바꾸고 싶다면 `balance_dashboard`에서 슬라이더로
   조정한 뒤 "DefaultBalance.json에 반영"을 먼저 눌러주세요 — 이 화면은 그 파일을 그대로 읽습니다.
2. **실행 현황** — 시뮬레이터가 10런마다 진행 상황을 파일에 기록하고, 이 화면이 0.5초마다 읽어와서
   진행률/누적 승률/평균 클리어 시간을 실시간으로 갱신합니다.
3. **스테이지 결과** — 봇별로 스테이지 1~30의 전투/승리 수, 판정 승률, 평균 클리어 시간, 평균 잔여
   HP를 표와 꺾은선 그래프로 보여줍니다. 승률이 급격히 떨어지는 지점(대체로 대형 보스 스테이지)이
   "정체 구간" 후보입니다. CSV/JSON으로 내려받을 수 있습니다.
4. **실제 플레이 데이터 비교** — 아직 자리만 있습니다. Unity 클라이언트가 개발되어 실제 플레이
   로그(Supabase 등)를 쌓기 시작하면, 그 데이터를 `summary.json`과 같은 스키마로 변환해 업로드하면
   시뮬레이션 결과 위에 점선으로 겹쳐 그립니다. 지금은 Unity 쪽에 스크립트가 하나도 없어서
   연동하지 않았습니다 — 나중에 실측 데이터가 생기면 이 자리에 끼워 넣으면 됩니다.

## 동작 방식

- `POST /api/run`이 `dotnet run --project ../../DungeonVM.Simulator -c Release -- <runs> --progress ... --summary ... --skip-llm`을
  백그라운드 프로세스로 띄웁니다.
- `DungeonVM.Simulator`는 10런마다 `--progress` 경로에 진행 상황 JSON을 덮어씁니다
  (완료한 런 수, 누적 전투/승률, 현재 처리 중인 봇 등).
- 프론트엔드가 `GET /api/progress/<runId>`를 0.5초 간격으로 폴링하다가 `status: "done"`이 되면
  `GET /api/result/<runId>`로 최종 `summary.json`(스테이지별 결과 포함)을 받아 표/그래프를 그립니다.
- 각 실행의 파일은 `.runs/<runId>/`에 남습니다(진행상황/요약/콘솔 로그). git에는 커밋되지 않습니다.

## 알아두면 좋은 점

- 첫 실행은 `dotnet run`이 빌드까지 하므로 몇 초 더 걸립니다. 미리
  `dotnet build backend/DungeonVM.sln -c Release`를 해두면 빨라집니다.
- 여러 명이 동시에 이 서버를 각자 로컬에서 띄워도 서로 영향 없습니다(포트/파일이 로컬 전용).
- 시뮬레이터가 비정상 종료하면 진행 상황 화면에 "오류" 배지가 뜨고, 콘솔 로그(`.runs/<runId>/console.log`)
  마지막 줄들을 브라우저 콘솔에 출력합니다.
