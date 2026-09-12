"""
DungeonVM 밸런스 JSON 스키마 정의.

DungeonVM.Core/Balance/BalanceData.cs 의 필드와 1:1로 대응한다.
C# 쪽 클래스에 필드를 추가/삭제하면 이 파일도 함께 갱신해야 한다.

- SCALAR_FIELDS: 엑셀의 "Scalars" 시트(Key/Value 두 컬럼)에 대응하는 단일 값 필드 전체 목록.
  Key는 "섹션.필드명" 형태의 점(dot) 표기법을 쓴다 (예: "VendingMachine.BaseHealth").
- TABLE_SHEETS: 무기별/등급별처럼 행이 여러 개인 값들은 전용 시트로 분리한다.
"""

from __future__ import annotations

from dataclasses import dataclass, field

WEAPON_TYPES = ["Sword", "Shield", "Dagger", "Bow", "Staff", "Bible"]
ARMOR_RARITIES = ["Common", "Rare", "Epic", "Legendary"]
ROW_POSITIONS = ["Front", "Back"]


@dataclass
class ScalarField:
    section: str
    field: str
    value_type: type
    description_kr: str

    @property
    def key(self) -> str:
        return f"{self.section}.{self.field}"


# 순서는 DefaultBalance.json / BalanceData.cs의 섹션 순서를 그대로 따른다.
SCALAR_FIELDS: list[ScalarField] = [
    ScalarField("Weapons", "TierDamageMultiplier", float, "머지 1회(동일 무기 2개 결합)당 데미지 성장 배율"),
    ScalarField("Weapons", "MaxTier", int, "무기 최대 티어"),

    ScalarField("Armor", "DodgeClampMax", float, "회피율 상한(0~1)"),

    ScalarField("VendingMachine", "BaseHealth", float, "자판기 기본 체력"),
    ScalarField("VendingMachine", "MaxUpgradeLevel", int, "공격/방어 업그레이드 최대 레벨"),
    ScalarField("VendingMachine", "WeaponRollCost", int, "무기 뽑기 1회 골드 비용"),
    ScalarField("VendingMachine", "ArmorRollCost", int, "방어구 뽑기 1회 골드 비용"),
    ScalarField("VendingMachine", "Tier2ChanceBase", float, "공격 업그레이드 Lv1 기준 2티어 무기 등장 확률"),
    ScalarField("VendingMachine", "Tier2ChancePerLevel", float, "공격 업그레이드 레벨당 2티어 확률 증가량"),
    ScalarField("VendingMachine", "LegendaryChanceBase", float, "방어 업그레이드 Lv1 기준 전설 등급 확률"),
    ScalarField("VendingMachine", "LegendaryChancePerLevel", float, "방어 업그레이드 레벨당 전설 확률 증가량"),
    ScalarField("VendingMachine", "EpicChanceBase", float, "방어 업그레이드 Lv1 기준 영웅 등급 확률"),
    ScalarField("VendingMachine", "EpicChancePerLevel", float, "방어 업그레이드 레벨당 영웅 확률 증가량"),
    ScalarField("VendingMachine", "RareChanceBase", float, "방어 업그레이드 Lv1 기준 희귀 등급 확률"),
    ScalarField("VendingMachine", "RareChancePerLevel", float, "방어 업그레이드 레벨당 희귀 확률 증가량"),

    ScalarField("Wave", "ScaleBase", float, "웨이브 스탯 스케일 기준값(1스테이지)"),
    ScalarField("Wave", "ScalePerStage", float, "스테이지당 스탯 스케일 증가량"),
    ScalarField("Wave", "MobCountBase", int, "일반 몹 기본 마리 수"),
    ScalarField("Wave", "MobCountStageDivisor", int, "몹 수 = MobCountBase + stage / 이 값"),
    ScalarField("Wave", "MobBaseHealth", float, "일반 몹 기본 체력(스케일 적용 전)"),
    ScalarField("Wave", "MobBaseDamage", float, "일반 몹 기본 공격력(스케일 적용 전)"),
    ScalarField("Wave", "MobApsMin", float, "일반 몹 초당 공격 횟수 최소값"),
    ScalarField("Wave", "MobApsRandomRange", float, "일반 몹 초당 공격 횟수 랜덤 폭(+0~이 값)"),
    ScalarField("Wave", "MobGoldBase", int, "일반 몹 처치 골드 기본값"),
    ScalarField("Wave", "MobGoldStageDivisor", int, "몹 골드 = MobGoldBase + stage / 이 값"),
    ScalarField("Wave", "MidBossHealth", float, "중간 보스 기본 체력(스케일 적용 전)"),
    ScalarField("Wave", "MidBossDamage", float, "중간 보스 기본 공격력(스케일 적용 전)"),
    ScalarField("Wave", "MidBossAps", float, "중간 보스 초당 공격 횟수"),
    ScalarField("Wave", "MidBossGoldBase", int, "중간 보스 처치 골드 기본값"),
    ScalarField("Wave", "MidBossGoldPerStage", int, "중간 보스 골드 스테이지당 증가량"),
    ScalarField("Wave", "BigBossHealth", float, "대형 보스 기본 체력(스케일 적용 전)"),
    ScalarField("Wave", "BigBossDamage", float, "대형 보스 기본 공격력(스케일 적용 전)"),
    ScalarField("Wave", "BigBossAps", float, "대형 보스 초당 공격 횟수"),
    ScalarField("Wave", "BigBossGoldBase", int, "대형 보스 처치 골드 기본값"),
    ScalarField("Wave", "BigBossGoldPerStage", int, "대형 보스 골드 스테이지당 증가량"),

    ScalarField("MetaProgression", "MaxLevel", int, "영혼 스킬트리 항목별 최대 레벨"),
    ScalarField("MetaProgression", "LevelCostBase", int, "레벨업 비용 기본값"),
    ScalarField("MetaProgression", "LevelCostPerLevel", int, "레벨업 비용 = Base + 현재레벨 * 이 값"),
    ScalarField("MetaProgression", "RetireSpeedPerLevel", float, "리타이어 타이머 진행 속도 레벨당 증가율"),
    ScalarField("MetaProgression", "FirstRollTierBoostPerLevel", float, "런 시작 첫 뽑기 2티어 확정 확률 레벨당 증가량"),
    ScalarField("MetaProgression", "BaseHealthBonusPerLevel", float, "전 캐릭터 기본 체력 가산치 레벨당 증가량"),

    ScalarField("Currency", "WeaponMarketValueTierBase", int, "무기 시세 = 이 값 * 2^(티어-1)"),
    ScalarField("Currency", "SellRefundRatio", float, "장비 판매 환급 비율(0~1)"),

    ScalarField("StageLoop", "MaxStage", int, "런 클리어 목표 스테이지 수"),
    ScalarField("StageLoop", "VictoryGoldBase", int, "스테이지 클리어 골드 보상 기본값"),
    ScalarField("StageLoop", "VictoryGoldPerStage", int, "골드 보상 스테이지당 증가량"),
    ScalarField("StageLoop", "VictoryGemsDefault", int, "평시 스테이지 클리어 보석 보상"),
    ScalarField("StageLoop", "VictoryGemsMilestone", int, "마일스톤 스테이지 클리어 보석 보상"),
    ScalarField("StageLoop", "VictoryGemsMilestoneInterval", int, "보석 마일스톤 간격(스테이지)"),
    ScalarField("StageLoop", "VictorySoulsBase", int, "스테이지 클리어 영혼 보상 기본값"),
    ScalarField("StageLoop", "VictorySoulsStageDivisor", int, "영혼 보상 = Base + stage / 이 값"),

    ScalarField("Character", "BaseHealth", float, "캐릭터(빈 껍데기) 기본 체력"),
    ScalarField("Character", "RetireDurationSeconds", float, "리타이어 지속 시간(초)"),

    ScalarField("Combat", "ElementAdvantageMultiplier", float, "속성 상성 유리 시 데미지 배율"),
    ScalarField("Combat", "ElementDisadvantageMultiplier", float, "속성 상성 불리 시 데미지 배율"),

    ScalarField("MergeGrid", "TotalCells", int, "머지 그리드 총 칸 수"),
    ScalarField("MergeGrid", "CellsPerUnlock", int, "그리드 해금 단위(칸)"),
]

SCALAR_FIELDS_BY_KEY: dict[str, ScalarField] = {f.key: f for f in SCALAR_FIELDS}

# 표 형태 시트: (시트 이름, 컬럼 목록, 설명)
WEAPONS_SHEET = "Weapons"
WEAPONS_COLUMNS = ["Type", "Row", "BaseDamage", "AttacksPerSecond", "BonusHealth", "PullAggro"]

ARMOR_ROLL_RANGES_SHEET = "ArmorRollRanges"
ARMOR_ROLL_RANGES_COLUMNS = ["Rarity", "MinHp", "MaxHp", "MinAtk", "MaxAtk", "MinDodge", "MaxDodge"]

ARMOR_MARKET_VALUES_SHEET = "ArmorMarketValues"
ARMOR_MARKET_VALUES_COLUMNS = ["Rarity", "Value"]

VM_UPGRADE_COSTS_SHEET = "VendingMachineUpgradeCosts"
VM_UPGRADE_COSTS_COLUMNS = ["Level", "GoldCost"]

MERGE_GRID_UNLOCK_COSTS_SHEET = "MergeGridUnlockCosts"
MERGE_GRID_UNLOCK_COSTS_COLUMNS = ["Block", "GoldCost"]

SCALARS_SHEET = "Scalars"
SCALARS_COLUMNS = ["Key", "Value", "Description"]
