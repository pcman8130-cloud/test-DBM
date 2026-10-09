using DungeonVM.Core.Enums;
using DungeonVM.Core.Models;
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

    /// <summary>스테이지 클리어 시 3개 상자(장비/특수/능력치) 중 하나를 고른다. choice.Options의 인덱스를 반환.</summary>
    int ChooseStageReward(BotContext ctx, StageRewardChoice choice);

    /// <summary>고른 상자를 열어 나온 후보(보통 3개) 중 하나를 고른다. 구현하지 않으면 기본 규칙(RewardCandidatePolicies.Default)을 쓴다.</summary>
    int ChooseRewardCandidate(BotContext ctx, StageRewardReveal reveal) => RewardCandidatePolicies.Default(ctx, reveal);

    /// <summary>고대 주화(보스 유물)를 골랐을 때 이후 나오지 않게 막을 무기 종류를 고른다. 구현하지 않으면 안 쓰는 종류(지팡이 우선)를 막는다.</summary>
    WeaponType ChooseBannedWeapon(BotContext ctx) => RewardCandidatePolicies.DefaultBannedWeapon(ctx);

    /// <summary>중간보스·보스 처치 시(5/10/15/20/25/30스테) 보스 유물 후보 중 하나를 확정 선택한다. candidates의 인덱스를 반환.</summary>
    int ChooseBossRelic(BotContext ctx, IReadOnlyList<BossRelic> candidates);
}
