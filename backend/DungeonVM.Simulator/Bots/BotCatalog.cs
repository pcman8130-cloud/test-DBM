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
        "RandomBoxes" => new BalancedBot(BalancedBotProfile.RandomBoxes),
        "RandomAll" => new BalancedBot(BalancedBotProfile.RandomAll),
        "StatOnly" => new BalancedBot(BalancedBotProfile.StatOnly),
        "SpecialOnly" => new BalancedBot(BalancedBotProfile.SpecialOnly),
        "Population" => new PopulationBot(),
        "ArmorEpic" => new BalancedBot(BalancedBotProfile.ArmorEpic),
        "ArmorLegend" => new BalancedBot(BalancedBotProfile.ArmorLegend),
        "ExpertArmorEpic" => new BalancedBot(BalancedBotProfile.ExpertArmorEpic),
        "ExpertArmorLegend" => new BalancedBot(BalancedBotProfile.ExpertArmorLegend),
        "Newbie" => new BalancedBot(BalancedBotProfile.Newbie),
        "Expert" => new BalancedBot(BalancedBotProfile.Expert),
        "ExpertWeapon" => new BalancedBot(BalancedBotProfile.ExpertWeapon),
        "SpaceExpansion" => new SpaceExpansionBot(),
        "VendingRush" => new VendingRushBot(),
        "MidTierCamp" => new MidTierCampBot(),
        _ => throw new ArgumentException($"알 수 없는 봇 이름: {name} (Balanced/Hoarder/Spender/BossPrep/RandomBoxes/RandomAll/StatOnly/SpecialOnly/Newbie/Expert/ExpertWeapon/SpaceExpansion/VendingRush/MidTierCamp 중 하나)"),
    };
}
