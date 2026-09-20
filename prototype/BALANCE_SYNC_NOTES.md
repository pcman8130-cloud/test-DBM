# 밸런스 동기화 기록 (2026-09-20)

`prototype/index.html`(구 `dungeon-vending-machine.html`, Vercel 정적 배포를 위해 리네임)의 수치를 `backend/DungeonVM.Core/Balance/DefaultBalance.json` 기준으로 동기화한 작업 기록.
백엔드 파일은 수정하지 않았음 — 전부 HTML 쪽만 변경.

## 사용법

`prototype/balance-snapshot.json`은 `DefaultBalance.json`과 동일한 스키마(`BalanceData.cs`)로 작성된,
**HTML 프로토타입이 실제로 쓰고 있는 수치의 스냅샷**이다. 시뮬레이터에 그대로 먹일 수 있다:

```bash
dotnet run --project DungeonVM.Simulator/DungeonVM.Simulator.csproj -c Release -- 500 --balance ../prototype/balance-snapshot.json
```

`BalanceProvider.LoadFromJson`은 **섹션 단위로만 병합**한다(필드 단위 아님) — 한 섹션을 넣으려면 그 섹션의 모든 필드를
채워야 하고, 일부만 넣으면 나머지 필드는 0/기본값으로 덮어써진다. 그래서 `balance-snapshot.json`은 항상 전체 섹션을
통째로 담고 있고, 프로토타입이 관여하지 않는 값(예: `metaProgression`, 영혼/`souls` 관련 필드)은 백엔드 기본값을
그대로 들고 있다 — 프로토타입에 아직 영혼/메타프로그레션 시스템 자체가 없기 때문.

## 현재 상태: 백엔드 동기화 이후 플레이테스트로 재조정된 값들

동기화 직후엔 성서(Bible) 하나만 빼고 스키마 수치가 전부 백엔드와 일치했으나, 실제로 플레이해보니
백엔드의 웨이브 수치가 너무 낮아 몹이 너무 쉽게 죽는 "방치형" 체감이 나서(2026-09-20 플레이테스트 피드백),
아래 두 영역은 **백엔드보다 의도적으로 높게** 재조정했다. 이 시점부터 프로토타입의 웨이브 밸런스는
백엔드와 별개로 독자적인 값을 가진다 — `balance-snapshot.json`도 최신값으로 갱신됨.

| 필드 | 백엔드 | 프로토타입 | 비고 |
|---|---|---|---|
| `weapons.table.Bible.baseDamage` | 4 | **8** | 과거 유저 요청으로 의도적으로 상향 |
| `weapons.table.Bible.bonusHealth` | 8 | **18** | 과거 유저 요청으로 의도적으로 상향 |
| `wave.mobBaseHealth` | 12 | **32** | 몬스터가 너무 쉽게 죽는다는 플레이테스트 피드백으로 상향 |
| `wave.mobBaseDamage` | 1.5 | **2.8** | 상동 |
| `wave.midBossHealth` | 220 | **360** | 상동 |
| `wave.midBossDamage` | 10 | **13** | 상동 |
| `wave.bigBossHealth` | 600 | **950** | 상동 |
| `wave.bigBossDamage` | 18 | **22** | 상동 |
| `DIFFICULTY_GATES`(11/21스테 배율) | 없음(백엔드엔 이 개념 자체가 없음) | ×2.6 / ×2.6 | 방치형 체감 완화를 위해 신규 추가 |

그 외 무기 6종 기초스탯, 레벨 배율 15단, 스킬 누적보너스, 방어구 등급별 굴림범위, 자판기 비용/확률,
웨이브 스케일 곡선의 형태(`1+0.035*(stage-1)` + 11/21스테 게이트), 몹 수 공식, 골드 공식, 그리드/파티슬롯
해금 비용, 속성 룬 6종 수치, 스테이지 보상 골드/상자 확률은 여전히 백엔드와 동일한 공식을 쓴다 — 위 표의
기준값(base)들만 플레이감을 위해 올라간 상태.

## 숫자로는 못 담는 구조적 차이 (JSON 스냅샷에 반영 불가)

아래 4가지는 **수치가 아니라 로직 자체가 다른 경우**라 `BalanceData` 필드 하나로 표현할 수 없다.
시뮬레이터가 프로토타입과 완전히 똑같이 행동하게 하려면, 이 항목들은 백엔드 C# 코드(`VendingMachine.cs`,
`CurrencyManager.cs`, `StageLoop.cs`, `BattleField.cs`)를 프로토타입 동작에 맞게 고치거나, 반대로
프로토타입을 백엔드 로직에 맞게 되돌려야 한다 — 순수 밸런스 JSON 값 교체로는 절대 안 됨.

1. **무기 뽑기 레벨 분포**
   - 백엔드(`VendingMachine.RollWeapon`): `tier2ChanceBase`/`PerLevel`로 항상 1랩 또는 2랩 중 하나만 등장(이진)
   - 프로토타입(`WEAPON_LEVEL_TABLE`, html 라인 834~852): 자판기 강화 레벨별로 1~7랩 범위 확률표
   - `vendingMachine.tier2ChanceBase` 등은 스냅샷에 값은 있지만 프로토타입은 실제로 이 필드를 전혀 참조하지 않음

2. **장비 판매가**
   - 백엔드(`CurrencyManager`): 무기는 `weaponMarketValueTierBase * 레벨배율 * sellRefundRatio`, 방어구는 등급별 시세표 * 비율(티어/등급에 따라 차등)
   - 프로토타입(`ITEM_SELL_VALUE`, html 라인 547~549): 무기/방어구 공통 flat 5G (`ROLL_COST*0.5`, 레벨·등급 무관)

3. **스테이지 보상 '팀 능력치' 옵션**
   - 백엔드(`StageLoop.ResolveStageRewardChoice`): 공격력%+체력% **둘 다 동시에** 영구 가산 (`Character.AddRunBonus`)
   - 프로토타입(`applyStatReward`, html 라인 1448~1460): 공격 또는 체력 중 **랜덤으로 하나만**, 값도 flat 가산

4. **성서(Bible) 힐**
   - 백엔드(`BattleField.Tick`): 레벨 무관하게 매 틱 파티 전체를 지속 회복 + 동시에 평타 공격도 정상 수행
   - 프로토타입(`applyWeaponSkill`, html 라인 1343~1353): Lv.5 스킬 티어에 도달해야만, 공격 시에만 회복 발동

## 추가로 완전히 빠져 있는 백엔드 메커닉 (수치 동기화 범위 밖)

- 속성 상성 시스템(화염→얼음→번개→화염 순환 + 빛↔어둠, `combat.elementAdvantageMultiplier`/`DisadvantageMultiplier`)과
  대형보스 원소 순환 배정 — 몬스터에 속성 필드 자체가 없어서 미구현
- 캐릭터별 개별 리타이어(기절 30초 후 자동 부활) — 지금은 파티 전멸 시 즉시 게임오버
- 방패(Shield)의 어그로 유발(`pullAggro`) — 프로토타입은 항상 전열 우선 고정 타겟팅이라 어그로 개념 자체가 없음
- 영혼(Souls)/메타프로그레션 영구 스킬트리 — 프로토타입에 재화 자체가 없음
