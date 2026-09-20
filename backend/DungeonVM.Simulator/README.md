# DungeonVM.Simulator

`DungeonVM.Core`를 참조하는 .NET 8 콘솔 헤드리스 시뮬레이터. 성향이 다른 가상 봇 3종을 각 1,000회(총 3,000회)
완주시켜 무기 레벨 분포/방어구 등급 채택률/그리드 병목/룬 보존 회피 빈도를 집계하고, "고레벨 무기가 강력함에도
불구하고 왜 유저(봇)는 머지 그리드 병목·골드 해금 비용·소켓 룬 소멸 패널티로 인해 중간 레벨(5~10레벨)에
안주하는가"를 데이터로 검증합니다.

## 실행

`backend/` 안에서 실행합니다.

```bash
dotnet run --project DungeonVM.Simulator/DungeonVM.Simulator.csproj -c Release
```

첫 번째 인자로 봇당 실행 횟수를 바꿀 수 있습니다 (기본값 1000, 즉 총 3000회).

```bash
dotnet run --project DungeonVM.Simulator/DungeonVM.Simulator.csproj -c Release -- 50
```

## 밸런스 오버라이드

모든 밸런스 수치는 `DungeonVM.Core`의 [`BalanceProvider`](../DungeonVM.Core/Balance/BalanceProvider.cs)를 거쳐 나옵니다.
기본값은 임베디드 리소스 `DefaultBalance.json`이지만, `--balance <경로>`(또는 `DUNGEONVM_BALANCE_JSON` 환경변수)로
JSON 파일을 지정하면 그 값으로 덮어써서 실행됩니다. JSON은 전체 섹션을 다 채우지 않아도 되며, 포함된 섹션만
기본값 위에 덮어씌워집니다(예: `vendingMachine` 섹션만 담은 파일도 유효).

```bash
dotnet run --project DungeonVM.Simulator/DungeonVM.Simulator.csproj -c Release -- 500 --balance my_balance.json
```

추가 옵션:

- `--summary <경로>`: `summary.json`을 빌드 출력 폴더 대신 지정한 경로에 저장 (외부 도구가 빌드 설정/TFM에
  따라 달라지는 출력 경로를 추측하지 않아도 되게 함)
- `--progress <경로>`: 실행 도중(10런마다) 진행 상황을 덮어쓰는 JSON 경로. 완료한 런 수/누적 전투·승률/현재
  처리 중인 봇 등을 담으며, 웹 UI가 이 파일을 폴링해서 실시간 진행률을 보여줄 때 사용
- `--skip-llm`: LLM 밸런싱 모듈(네트워크 호출) 실행을 건너뜀 — 반복 실행 시 API 비용/지연을 피하고 싶을 때

기획자가 엑셀로 밸런스를 조정해 이 JSON을 만드는 파이프라인은 [`tools/balance_pipeline`](../tools/balance_pipeline)를 참고하세요
(`convert ... --simulate 500`으로 변환과 재시뮬레이션을 한 번에 실행할 수 있습니다). 슬라이더로 값을 조정하면서
그래프가 바로 갱신되는 라이브 대시보드는 [`tools/balance_dashboard`](../tools/balance_dashboard)를, 진행 상황과
스테이지별 결과를 실시간으로 보는 웹 러너는 [`tools/balance_web`](../tools/balance_web)을 참고하세요.

## 봇 3종 ([`Bots/`](Bots))

| 봇 | 전략 | 가설 |
|---|---|---|
| [`SpaceExpansionBot`](Bots/SpaceExpansionBot.cs) | 골드를 그리드 해금에 최우선 투자, 룬 소멸을 개의치 않고 무조건 머지해 단일 무기를 최대한 높은 레벨로 밀어붙임("세로 성장") | 그리드는 넉넉해지지만 캐릭터 슬롯/방어구 투자가 밀려 파티 규모가 작게 유지됨 |
| [`VendingRushBot`](Bots/VendingRushBot.cs) | 그리드 해금은 최소화, 자판기 업그레이드와 캐릭터 슬롯(최대 5명) 해금에 골드 집중("가로 확장") | 사람은 늘지만 그리드가 좁아 개개인의 무기는 낮은 레벨에 머묾, 그리드 병목 지표가 높게 나타남 |
| [`MidTierCampBot`](Bots/MidTierCampBot.cs) | 룬이 소켓된 무기가 5~7레벨 구간이면 머지를 의도적으로 건너뛰어 룬을 보존, 방어구/룬 구매에 집중 | 룬 소멸 페널티 때문에 스스로 성장을 멈추는 "안주" 행동을 룬 회피 지표로 직접 관측 |

새 봇을 추가하려면 [`IBot`](Bots/IBot.cs)을 구현하고 [`Program.cs`](Program.cs)의 `bots` 배열에 등록하면 됩니다.
[`BotContext`](Bots/BotContext.cs)가 뽑기/머지(룬 보존 조건부 스킵 포함)/업그레이드/캐릭터 슬롯 해금/룬 구매·소켓/
무기·방어구 장착 등 봇이 쓸 수 있는 행동을 제공합니다.

## 출력물

1. **콘솔 리포트**: 봇별 승률/평균 도달 스테이지/종료 사유/그리드 병목 강제판매/룬 보존 회피 횟수, 무기 레벨 구간
   분포(1-4/5-9/10-14/15)·방어구 등급별 채택률, LLM 밸런싱 모듈 감지 결과
2. **`bin/<Config>/net8.0/run_logs.jsonl`**: 런 1건당 1줄 JSON(`RunResult`, `GridBottleneckSells`·`RuneAvoidanceSkips` 포함) — 항상 기록됨
3. **`bin/<Config>/net8.0/summary.json`**: 콘솔 리포트와 동일한 집계(승률/평균 스테이지/레벨 구간 분포/채택률)에 더해
   봇별 `stageBreakdown`(스테이지 1~30 각각의 전투/승리 수, 판정 승률, 평균 클리어 시간, 평균 잔여 HP 비율)을 담은
   요약 JSON — 대시보드/웹 러너 등 외부 도구가 콘솔 출력을 파싱하지 않고 이 파일만 읽으면 되도록 함
4. **`--progress`로 지정한 경로**: 실행 도중 10런마다 갱신되는 진행 상황 JSON(옵션을 준 경우에만 생성)
5. **`bin/<Config>/net8.0/balance_suggestions.json`**: LLM이 제안한 밸런스 조정안(API 키가 설정된 경우에만 생성)

## Supabase 로깅 (선택)

기본은 로컬 JSONL 로그만 남기고, 아래 두 환경 변수가 없으면 Supabase 호출을 조용히 건너뜁니다
([`Metrics/SupabaseLogger.cs`](Metrics/SupabaseLogger.cs)). 실제 인프라에 적재하려면:

```bash
export SUPABASE_URL="https://xxxx.supabase.co"
export SUPABASE_SERVICE_KEY="..."
```

```sql
create table run_logs (
  id bigint generated always as identity primary key,
  bot_name text,
  run_index int,
  outcome text,
  stages_cleared int,
  final_gold int,
  final_souls int,
  final_weapons text[],
  final_armors text[],
  grid_bottleneck_sells int,
  rune_avoidance_skips int,
  savings_holds int,
  created_at timestamptz default now()
);
```

## 시뮬레이션 파라미터

`Program.cs` 상단의 `TickSeconds`(틱 간격), `MaxSecondsPerStage`(스테이지 제한시간), `StartingGold`(시작 골드)로
전체 시뮬레이션 속도/난이도를 조정할 수 있습니다.
