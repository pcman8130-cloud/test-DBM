namespace DungeonVM.Core.Enums;

/// <summary>스테이지 보상 등급. 기본/중간보스(ST 5·15·25)/보스(ST 10·20·30) 순으로 기본보상과 선택보상이 강화된다.</summary>
public enum StageTier
{
    Regular,
    MidBoss,
    Boss,
}
