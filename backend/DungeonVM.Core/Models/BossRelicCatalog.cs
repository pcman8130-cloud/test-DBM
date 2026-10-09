namespace DungeonVM.Core.Models;

/// <summary>
/// 스테이지 5/10/15/20/25/30 클리어 시 확정 선택하는 보스 유물 후보군. 정의는 RelicCatalog(RelicKind.Boss)에 있고,
/// 이 클래스는 시뮬레이터/평가기가 쓰는 (Id, 이름) 목록만 돌려준다.
/// </summary>
public static class BossRelicCatalog
{
    public static IReadOnlyList<BossRelic> CandidatesFor(int stage)
        => RelicCatalog.BossCandidatesFor(stage).Select(r => new BossRelic(r.Id, r.Name)).ToList();
}
