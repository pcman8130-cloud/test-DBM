# balance_pipeline

기획자가 엑셀(.xlsx)에 밸런스 수치를 적으면, `DungeonVM.Core`/`DungeonVM.Simulator`가 그대로 읽는
JSON(`DungeonVM.Core/Balance/DefaultBalance.json`, 스키마: [`BalanceData.cs`](../../DungeonVM.Core/Balance/BalanceData.cs))으로
변환하는 CLI 툴입니다.

## 설치

```bash
pip install -r requirements.txt
```

## 사용법

### 1) 템플릿 생성 (최초 1회)

현재 `DefaultBalance.json`에 들어있는 실제 수치로 채워진 `balance.xlsx`를 만듭니다.
이 파일을 기획자에게 전달하면 됩니다.

```bash
python xlsx_to_balance_json.py template balance.xlsx
```

시트 구성:

| 시트 | 내용 |
|---|---|
| `Scalars` | `섹션.필드명` 형태의 Key와 Value 두 컬럼. 대부분의 수치(자판기 뽑기 확률, 웨이브 스케일링, 영혼 스킬트리, 속성 룬 효과, 스테이지 선택보상 등)가 여기 있습니다. |
| `Weapons` | 무기 6종의 기초 데미지/공속/체력/특수치 |
| `ArmorRollRanges` | 방어구 등급별(Common~Legendary) 랜덤 스탯 최소/최대값 |
| `ArmorMarketValues` | 방어구 등급별 판매 시세 |
| `VendingMachineUpgradeCosts` | 자판기 업그레이드 레벨별 골드 비용 |
| `MergeGridUnlockCosts` | 머지 그리드 블록 해금 골드 비용 |
| `WeaponLevelMultipliers` | 무기 레벨(1~15)별 데미지/힐 성장 배율 |
| `CharacterSlotUnlockCosts` | 캐릭터 슬롯(3번째~최대)별 해금 골드 비용 |

### 2) 엑셀 수정 후 JSON으로 변환

```bash
# DungeonVM.Core/Balance/DefaultBalance.json에 바로 반영 (기본 동작)
python xlsx_to_balance_json.py convert balance.xlsx

# 다른 경로에 저장하고 싶다면
python xlsx_to_balance_json.py convert balance.xlsx --out override.json
```

`Scalars` 시트의 Key 오타, 필수 항목 누락, 숫자여야 하는 칸에 문자가 들어간 경우 등은 파일을 쓰지 않고
어떤 셀이 문제인지 목록으로 알려줍니다.

### 3) 변환 즉시 시뮬레이터로 검증

```bash
python xlsx_to_balance_json.py convert balance.xlsx --simulate 500
```

`--simulate N`을 주면 변환 직후 `dotnet run --project DungeonVM.Simulator -- N --balance <변환된 JSON>`을
실행해서 그 자리에서 승률/티어 채택률 리포트를 볼 수 있습니다.

## 새 밸런스 필드를 추가하려면

1. `DungeonVM.Core/Balance/BalanceData.cs`에 필드 추가
2. `DungeonVM.Core/Balance/DefaultBalance.json`에 기본값 추가
3. 실제로 그 값을 쓰는 클래스(`WeaponCatalog`, `VendingMachine` 등)에서 `BalanceProvider.Current`를 통해 읽도록 연결
4. 여기 `balance_schema.py`의 `SCALAR_FIELDS`(단일 값) 또는 해당 표 시트 컬럼 목록에 반영
5. `python xlsx_to_balance_json.py template balance.xlsx`로 템플릿을 다시 생성해 기획자에게 전달

## 참고

- 이 스크립트는 항상 `DungeonVM.Core/Balance/DefaultBalance.json`을 기준으로 동작합니다(리포지토리 루트 기준 상대 경로로 자동 탐색).
- Google Sheets를 직접 쓰고 싶다면, 시트를 "파일 > 다운로드 > Microsoft Excel(.xlsx)"로 내려받아 그대로
  `convert`에 넘기면 됩니다. Sheets API 연동(자동 동기화)은 아직 구현되어 있지 않습니다.
