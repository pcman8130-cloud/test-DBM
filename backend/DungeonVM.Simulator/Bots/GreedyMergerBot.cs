using DungeonVM.Core.Models;

namespace DungeonVM.Simulator.Bots;

/// <summary>
/// 즉시 소비형 봇: 골드가 생기는 즉시 뽑기부터 소진하고, 자판기 업그레이드/그리드 해금은 거의 투자하지 않는다.
/// 정비 페이즈에서도 무기 종류를 가리지 않고 그리드의 "가장 높은 티어" 아이템을 바로 장착해버려 타입 전환이 잦다.
/// -> 골드가 항상 저티어 재뽑기에 흡수되어 동일 무기 스택이 잘 쌓이지 않고, 2티어 근처에서 정체되기 쉬운 프로필.
/// </summary>
public sealed class GreedyMergerBot : IBot
{
    public string Name => "GreedyMerger";

    public void OnCombatTick(BotContext ctx)
    {
        ctx.ApplyFreeActions();
        RollUntilBroke(ctx);
    }

    public void OnMaintenancePhase(BotContext ctx)
    {
        ctx.ApplyFreeActions();
        RollUntilBroke(ctx);

        foreach (var character in ctx.Party)
        {
            while (ctx.TryEquipFromGrid(character, w => character.EquippedWeapon is null || w.Tier > character.EquippedWeapon.Tier))
            {
                // 더 높은 티어가 그리드에 남아있는 한 종류 불문 계속 교체
            }
        }
    }

    private static void RollUntilBroke(BotContext ctx)
    {
        while (ctx.Currency.Gold >= VendingMachine.WeaponRollCost)
        {
            bool rollWeapon = ctx.Rng.NextDouble() < 0.8;
            bool success = rollWeapon ? ctx.TryRollWeapon() : ctx.TryRollArmor();
            if (!success) break;
        }
    }
}
