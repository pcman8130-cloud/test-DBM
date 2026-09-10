using DungeonVM.Core.Enums;
using DungeonVM.Core.Models;

namespace DungeonVM.Simulator.Bots;

/// <summary>
/// 저축 후 투자형 봇: 자판기 공격/방어 업그레이드와 그리드 해금을 최우선으로 저축하고, 남는 골드만 뽑기에 쓴다.
/// 정비 페이즈에서 캐릭터별로 한 번 정한 무기 종류를 고수해(타입 전환 없음) 동일 무기 스택을 꾸준히 쌓는다.
/// -> 뽑기 확률 자체를 끌어올린 상태에서 안정적으로 동종 무기를 모으므로 고티어 도달 빈도가 높은 프로필.
/// </summary>
public sealed class SaverUpgraderBot : IBot
{
    private readonly Dictionary<Guid, WeaponType> _committedType = new();

    public string Name => "SaverUpgrader";

    public void OnCombatTick(BotContext ctx)
    {
        ctx.ApplyFreeActions();
        InvestExcessGold(ctx, reserve: 40);
    }

    public void OnMaintenancePhase(BotContext ctx)
    {
        ctx.ApplyFreeActions();

        // 부트스트랩: 아직 무기가 없는 캐릭터가 있으면 업그레이드/해금 저축보다 첫 무기 확보가 우선이다.
        // (안 그러면 첫 정비 페이즈에 가진 골드를 전부 그리드 해금에 써버려 영원히 맨손으로 전투에 나가게 된다)
        while (ctx.Party.Any(c => c.EquippedWeapon is null))
        {
            EquipFromGridForUnarmed(ctx);
            if (ctx.Party.All(c => c.EquippedWeapon is not null)) break;
            if (!ctx.TryRollWeapon()) break;
            EquipFromGridForUnarmed(ctx);
        }

        InvestExcessGold(ctx, reserve: 0);

        foreach (var character in ctx.Party.Where(c => c.EquippedWeapon is not null))
        {
            var committed = _committedType.GetValueOrDefault(character.Id, character.EquippedWeapon!.Type);
            ctx.TryEquipFromGrid(character, w => w.Type == committed && w.Tier > character.EquippedWeapon!.Tier);
        }
    }

    private void EquipFromGridForUnarmed(BotContext ctx)
    {
        foreach (var character in ctx.Party.Where(c => c.EquippedWeapon is null))
        {
            if (ctx.TryEquipFromGrid(character))
                _committedType[character.Id] = character.EquippedWeapon!.Type;
        }
    }

    private void InvestExcessGold(BotContext ctx, int reserve)
    {
        // 1) 자판기 업그레이드 최우선
        while (ctx.Currency.Gold - reserve >= 0 && ctx.TryUpgradeAttack()) { }
        while (ctx.Currency.Gold - reserve >= 0 && ctx.TryUpgradeDefense()) { }

        // 2) 그리드 해금 (동시 보관 가능한 동일 무기 수를 늘려 고티어 머지를 지원)
        while (ctx.Currency.Gold - reserve >= 0 && ctx.TryUnlockGrid()) { }

        // 3) 남는 골드는 무기 위주로 순환(신경 써서 모은 종류 스택을 채우되, 방어구도 일부 병행)
        while (ctx.Currency.Gold - reserve >= VendingMachine.WeaponRollCost)
        {
            bool rollWeapon = ctx.Rng.NextDouble() < 0.75;
            bool success = rollWeapon ? ctx.TryRollWeapon() : ctx.TryRollArmor();
            if (!success) break;
        }
    }
}
