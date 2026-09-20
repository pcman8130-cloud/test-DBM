using DungeonVM.Core.Enums;
using DungeonVM.Core.Models;
using DungeonVM.Core.Systems;

namespace DungeonVM.Simulator.Bots;

/// <summary>
/// 중간 레벨 안주형 봇: 무기에 룬이 소켓되어 있고 티어가 안주 구간(5~7)이면 "룬 소멸" 손실을 피하기 위해
/// 의도적으로 병합을 건너뛴다. 룬은 스테이지/보스 드롭으로만 얻으므로 확보되는 대로 즉시 소켓하고,
/// 방어구 고등급 세팅에 집중 투자한다. 한 번 장착한 무기는 이후 다른 종류로 갈아타지 않는다
/// (스택 성장보다 현재 세팅 보존을 우선).
/// </summary>
public sealed class MidTierCampBot : IBot
{
    private const int CampZoneMinTier = 5;
    private const int CampZoneMaxTier = 7;

    public string Name => "MidTierCamp";

    public void OnCombatTick(BotContext ctx)
    {
        ctx.ApplyFreeActions(AllowMerge);
        while (ctx.TryRollWeapon()) { }

        foreach (var character in ctx.Party)
            ctx.TrySocketRuneOn(character);
    }

    public void OnMaintenancePhase(BotContext ctx)
    {
        ctx.ApplyFreeActions(AllowMerge);

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

        // 그리드가 꽉 찼을 때만 최소한으로 해금(공격적 확장은 하지 않음)
        if (ctx.Inventory.Grid.IsFull)
            ctx.TryUnlockGrid();

        while (ctx.TryRollWeapon()) { }
    }

    /// <summary>두 무기 중 하나라도 룬이 소켓되어 있고 그 티어가 안주 구간(5~7)이면 병합을 건너뛴다.</summary>
    private static bool AllowMerge(Weapon a, Weapon b)
    {
        bool hasRune = a.Element != ElementType.None || b.Element != ElementType.None;
        bool inCampZone = a.Tier is >= CampZoneMinTier and <= CampZoneMaxTier;
        return !(hasRune && inCampZone);
    }

    /// <summary>항상 상자 — 룬 확보 자체가 이 봇의 정체성(소켓해서 안주 구간을 방어)이므로 룬/유물 상자를 최우선한다.</summary>
    public int ChooseStageReward(BotContext ctx, StageRewardChoice choice) => choice.IndexOf(StageRewardOptionType.Box);
}
