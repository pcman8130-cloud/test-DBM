# DungeonVM.Core

순수 C# 도메인 엔진 (`netstandard2.1`). UnityEngine, MonoBehaviour 등 Unity 의존성이 전혀 없어
Unity 프로젝트뿐 아니라 콘솔 시뮬레이터(`DungeonVM.Simulator`)에서도 동일하게 사용됩니다.
UI 렌더링, 스프라이트, 연출은 이 프로젝트의 책임이 아닙니다 — 상태와 규칙만 다룹니다.

## 폴더 구조

| 폴더 | 내용 |
|---|---|
| `Balance/` | BalanceData(밸런스 JSON 스키마), BalanceProvider(로딩/오버레이), DefaultBalance.json(임베디드 기본값) |
| `Enums/` | WeaponType, ArmorType, ArmorRarity, ElementType, StagePhase, RowPosition, RunEndReason, CurrencyType |
| `Models/` | Character(빈 껍데기 아바타), Weapon, Armor, Monster, Relic, Rune, VendingMachine, WeaponCatalog(무기 기초 스탯 테이블) |
| `Combat/` | DamageCalculator(속성 상성), FormationManager(전열/후열 자동 배치), WaveEngine(웨이브 생성), BattleField(실시간 틱 시뮬레이션) |
| `Inventory/` | MergeGrid(4x4 머지 보관함), InventoryManager(자동 머지 + 전투 중 동종 강화) |
| `Systems/` | CurrencyManager(골드/보석/영혼), MetaProgression(영구 스킬트리), StageLoop(1~30 스테이지 오케스트레이션) |

## 핵심 개념

- **캐릭터는 빈 껍데기**: [`Character`](Models/Character.cs)는 기본 스탯이 없고, 장착한 무기(사거리/데미지/공속)와
  방어구(체력/공속 보너스/회피)가 모든 능력치를 결정합니다.
- **무기는 머지로 승급**: 동일 `Type`+`Tier` 무기 2개를 합치면 `Tier+1`이 됩니다 ([`Weapon.MergeInto`](Models/Weapon.cs)).
  전투 중에도 필드에서 사용 중인 무기와 같은 종류가 그리드에서 완성되면 즉시 드래그해 강화할 수 있습니다
  ([`InventoryManager.TryFieldUpgrade`](Inventory/InventoryManager.cs)).
- **방어구는 완제품**: 머지 없이 등급(Common~Legendary)별 랜덤 스탯으로 드롭/구매됩니다.
- **전투는 틱 기반**: [`BattleField.Tick(dt)`](Combat/BattleField.cs)를 반복 호출해 진행합니다. 몬스터는 전열 우선으로
  캐릭터를 공격하고, 방어선이 없으면 자판기를 공격합니다. 동시 교전 가능 수는 `EngagementCap`(기본 3)으로 제한되어
  웨이브 전체가 캐릭터 한 명에게 몰리지 않도록 근사했습니다.
- **3중 재화**: 골드(일반 소모), 보석(룬/유물), 영혼(런 종료 후 정산되는 영구 메타 프로그레션).

## 밸런스 수치 조정 위치

무기/방어구/자판기/웨이브/영혼 스킬트리/재화/스테이지 보상/캐릭터/속성 상성/머지 그리드의 모든 수치는
더 이상 C# 하드코딩이 아니라 **[`Balance/BalanceProvider.cs`](Balance/BalanceProvider.cs)를 거친 JSON 값**입니다.
기본값은 임베디드 리소스 [`Balance/DefaultBalance.json`](Balance/DefaultBalance.json)이고, 스키마는
[`Balance/BalanceData.cs`](Balance/BalanceData.cs)에 정의되어 있습니다.

- **값만 바꾸고 싶다면**: `Balance/DefaultBalance.json`을 직접 수정하거나(가장 빠름),
  `tools/balance_pipeline`의 엑셀 파이프라인으로 생성한 JSON을 같은 경로에 덮어씁니다.
- **런타임에 다른 값으로 실행하고 싶다면**(예: 시뮬레이터 A/B 비교): `BalanceProvider.LoadFromFile(path)` /
  `LoadFromJson(json)`을 앱 시작 시 호출합니다. `DungeonVM.Simulator`는 `--balance <path>` CLI 인자로 이미 지원합니다.
  넘긴 JSON에 없는 섹션은 이전 값을 그대로 유지하므로, 바꾸고 싶은 섹션만 담아도 됩니다.
- **필드를 추가/삭제하는 구조 변경이라면**: `Balance/BalanceData.cs`, `Balance/DefaultBalance.json`,
  각 소비 클래스(`WeaponCatalog`, `VendingMachine`, `Armor`, `WaveEngine`, `MetaProgression`, `CurrencyManager`,
  `StageLoop`, `Character`, `DamageCalculator`, `MergeGrid`), 그리고 `tools/balance_pipeline/balance_schema.py`를
  함께 갱신해야 합니다.

각 항목이 실제로 어느 클래스에서 소비되는지는 아래를 참고하세요.

| 항목 | 소비 클래스 |
|---|---|
| 무기별 기초 데미지/공속/체력/특수치, 티어당 성장 배율 | [`Models/WeaponCatalog.cs`](Models/WeaponCatalog.cs) |
| 자판기 뽑기 확률(2티어 확률, 방어구 등급 확률), 업그레이드 비용 | [`Models/VendingMachine.cs`](Models/VendingMachine.cs) |
| 방어구 등급별 랜덤 스탯 범위 | [`Models/Armor.cs`](Models/Armor.cs) |
| 그리드 해금 비용 | [`Inventory/MergeGrid.cs`](Inventory/MergeGrid.cs) |
| 몬스터 스케일링, 몹 수, 중간/대형 보스 스탯 | [`Combat/WaveEngine.cs`](Combat/WaveEngine.cs) |
| 속성 상성 배율(순환/Holy↔Dark) | [`Combat/DamageCalculator.cs`](Combat/DamageCalculator.cs) |
| 동시 교전 가능 수(EngagementCap, 밸런스 JSON 미포함 — 상수 유지) | [`Combat/BattleField.cs`](Combat/BattleField.cs) |
| 장비 판매 시세/환급 비율 | [`Systems/CurrencyManager.cs`](Systems/CurrencyManager.cs) |
| 영혼 스킬트리 비용/효과 | [`Systems/MetaProgression.cs`](Systems/MetaProgression.cs) |
| 스테이지 클리어 보상(골드/보석/영혼) | [`Systems/StageLoop.cs`](Systems/StageLoop.cs) |
| 캐릭터 기본 체력/리타이어 지속시간 | [`Models/Character.cs`](Models/Character.cs) |

## 빌드

`backend/` 안에서 실행합니다.

```bash
dotnet build DungeonVM.Core/DungeonVM.Core.csproj
```
