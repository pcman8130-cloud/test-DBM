using DungeonVM.Core.Balance;
using DungeonVM.Core.Enums;
using DungeonVM.Core.Models;

namespace DungeonVM.Core.Systems;

/// <summary>2중 재화(Gold/Souls)의 획득·소비를 관장한다. 룬은 구매가 아닌 스테이지/보스 드롭으로만 획득한다.</summary>
public sealed class CurrencyManager
{
    private static CurrencyBalanceSection Config => BalanceProvider.Current.Currency;

    public int Gold { get; private set; }
    public int Souls { get; private set; }

    public void Add(CurrencyType type, int amount)
    {
        switch (type)
        {
            case CurrencyType.Gold: Gold += amount; break;
            case CurrencyType.Souls: Souls += amount; break;
        }
    }

    public bool TrySpend(CurrencyType type, int amount)
    {
        switch (type)
        {
            case CurrencyType.Gold when Gold >= amount: Gold -= amount; return true;
            case CurrencyType.Souls when Souls >= amount: Souls -= amount; return true;
            default: return false;
        }
    }

    public static int WeaponMarketValue(Weapon w) => (int)(Config.WeaponMarketValueTierBase * WeaponCatalog.LevelMultiplierAt(w.Tier));

    public static int ArmorMarketValue(Armor a)
        => Config.ArmorMarketValues.TryGetValue(a.Rarity.ToString(), out var value) ? value : 0;

    /// <summary>보유 장비 판매(레시피 SellRefundRatio 비율만큼 환급).</summary>
    public int SellWeapon(Weapon w)
    {
        int refund = (int)(WeaponMarketValue(w) * Config.SellRefundRatio);
        Gold += refund;
        return refund;
    }

    public int SellArmor(Armor a)
    {
        int refund = (int)(ArmorMarketValue(a) * Config.SellRefundRatio);
        Gold += refund;
        return refund;
    }
}
