using DungeonVM.Core.Enums;
using DungeonVM.Core.Models;
using DungeonVM.Core.Systems;

namespace DungeonVM.Simulator.Bots;

/// <summary>
/// 룬 수집형 봇: 룬은 스테이지 보상의 특수 상자로만 얻으므로 항상 특수 상자를 고르고, 확보되는 대로 즉시 소켓한다.
/// 방어구 고등급 세팅에 집중 투자하며, 한 번 장착한 무기는 이후 다른 종류로 갈아타지 않는다
/// (스택 성장보다 현재 세팅 보존을 우선). 룬이 머지 후에도 무기에 귀속되도록 규칙이 바뀌어서,
/// 예전처럼 "룬 소멸을 피하려고 머지를 건너뛰는" 행동은 더 이상 하지 않는다.
/// </summary>
public sealed class MidTierCampBot : IBot
{
    public string Name => "MidTierCamp";

    public void OnCombatTick(BotContext ctx)
    {
        ctx.ApplyFreeActions();
        while (ctx.TryRollWeapon()) { }

        foreach (var character in ctx.Party)
            ctx.TrySocketRuneOn(character);
    }

    public void OnMaintenancePhase(BotContext ctx)
    {
        ctx.ApplyFreeActions();

        // 부트스트랩: 첫 무기는 그리드에 있는 대로 장착하고, 이후로는 종류를 바꾸지 않는다.
        while (ctx.Party.Any(c => c.EquippedWeapon is null))
        {
            foreach (var character in ctx.Party.Where(c => c.EquippedWeapon is null))
                ctx.TryEquipFromGrid(character);

            if (ctx.Party.All(c => c.EquippedWeapon is not null)) break;
            if (!ctx.TryRollWeapon()) break;
        }

        // 1순위: 방어구 고등급 세팅
        while (ctx.TryRollArmor()) { }
        foreach (var character in ctx.Party)
            ctx.TryEquipArmorFromGrid(character);

        // 2순위: 드롭으로 확보된 룬을 아직 룬이 없는 무기에 즉시 소켓
        foreach (var character in ctx.Party)
            ctx.TrySocketRuneOn(character);

        // 그리드가 꽉 찼을 때만 최소한으로 해금(공격적 확장은 하지 않음). 그리드 확장 자체가 이 봇의
        // 목표가 아니라서 이걸 위해 재뽑기(무기 확보/룬 확보 기회)를 멈추고 저축하지는 않는다 —
        // 그렇게 해봤더니 승률이 오히려 떨어졌다(무기/룬 확보가 밀려서).
        if (ctx.Inventory.Grid.IsFull)
            ctx.TryUnlockGrid();

        while (ctx.TryRollWeapon()) { }
    }

    /// <summary>항상 특수 상자 — 룬 확보 자체가 이 봇의 정체성이므로 룬/유물 상자를 최우선한다.</summary>
    public int ChooseStageReward(BotContext ctx, StageRewardChoice choice) => choice.IndexOf(StageRewardOptionType.SpecialBox);

    public int ChooseBossRelic(BotContext ctx, IReadOnlyList<BossRelic> candidates) => 0;
}
