using DungeonVM.Core.Enums;
using DungeonVM.Core.Models;

namespace DungeonVM.Simulator.Bots;

/// <summary>
/// 균형 최적화형 봇: 스테이지 진행도에 따라 투자 비중을 동적으로 조절하는 "이상적 유저" 벤치마크.
/// 초반에는 자판기 업그레이드에 투자해 고티어 등장 확률을 끌어올리고, 이후에는 확보한 골드 여유로
/// 그리드 해금과 뽑기를 병행하며 위탁형 무기 종류를 유지해 안정적으로 고티어까지 성장시킨다.
/// </summary>
public sealed class BalancedOptimizerBot : IBot
{
    private readonly Dictionary<Guid, WeaponType> _committedType = new();

    public string Name => "BalancedOptimizer";

    public void OnCombatTick(BotContext ctx)
    {
        ctx.ApplyFreeActions();

        // 전투 중에는 여유 골드의 절반 정도만 즉시 재투입(변동성 완화)
        int reserve = ctx.Currency.Gold / 2;
        while (ctx.Currency.Gold - reserve >= VendingMachine.WeaponRollCost && ctx.TryRollWeapon()) { }
    }

    public void OnMaintenancePhase(BotContext ctx)
    {
        ctx.ApplyFreeActions();

        bool early = ctx.StageLoop.CurrentStage <= 10;

        if (early)
        {
            // 초반: 업그레이드 우선 투자로 확률 기반 체질 개선
            if (!ctx.TryUpgradeAttack() && ctx.Currency.Gold >= VendingMachine.WeaponRollCost * 2)
                ctx.TryRollWeapon();
        }
        else
        {
            // 중후반: 그리드 해금 + 방어 업그레이드 + 남는 골드는 뽑기로 순환
            if (!ctx.TryUnlockGrid())
                ctx.TryUpgradeDefense();

            while (ctx.Currency.Gold >= VendingMachine.WeaponRollCost)
            {
                bool rollWeapon = ctx.Rng.NextDouble() < 0.7;
                bool success = rollWeapon ? ctx.TryRollWeapon() : ctx.TryRollArmor();
                if (!success) break;
            }
        }

        foreach (var character in ctx.Party)
        {
            if (character.EquippedWeapon is null)
            {
                if (ctx.TryEquipFromGrid(character))
                    _committedType[character.Id] = character.EquippedWeapon!.Type;
            }
            else
            {
                var committed = _committedType.GetValueOrDefault(character.Id, character.EquippedWeapon.Type);
                ctx.TryEquipFromGrid(character, w => w.Type == committed && w.Tier > character.EquippedWeapon!.Tier);
            }
        }
    }
}
