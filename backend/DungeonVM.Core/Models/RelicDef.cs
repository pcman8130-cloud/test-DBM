namespace DungeonVM.Core.Models;

public enum RelicKind
{
    /// <summary>중간보스·대형보스 처치 시 확정으로 고르는 유물(5·10·15·20·25·30스테이지).</summary>
    Boss,

    /// <summary>스테이지 보상의 특수 상자에서 나오는 일반 유물.</summary>
    Regular,
}

/// <summary>
/// 유물 1종의 정의. Mods는 이 유물이 주는 효과 수치이고, GoldOnPick/BansWeapon은 획득 즉시 일어나는 일회성 효과다.
/// UnlockStage: 보스 유물은 그 보스 스테이지, 일반 유물은 이 스테이지 이후의 보상부터 등장한다.
/// </summary>
public sealed record RelicDef(
    string Id, string Name, string Description, RelicKind Kind, int UnlockStage, RelicMods Mods,
    int GoldOnPick = 0, bool BansWeapon = false);
