# balance_dashboard

밸런스 파라미터를 슬라이더/표로 조정하고 그 자리에서 재시뮬레이션해 승률·티어 채택률 그래프를 보는
Streamlit 라이브 대시보드입니다.

기존에 만들어둔 [정적 결과 뷰어](https://claude.ai/code/artifact/09feef65-e3ae-471a-bac3-43efc7d60797)는
`run_logs.jsonl`을 업로드해서 사후 분석하는 용도이고, 이 대시보드는 그와 달리 **값을 바꾸면 그 자리에서
`DungeonVM.Simulator`를 재실행**해 결과를 보여줍니다.

## 설치 및 실행

```bash
pip install -r requirements.txt
streamlit run app.py
```

브라우저가 자동으로 열리지 않으면 콘솔에 출력된 `http://localhost:8501`로 접속하세요.

## 사용법

1. 탭(무기/방어구/자판기/웨이브/...)을 오가며 슬라이더나 표를 원하는 값으로 조정합니다.
   - 무기 6종 기초 스탯, 방어구 등급별 랜덤 스탯 범위/시세, 업그레이드·해금 비용 리스트는
     표(더블클릭해서 셀 편집)로 되어 있습니다.
   - 그 외 수치는 슬라이더입니다. 범위는 필드 이름/현재값 기준 휴리스틱으로 자동 추정한 것이라
     너무 좁거나 넓다면 `app.py`의 `slider_range()` 함수만 고치면 됩니다.
2. 사이드바에서 "봇당 실행 횟수"를 고르고(빠른 탐색은 100~300, 신뢰도 높은 확인은 1000~2000),
   **시뮬레이션 실행** 버튼을 누릅니다. (슬라이더를 드래그하는 즉시 실행되지는 않습니다 — 매번 자동
   재시뮬레이션하면 값 하나 옮길 때마다 프로세스가 계속 뜨므로, 원하는 값들을 다 맞춘 뒤 버튼으로
   확정 실행하는 방식입니다.)
3. 아래에 봇별 승률/평균 도달 스테이지, 무기 티어 채택률, 방어구 등급 채택률 그래프가 갱신됩니다.
4. 마음에 드는 조합을 찾았다면:
   - **현재 값 JSON 다운로드**로 내려받아 팀에 공유하거나 `--balance` 옵션으로 시뮬레이터에 재사용
   - **DefaultBalance.json에 반영**(체크박스 확인 후)으로 저장소의 기본값 자체를 갱신 —
     반영 후 `git diff`로 실제 뭐가 바뀌었는지 꼭 확인하고 커밋하세요.

## 동작 방식

- 스키마는 [`tools/balance_pipeline/balance_schema.py`](../balance_pipeline/balance_schema.py)를
  그대로 import해서 씁니다. `BalanceData.cs`에 필드가 추가되면 그쪽 `SCALAR_FIELDS`만 갱신하면
  이 대시보드의 슬라이더도 자동으로 늘어납니다(표 형태 항목은 `app.py`에 직접 추가해야 함).
- "시뮬레이션 실행"을 누르면:
  1. 현재 폼 값을 `.scratch/current_balance.json`에 씀
  2. `dotnet run --project ../../DungeonVM.Simulator -c Release -- <runs> --balance .scratch/current_balance.json --summary .scratch/last_summary.json --skip-llm` 실행
     (`--skip-llm`: 반복 실행마다 LLM API를 호출하지 않도록 밸런싱 모듈은 건너뜀)
  3. `.scratch/last_summary.json`을 읽어 그래프로 표시
- `.scratch/`는 이 폴더 안의 임시 작업 파일이라 git에 커밋되지 않습니다(`.gitignore` 참고).

## 알아두면 좋은 점

- 첫 실행은 `dotnet run`이 빌드까지 하므로 몇 초 더 걸립니다. 미리
  `dotnet build backend/DungeonVM.sln -c Release`를 한 번 해두면 이후 실행이 빨라집니다.
- 슬라이더는 Streamlit의 커스텀 위젯이라 마우스 드래그/클릭으로만 정확히 값이 바뀝니다
  (자동화 스크립트로 값만 강제로 주입하면 화면 표시와 실제 제출값이 어긋날 수 있음).
