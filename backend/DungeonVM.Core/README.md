# DungeonVM.Core

순수 C# 도메인 엔진 (`netstandard2.1`). UnityEngine, MonoBehaviour 등 Unity 의존성이 전혀 없어
Unity 프로젝트뿐 아니라 콘솔 시뮬레이터(`DungeonVM.Simulator`)에서도 동일하게 사용됩니다.
UI 렌더링, 스프라이트, 연출은 이 프로젝트의 책임이 아닙니다 — 상태와 규칙만 다룹니다.

## 폴더 구조

| 폴더 | 내용 |
|---|---|
| `Balance/` | BalanceData(밸런스 JSON 스키마), BalanceProvider(로딩/오버레이), DefaultBalance.json(임베디드 기본값) |
| `Enums/` | WeaponType, ArmorType, ArmorRarity, ElementType, StagePhase, RowPosition, RunEndReason, CurrencyType |
| `Models/` | Character(빈 껍데기 아바타), Weapon, Armor, Monster, RelicDef/RelicMods/RelicCatalog(유물 정의와 효과 합산), Rune, VendingMachine, WeaponCatalog(무기 기초 스탯/레벨 곡선) |
| `Combat/` | DamageCalculator(속성 상성), FormationManager(전열/후열 자동 배치), WaveEngine(웨이브 생성), BattleField(실시간 틱 시뮬레이션, 성서 힐 + 속성 룬 효과 포함) |
| `Inventory/` | MergeGrid(4x4, 무기+방어구 공유), InventoryManager(자동 머지 + 전투 중 동종 강화 + 룬 전용 보관함) |
| `Systems/` | CurrencyManager(골드/영혼), MetaProgression(영구 스킬트리), StageLoop(1~30 스테이지 + 캐릭터 슬롯 해금 + 스테이지 보상 선택 + 유물 효과 적용), StageRewardChoice(상자 3개 데이터와 열린 결과) |

## 핵심 개념

- **캐릭터는 빈 껍데기**: [`Character`](Models/Character.cs)는 기본 스탯이 없고, 장착한 무기(사거리/데미지/공속)와
  방어구(체력/공속 보너스/회피)가 모든 능력치를 결정합니다. 초기 2명, 골드로 최대 5명까지 슬롯을 해금합니다
  ([`StageLoop.UnlockCharacterSlot`](Systems/StageLoop.cs)).
- **무기는 1~15레벨 머지 승급**: 동일 `Type`+`Tier` 무기 2개를 합치면 `Tier+1`이 됩니다 ([`Weapon.MergeInto`](Models/Weapon.cs)).
  10레벨이 실질적인 엔드스펙, 15레벨은 극단적으로 희귀한 하이롤로 설계된 완만한 성장 곡선을 씁니다
  ([`WeaponCatalog.LevelMultiplierAt`](Models/WeaponCatalog.cs)). 레벨 5/10/15(무기 스킬 해금 마일스톤)에
  도달하면 전투력 보너스가 근사적으로 가산됩니다(`SkillBonusAt`) — 성서(Bible)는 이 보너스가 파티 전체를
  틱마다 회복시키는 실제 힐량으로 적용됩니다([`BattleField.Tick`](Combat/BattleField.cs)).
  **소켓된 룬은 머지해도 사라지지 않고 무기에 귀속**됩니다(`Weapon.MergeInto`) — 룬이 있는 무기가 머지의 앞쪽이면 그
  속성을, 아니면 상대 무기의 속성을 이어받고, 둘 다 룬이 있으면 앞쪽 무기의 룬만 남습니다(예전엔 머지 시 소멸하는
  딜레마 규칙이었으나 변경됨).
- **방어구도 그리드를 공유**: 방어구는 머지 없이 등급(Common~Legendary)별 랜덤 스탯으로 드롭/구매되지만, 무기
  재료와 **같은 4x4 그리드 칸을 공유**해 공간 병목을 유발합니다([`MergeGrid`](Inventory/MergeGrid.cs)).
- **룬은 전용 보관함**: 속성 룬은 구매 수단이 없고, 스테이지 클리어 보상의 '특수 상자'에서만 확률적으로
  나옵니다([`StageLoop.ResolveStageRewardChoice`](Systems/StageLoop.cs)). 획득한 룬은 그리드가 아닌 별도 보관함
  ([`InventoryManager.RuneStorage`](Inventory/InventoryManager.cs))에 쌓이고, 필요할 때 무기에 소켓합니다
  (`InventoryManager.TrySocketRune`). 소켓된 룬은 속성 상성 배율과 별개로 원소별 고유 전투 효과(화상/둔화/중첩독/
  전이피해/흡혈/추가피해)를 항상 발동시킵니다([`BattleField.ApplyElementEffect`](Combat/BattleField.cs)).
- **스테이지 보상은 기본+상자 3택1, 다시 후보 3택1(2중 선택)**: 클리어 시 등급별 기본보상을 먼저 지급하고, 장비 상자·특수 상자·능력치 상자 중
  하나를 고르게 합니다([`StageLoop.CompleteStageVictory`](Systems/StageLoop.cs)가 상자 목록 반환). 상자를 고르면 `OpenRewardBox`가 **결과 후보
  3개를 미리 굴려 보여 주고**(`StageRewardReveal`), 그중 하나를 `ClaimRewardCandidate`로 확정합니다(`StageRewardOutcome` 반환).
  - 장비 상자 후보: 무기 1개(종류·레벨 확정) / 방어구 1개(종류·등급 확정) / 골드. 품질은 5스테이지 구간 표(`EquipmentBoxBands`)를 따릅니다:
    5스테 무기 Lv.3~4 / 10스테 Lv.4~6 / 15스테 Lv.5~7 / 20스테 Lv.7~8 / 25스테 Lv.8~9 / 30스테 Lv.9~10(표에 없어 추정), 방어구는 5~15스테
    Rare~Epic, 20·25·30스테 Epic~Legendary. 일반 스테이지는 다음 보스 스테이지 표를 기준으로 무기 최소 레벨을 1 낮춘 범위(낮춘 레벨 확률
    `RegularStageLowestLevelChance`)에서 나오고 방어구는 Rare/Epic만 나옵니다. 골드는 `…GoldOption + …GoldOptionPerStage × 스테이지`(유물 골드 배율 적용).
    무기/방어구를 골랐는데 그리드가 가득 차 있으면 사라집니다(`Stored=false`).
  - 특수 상자 후보: 룬 또는 유물 3개(각각 `*BoxRuneChance` 확률로 룬, 서로 다른 것).
  - 능력치 상자 후보: 공격력%(현재 공격력의 %)·체력(고정값)·회피율·공격속도·쿨타임 감소 중 서로 다른 3개. 쿨타임 감소는 별도 쿨타임 스탯이 없어
    무기 스킬 발동 확률 배율로 근사합니다.
  유물은 [`RelicCatalog`](Models/RelicCatalog.cs)에 보스 유물(5·10·15·20·25·30스테이지 확정 선택)과 일반 유물(특수 상자, 1·11·21스테이지부터 단계적으로 해금, 중복 없음)로 정의되어 있고, 효과는 [`RelicMods`](Models/RelicMods.cs)로 합산되어 `Character`/`BattleField`/`StageLoop`가 읽습니다(고대 주화는 무기 한 종류를 이후 자판기·보상에서 제외).
- **전투는 틱 기반**: [`BattleField.Tick(dt)`](Combat/BattleField.cs)를 반복 호출해 진행합니다. 몬스터는 전열 우선으로
  캐릭터를 공격합니다(자판기는 체력이 없어 공격받지 않음). 동시 교전 가능 수는 `EngagementCap`(기본 3)으로 제한되어
  웨이브 전체가 캐릭터 한 명에게 몰리지 않도록 근사했습니다. **패배 조건은 출격한 모험가 전원 전멸뿐**입니다.
- **2중 재화**: 골드(뽑기/업그레이드/그리드 해금/캐릭터 슬롯), 영혼(중간보스·보스 클리어 기본보상 + 런 종료 후 정산되는
  영구 메타 프로그레션). 룬/유물은 재화로 사는 게 아니라 스테이지 보상의 특수 상자에서만 나옵니다.

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
  `StageLoop`, `Character`, `DamageCalculator`, `BattleField`, `MergeGrid`), 그리고
  `tools/balance_pipeline/balance_schema.py`를 함께 갱신해야 합니다.

각 항목이 실제로 어느 클래스에서 소비되는지는 아래를 참고하세요.

| 항목 | 소비 클래스 |
|---|---|
| 무기별 기초 데미지/공속/체력/특수치, 레벨별 성장 배율(1~15), 스킬 마일스톤(Lv.5/10/15) 보너스 | [`Models/WeaponCatalog.cs`](Models/WeaponCatalog.cs) |
| 자판기 뽑기 확률(2티어 확률, 방어구 등급 확률), 업그레이드 비용 | [`Models/VendingMachine.cs`](Models/VendingMachine.cs) |
| 방어구 등급별 랜덤 스탯 범위 | [`Models/Armor.cs`](Models/Armor.cs) |
| 그리드 해금 비용(무기+방어구 공유) | [`Inventory/MergeGrid.cs`](Inventory/MergeGrid.cs) |
| 캐릭터 시작/최대 인원, 슬롯 해금 골드 비용 | `CharacterSlots` 섹션 — [`Systems/StageLoop.cs`](Systems/StageLoop.cs)에서 소비 |
| 몬스터 스케일링, 몹 수, 중간/대형 보스 스탯 | [`Combat/WaveEngine.cs`](Combat/WaveEngine.cs) |
| 속성 상성 배율(순환/Holy↔Dark) | [`Combat/DamageCalculator.cs`](Combat/DamageCalculator.cs) |
| 속성 룬 6종 고유 효과(화상/둔화/중첩독/전이피해/흡혈/추가피해) 수치 | `ElementEffects` 섹션 — [`Combat/BattleField.cs`](Combat/BattleField.cs)에서 소비 |
| 동시 교전 가능 수(EngagementCap, 밸런스 JSON 미포함 — 상수 유지) | [`Combat/BattleField.cs`](Combat/BattleField.cs) |
| 장비 판매 시세/환급 비율 | [`Systems/CurrencyManager.cs`](Systems/CurrencyManager.cs) |
| 영혼 스킬트리 비용/효과 | [`Systems/MetaProgression.cs`](Systems/MetaProgression.cs) |
| 스테이지 클리어 기본보상(골드/영혼) | [`Systems/StageLoop.cs`](Systems/StageLoop.cs) |
| 스테이지 보상 3개 상자(장비 상자 무기↔방어구/등급, 특수 상자 룬↔유물 확률, 능력치 상자 공격력%·체력·유틸 수치), 등급별(기본/중간보스/보스) 차등 | `StageRewardChoice` 섹션 — [`Systems/StageLoop.cs`](Systems/StageLoop.cs)에서 소비 |
| 캐릭터 기본 체력/리타이어 지속시간 | [`Models/Character.cs`](Models/Character.cs) |

## 빌드

`backend/` 안에서 실행합니다.

```bash
dotnet build DungeonVM.Core/DungeonVM.Core.csproj
```
