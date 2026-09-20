using DungeonVM.Core.Enums;
using DungeonVM.Core.Systems;

namespace DungeonVM.Simulator.Bots;

/// <summary>
/// 그리드 확장 집착형 봇: 골드를 최우선으로 그리드 해금에 쏟아붓고, 룬 소멸을 개의치 않고 무조건 머지해
/// 단일 무기를 최대한 높은 레벨(10레벨 이상)까지 밀어붙인다. "세로 성장" 전략의 극단.
/// -> 그리드 공간은 넉넉해지지만 캐릭터 슬롯/방어구 투자가 밀려 파티 규모가 작게 유지된다.
/// </summary>
public sealed class SpaceExpansionBot : IBot
{
    public string Name => "SpaceExpansion";

    public void OnCombatTick(BotContext ctx)
    {
        ctx.ApplyFreeActions(); // 룬 보존 없이 항상 즉시 머지
        if (!ShouldSaveForNextGoal(ctx))
            while (ctx.TryRollWeapon()) { }
    }

    public void OnMaintenancePhase(BotContext ctx)
    {
        ctx.ApplyFreeActions();

        // 부트스트랩: 맨몸 캐릭터가 있으면 그리드 해금 저축보다 첫 무기 확보가 우선이다.
        // (안 그러면 시작 골드를 첫 정비 페이즈에 그리드 해금에 전부 써버려 영원히 맨손으로 나가게 된다)
        while (ctx.Party.Any(c => c.EquippedWeapon is null))
        {
            EquipUnarmed(ctx);
            if (ctx.Party.All(c => c.EquippedWeapon is not null)) break;
            if (!ctx.TryRollWeapon()) break;
        }
        EquipUnarmed(ctx);

        // 1순위: 그리드 해금 (동시 보관 가능한 재료를 늘려 고레벨 머지 체인을 지원)
        while (ctx.TryUnlockGrid()) { }

        // 다음 그리드 블록 해금이 1~2스테이지 수입으로 곧 감당 가능하면 재뽑기를 멈추고 저축한다.
        // 남는 골드는 그 외엔 전부 무기 뽑기로 재투입.
        if (!ShouldSaveForNextGoal(ctx))
            while (ctx.TryRollWeapon()) { }

        foreach (var character in ctx.Party)
        {
            // 무기 종류를 가리지 않고 그리드의 최고 티어를 계속 흡수한다(끝없는 성장 추구).
            while (ctx.TryEquipFromGrid(character, w => character.EquippedWeapon is null || w.Tier > character.EquippedWeapon.Tier))
            {
            }
        }
    }

    private static void EquipUnarmed(BotContext ctx)
    {
        foreach (var character in ctx.Party.Where(c => c.EquippedWeapon is null))
            ctx.TryEquipFromGrid(character);
    }

    private static bool ShouldSaveForNextGoal(BotContext ctx)
        => !ctx.Inventory.Grid.IsFullyUnlocked && ctx.ShouldSaveFor(ctx.Inventory.Grid.NextUnlockGoldCost());

    /// <summary>항상 골드 — 그리드 해금과 끝없는 재뽑기에 쏟아부을 현금이 최우선이다.</summary>
    public int ChooseStageReward(BotContext ctx, StageRewardChoice choice) => choice.IndexOf(StageRewardOptionType.Gold);
}
