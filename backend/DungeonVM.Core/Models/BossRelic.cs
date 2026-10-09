namespace DungeonVM.Core.Models;

/// <summary>중간보스/대형보스 처치 시 확정 선택하는 보스 전용 유물의 (Id, 이름). 효과는 RelicCatalog의 RelicDef.Mods에 있다.</summary>
public sealed class BossRelic
{
    public string Id { get; }
    public string Name { get; }

    public BossRelic(string id, string name)
    {
        Id = id;
        Name = name;
    }
}
