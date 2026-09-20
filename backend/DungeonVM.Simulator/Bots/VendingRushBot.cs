using DungeonVM.Core.Enums;
using DungeonVM.Core.Systems;

namespace DungeonVM.Simulator.Bots;

/// <summary>
/// 자판기·로스터 올인형 봇: 그리드 해금은 최소화하고 자판기 공격/방어 업그레이드와 캐릭터 슬롯(최대 5명)
/// 해금에 골드를 집중한다. "가로 확장" 전략 — 사람은 늘지만 그리드가 좁아 개개인의 무기는 낮은 레벨에 머문다.
/// </summary>
public sealed class VendingRushBot : IBot
{
    private readonly Dictionary<Guid, WeaponType> _committedType = new();

    public string Name => "VendingRush";

    public void OnCombatTick(BotContext ctx)
    {
        ctx.ApplyFreeActions();
        if (!ShouldSaveForNextGoal(ctx))
            while (ctx.TryRollWeapon()) { }
    }

    public void OnMaintenancePhase(BotContext ctx)
    {
        ctx.ApplyFreeActions();

        // 1순위: 자판기 업그레이드 (고티어 등장 확률 자체를 끌어올림)
        while (ctx.TryUpgradeAttack()) { }
        while (ctx.TryUpgradeDefense()) { }

        // 2순위: 캐릭터 슬롯 해금 — 최대 5명까지 최대한 빨리 채운다.
        while (ctx.TryUnlockCharacterSlot()) { }

        // 부트스트랩: 새로 늘어난 슬롯이 맨몸이면 최소한 한 자루는 쥐어준다.
        while (ctx.Party.Any(c => c.EquippedWeapon is null))
        {
            EquipUnarmed(ctx);
            if (ctx.Party.All(c => c.EquippedWeapon is not null)) break;
            if (!ctx.TryRollWeapon()) break;
        }
        EquipUnarmed(ctx);

        // 그리드 해금은 하지 않는다(가로 확장에 골드를 몰아주고 그리드 병목은 그대로 감수).
        // 다음 업그레이드/슬롯이 1~2스테이지 수입으로 곧 감당 가능하면 재뽑기를 멈추고 저축한다
        // (안 그러면 남는 돈을 매번 재뽑기에 다 써버려서 목돈이 드는 목표를 영원히 못 산다).
        if (!ShouldSaveForNextGoal(ctx))
            while (ctx.TryRollWeapon()) { }

        foreach (var character in ctx.Party.Where(c => c.EquippedWeapon is not null))
        {
            var committed = _committedType.GetValueOrDefault(character.Id, character.EquippedWeapon!.Type);
            ctx.TryEquipFromGrid(character, w => w.Type == committed && w.Tier > character.EquippedWeapon!.Tier);
        }
    }

    /// <summary>다음으로 노리는 목표(공격 업그레이드 → 방어 업그레이드 → 캐릭터 슬롯) 비용. 더 살 게 없으면 null.</summary>
    private static int? NextGoalCost(BotContext ctx)
    {
        if (ctx.Machine.CanUpgradeAttack) return ctx.Machine.AttackUpgradeCost;
        if (ctx.Machine.CanUpgradeDefense) return ctx.Machine.DefenseUpgradeCost;
        if (ctx.StageLoop.CanUnlockCharacterSlot) return ctx.StageLoop.NextCharacterSlotGoldCost();
        return null;
    }

    private static bool ShouldSaveForNextGoal(BotContext ctx)
        => NextGoalCost(ctx) is { } cost && ctx.ShouldSaveFor(cost);

    private void EquipUnarmed(BotContext ctx)
    {
        foreach (var character in ctx.Party.Where(c => c.EquippedWeapon is null))
            if (ctx.TryEquipFromGrid(character))
                _committedType[character.Id] = character.EquippedWeapon!.Type;
    }

    /// <summary>항상 팀 능력치 영구증가 — 가로 확장(로스터/업그레이드)에 어울리는 확정적·누적형 성장을 선호한다.</summary>
    public int ChooseStageReward(BotContext ctx, StageRewardChoice choice) => choice.IndexOf(StageRewardOptionType.StatBoost);
}
