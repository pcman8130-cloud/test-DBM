# DungeonVM.Simulator

`DungeonVM.Core`를 참조하는 .NET 8 콘솔 헤드리스 시뮬레이터. 성향이 다른 가상 봇 3종을 각 1,000회(총 3,000회)
완주시켜 무기 티어/방어구 등급 채택률을 집계하고, "왜 유저가 고티어 대신 2티어 무기에 안주하는가"를 데이터로 검증합니다.

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

기획자가 엑셀로 밸런스를 조정해 이 JSON을 만드는 파이프라인은 [`tools/balance_pipeline`](../tools/balance_pipeline)를 참고하세요
(`convert ... --simulate 500`으로 변환과 재시뮬레이션을 한 번에 실행할 수 있습니다).

## 봇 3종 ([`Bots/`](Bots))

| 봇 | 전략 | 가설 |
|---|---|---|
| [`GreedyMergerBot`](Bots/GreedyMergerBot.cs) | 골드가 생기는 즉시 뽑기부터 소진, 업그레이드/해금 거의 안 함, 무기 종류 안 가리고 최고 티어로 계속 교체 | 골드가 항상 저티어 재뽑기에 흡수되어 동일 무기 스택이 안 쌓이고 2티어 근처에서 정체 |
| [`SaverUpgraderBot`](Bots/SaverUpgraderBot.cs) | 자판기 업그레이드/그리드 해금을 최우선 저축, 무기 종류를 한 번 정하면 고수(타입 전환 없음) | 뽑기 확률 자체를 끌어올린 상태로 동종 스택을 안정적으로 쌓아 고티어 도달 빈도가 높음 |
| [`BalancedOptimizerBot`](Bots/BalancedOptimizerBot.cs) | 초반엔 업그레이드 우선, 중후반엔 그리드 해금+뽑기 병행 | "이상적 유저" 벤치마크 |

새 봇을 추가하려면 [`IBot`](Bots/IBot.cs)을 구현하고 [`Program.cs`](Program.cs)의 `bots` 배열에 등록하면 됩니다.
[`BotContext`](Bots/BotContext.cs)가 뽑기/머지/업그레이드/장착 등 봇이 쓸 수 있는 행동을 제공합니다.

## 출력물

1. **콘솔 리포트**: 봇별 승률/평균 도달 스테이지/종료 사유, 무기 티어별·방어구 등급별 채택률, LLM 밸런싱 모듈 감지 결과
2. **`bin/<Config>/net8.0/run_logs.jsonl`**: 런 1건당 1줄 JSON(`RunResult`) — 항상 기록됨
3. **`bin/<Config>/net8.0/summary.json`**: 콘솔 리포트와 동일한 집계(승률/평균 스테이지/채택률)를 담은 요약 JSON —
   대시보드 등 외부 도구가 콘솔 출력을 파싱하지 않고 이 파일만 읽으면 되도록 함
4. **`bin/<Config>/net8.0/balance_suggestions.json`**: LLM이 제안한 밸런스 조정안(API 키가 설정된 경우에만 생성)

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
  final_gems int,
  final_souls int,
  final_weapons text[],
  final_armors text[],
  created_at timestamptz default now()
);
```

## 시뮬레이션 파라미터

`Program.cs` 상단의 `TickSeconds`(틱 간격), `MaxSecondsPerStage`(스테이지 제한시간), `StartingGold`(시작 골드)로
전체 시뮬레이션 속도/난이도를 조정할 수 있습니다.
