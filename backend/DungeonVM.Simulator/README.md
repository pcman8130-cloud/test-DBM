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

## 봇 8종 ([`Bots/`](Bots))

| 봇 | 전략 | 가설 |
|---|---|---|
| [`SpaceExpansionBot`](Bots/SpaceExpansionBot.cs) | 골드를 그리드 해금에 최우선 투자, 룬 소멸을 개의치 않고 무조건 머지해 단일 무기를 최대한 높은 레벨로 밀어붙임("세로 성장") | 그리드는 넉넉해지지만 캐릭터 슬롯/방어구 투자가 밀려 파티 규모가 작게 유지됨 |
| [`VendingRushBot`](Bots/VendingRushBot.cs) | 그리드 해금은 최소화, 자판기 업그레이드와 캐릭터 슬롯(최대 5명) 해금에 골드 집중("가로 확장") | 사람은 늘지만 그리드가 좁아 개개인의 무기는 낮은 레벨에 머묾, 그리드 병목 지표가 높게 나타남 |
| [`MidTierCampBot`](Bots/MidTierCampBot.cs) | 항상 특수 상자(룬/유물)를 고르고 룬을 즉시 소켓, 방어구 세팅에 집중(룬이 머지 후에도 귀속되도록 바뀌어 머지 회피는 더 이상 하지 않음) | 룬 수집 위주 플레이의 성과 관측(룬 회피 지표는 항상 0) |
| [`BalancedBot`](Bots/BalancedBot.cs) (`Balanced`) | 인원·자판기·그리드·방어구에 골고루 투자하는 기본 운영. 밸런스 평가기(`--evaluate`)가 기준으로 쓰는 봇. 상자는 인원 4명이 차기 전엔 장비 상자, 그 뒤로는 능력치 상자 | 사람의 평균적인 플레이 |
| `BalancedBot(Hoarder)` | 같은 운영에 저축 창을 6스테이지로 넓혀 목표 금액을 오래 모음. 상자는 항상 장비 상자 | 골드를 모아서 한꺼번에 쓰는 플레이 |
| `BalancedBot(Spender)` | 길게 모으지 않고(1스테이지) 번 돈을 바로 재뽑기/강화에 씀. 상자는 능력치 상자, 보스 직전 스테이지에서만 특수 상자 | 장비를 바로바로 맞추는 플레이 |
| `BalancedBot(BossPrep)` | 기본 운영에 보스 직전 스테이지에서만 특수 상자(룬/유물)로 갈아탐 | 보스 대비용으로 룬/유물을 노리는 플레이 |
| `BalancedBot(Expert)` | 개발자 본인 플레이를 모사한 숙련자 루트. 시작 골드로 무기 3개·방어구 2개 → 인원 3명 → 자판기 Lv.3 → 그리드 확장 → 무기 4레벨 → 인원 4명 → 전원 무기 5레벨 → 자판기 Lv.4 → 인원 5명 순으로 목표 금액을 다 모을 때까지 재뽑기 없이 저축(`PlanGoal`). 상자는 능력치 상자만. 지팡이는 뽑는 즉시 판매, 자판기 Lv.4 이후엔 짝 없는 Lv.2 이하 무기 판매, 그리드가 가득 차면 낮은 무기를 팔아 칸을 만들고 골드가 남으면 그리드 확장 | "숙련자는 29스테이지까지 깨고 30스테이지가 어렵다"는 경험을 시뮬레이션으로 재현(현재 30스테이지 완주 약 67%) |

균형형 변형 봇은 [`BalancedBotProfile`](Bots/RewardPolicies.cs)(저축 창 + 상자 선택 정책)만 바꿔 끼운 것이라, 성향이 다른 봇을
몇 줄로 추가할 수 있습니다. 상자 선택 정책은 `RewardPolicies`에 모여 있습니다.

**뽑기 규칙**: 프로토타입과 동일하게 그리드(무기+방어구 공유)에 빈 칸이 없으면 무기/방어구 뽑기가 막히고 골드는 나가지 않습니다
(`BotContext.TryRollWeapon/TryRollArmor`, 막힌 횟수는 `GridBottleneckSells`로 집계). 예전엔 골드를 쓰고 뽑은 뒤 환급 없이 버렸습니다.

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

## 밸런스 평가기 (`--evaluate`)

[`BalanceTargets.json`](BalanceTargets.json)에 정의한 목표 지표(스테이지별 승률 범위, 30스테 완주율, 보스 유물 선택지 간
격차, 무기 종류 채택률, 무기 성장 등)를 균형형 봇으로 측정해 **목표에서 벗어난 지표**를 뽑아내는 모드입니다.
LLM 밸런싱 단계의 입력(`balance_eval.json`)이 됩니다. 모든 난수를 고정 시드로 만들어서 같은 시드면 결과가 항상
동일하므로(수치 변경 전/후 비교 가능), 밸런스 JSON을 바꿔 가며 반복 평가할 수 있습니다.

```bash
dotnet run --project DungeonVM.Simulator -c Release -- 3000 --evaluate
dotnet run --project DungeonVM.Simulator -c Release -- 3000 --evaluate --balance my_balance.json --eval-out eval.json
```

| 옵션 | 설명 |
|---|---|
| `[숫자]` | 기준선 측정 런 수 (기본 1000, 권장 3000) |
| `--relic-runs <n>` | 유물 격차 측정 시 선택지별 런 수 (기본 2000) |
| `--skip-relics` | 유물 격차 측정 생략(전체 약 20초, 포함 시 약 50초 — 3000런·선택지별 2000런 기준) |
| `--targets <경로>` | 목표 지표 파일 지정(기본: 빌드 출력 폴더의 `BalanceTargets.json`) |
| `--eval-out <경로>` | 결과 JSON 저장 경로 |
| `--seed <n>` | 난수 시드(기본 12345) |

각 지표는 `OK`(목표 안) / `LOW`(미달) / `HIGH`(초과) / `INSUFFICIENT`(표본 부족으로 판정 보류)로 판정되고, `deviation`은
허용 범위 밖으로 벗어난 거리입니다. 목표가 `draft`로 표시된 지표(무기 성장)는 디자이너 확정 전 초안입니다.

**플레이 성향별 지표**: `BalanceTargets.json`의 `playstyles.bots`에 적은 봇(기본 Balanced/Hoarder/Spender/BossPrep)을 같은
런 수로 추가로 돌려, 어느 한 성향이 압도적이거나(`styleClearRate.<봇>` 상한) 막혀 있지 않은지(`styleStageRatio` — 가장 약한
성향의 평균 클리어 스테이지 ÷ 가장 강한 성향 값의 하한) 봅니다. 스테이지 승률 같은 기본 지표는 계속 Balanced 봇 하나로
재며, 성향별 지표는 튜닝 점수에 포함됩니다. 봇 이름은 [`BotCatalog`](Bots/BotCatalog.cs)에 등록하면 목표 파일에서 쓸 수 있습니다.

## 밸런스 튜닝 루프 (`--tune`)

평가기가 찾아낸 편차를 LLM에 넘겨 수치 변경안을 받고, 검증·재시뮬레이션으로 채택 여부를 판단하는 자동 밸런싱 루프입니다.

```
평가(목표 대비 편차) → 제안자(LLM)가 화이트리스트 안에서 변경안 제안 → 검증(경로/허용 범위/중복/개수)
   → 적용 후 재시뮬레이션 → 점수가 개선되면 채택, 아니면 되돌림 → 반복
   → 마지막에 다른 시드(시드+1)로 원본/튜닝본을 다시 측정해 과적합 여부 확인
```

```bash
export ANTHROPIC_API_KEY=...   # 또는 OPENAI_API_KEY
dotnet run --project DungeonVM.Simulator -c Release -- 3000 --tune --iterations 5
dotnet run --project DungeonVM.Simulator -c Release -- 3000 --tune --mock-llm   # API 키 없이 루프 동작만 확인
```

| 옵션 | 설명 |
|---|---|
| `[숫자]` | 평가 1회당 시뮬레이션 런 수 (권장 3000, 반복 1회에 약 20초) |
| `--iterations <n>` | 최대 반복 횟수 (기본 3) |
| `--max-changes <n>` | 반복 1회당 변경 가능한 파라미터 수 (기본 4 — 적게 바꿔야 어떤 변경이 효과를 냈는지 알 수 있음) |
| `--mock-llm` | 실제 LLM 대신 규칙 기반 대역 사용(**LLM이 아님**, 보스/몹 스탯·난이도 게이트만 다루는 단순 규칙) |
| `--tune-out <폴더>` | 결과 저장 폴더(기본: 빌드 출력 폴더의 `tuning/`) |
| `--targets`, `--seed`, `--balance` | 평가기와 동일 |

**안전장치**: LLM은 `BalanceTargets.json`의 `tunableParameters`에 있는 파라미터만, 정해진 `[min, max]` 안에서만 바꿀 수 있고,
범위 밖/미등록 경로/현재값과 같은 값/같은 경로 중복/개수 초과 제안은 시뮬레이션에 적용되기 전에 거부됩니다. 점수는 목표를
벗어난 지표의 정규화된 이탈 거리 합이며(낮을수록 좋음), 개선되지 않은 변경은 자동으로 되돌려지고 그 이력이 다음 프롬프트에 포함됩니다.
초안(draft) 지표와 '유물 격차'는 현재 화이트리스트로 움직일 수 없어 점수에서 제외합니다(리포트에는 표시).

출력: `tuned_balance.json`(전체 섹션을 담은 밸런스 — `--balance`로 그대로 적용해 재현 가능), `tuning_log.json`(반복별 제안·거부 사유·
점수·검증 시드 결과).

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
