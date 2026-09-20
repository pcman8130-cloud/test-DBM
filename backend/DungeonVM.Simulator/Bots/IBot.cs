using DungeonVM.Core.Systems;

namespace DungeonVM.Simulator.Bots;

/// <summary>가상 유저의 경제적 의사결정 성향 1종. 헤드리스 시뮬레이터가 매 틱/정비 페이즈마다 호출한다.</summary>
public interface IBot
{
    string Name { get; }

    /// <summary>정비 페이즈(스테이지 클리어 직후): 무기 전면 교체, 업그레이드, 판매 등 자유 의사결정.</summary>
    void OnMaintenancePhase(BotContext ctx);

    /// <summary>전투 중 매 틱: 실시간으로 들어오는 골드를 즉시 뽑기/업그레이드에 쓸지 판단.</summary>
    void OnCombatTick(BotContext ctx);

    /// <summary>스테이지 클리어 시 3개 선택보상(골드/팀 능력치 영구증가/상자) 중 하나를 고른다. choice.Options의 인덱스를 반환.</summary>
    int ChooseStageReward(BotContext ctx, StageRewardChoice choice);
}
