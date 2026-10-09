namespace DungeonVM.Core.Enums;

/// <summary>스테이지 클리어 시 고르는 3개 상자 중 하나의 유형. 상자를 고르는 것이 1차 선택이고,
/// 상자 안에서 무엇이 나오는지는 열 때 한 번 더 굴린다(2중 가챠).</summary>
public enum StageRewardOptionType
{
    /// <summary>무기(Lv.3~4) 또는 방어구(Rare~Epic) — 둘 중 하나가 나온다.</summary>
    EquipmentBox,

    /// <summary>룬(속성 랜덤) 또는 유물 — 둘 중 하나가 나온다.</summary>
    SpecialBox,

    /// <summary>공격력% / 체력 / 유틸(회피율·공격속도·쿨타임 감소 중 하나) 중 하나가 나온다.</summary>
    StatBox,
}
