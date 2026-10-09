namespace DungeonVM.Simulator.Bots;

/// <summary>이름으로 봇을 만든다. 밸런스 목표 파일(BalanceTargets.json)의 playstyles.bots가 이 이름을 참조한다.</summary>
public static class BotCatalog
{
    public static IBot Create(string name) => name switch
    {
        "Balanced" => new BalancedBot(),
        "Hoarder" => new BalancedBot(BalancedBotProfile.Hoarder),
        "Spender" => new BalancedBot(BalancedBotProfile.Spender),
        "BossPrep" => new BalancedBot(BalancedBotProfile.BossPrep),
        "Expert" => new BalancedBot(BalancedBotProfile.Expert),
        "ExpertWeapon" => new BalancedBot(BalancedBotProfile.ExpertWeapon),
        "SpaceExpansion" => new SpaceExpansionBot(),
        "VendingRush" => new VendingRushBot(),
        "MidTierCamp" => new MidTierCampBot(),
        _ => throw new ArgumentException($"알 수 없는 봇 이름: {name} (Balanced/Hoarder/Spender/BossPrep/Expert/ExpertWeapon/SpaceExpansion/VendingRush/MidTierCamp 중 하나)"),
    };
}
