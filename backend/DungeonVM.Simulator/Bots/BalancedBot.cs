using DungeonVM.Core.Enums;
using DungeonVM.Core.Models;
using DungeonVM.Core.Systems;

namespace DungeonVM.Simulator.Bots;

/// <summary>
/// 균형형 봇: 기존 3봇(SpaceExpansion/VendingRush/MidTierCamp)은 각자 정체성상 한 시스템에
/// 완전히 몰빵하고 다른 시스템은 아예 건드리지 않는 "극단" 테스트용이었다(예: SpaceExpansion/MidTierCamp는
/// 자판기 강화를 단 한 번도 안 하고 캐릭터 슬롯도 절대 안 늘림). 실제 사람 플레이는 그렇지 않고
/// 자판기·인원·그리드·방어구에 골고루, 적당히 투자한다 — 이 봇은 그 "균형 잡힌" 플레이를 흉내낸다.
/// </summary>
public sealed class BalancedBot : IBot
{
    private readonly BalancedBotProfile _profile;

    /// <summary>기본 성향(밸런스 평가기 기준 봇).</summary>
    public BalancedBot() : this(BalancedBotProfile.Default) { }

    /// <summary>같은 경제 엔진에 저축 성향/상자 선택 정책만 바꿔 끼운 변형 봇.</summary>
    public BalancedBot(BalancedBotProfile profile) => _profile = profile;

    /// <summary>인원은 4명까지만 우선 투자(5번째는 비용 대비 효율이 낮아 뒤로 미룸).</summary>
    private const int PreferredMaxPartySize = 4;

    /// <summary>자판기 공격/방어 강화는 스테20 전까지는 Lv.3까지만 우선 투자한다(유저 본인의 실제 클리어
    /// 플레이도 무기 자판기 Lv.3에서 멈추고 나머지 골드는 인원/재뽑기로 돌렸다고 확인함). Lv.4~5는 400런
    /// 규모로 실측해보니 스테20 도달 시점까지도 경제 규모가 그 비용(각 700G)을 감당 못 해 자연스럽게 도달을
    /// 못 했는데, 그런데도 ShouldSaveFor가 "곧 모일 것"이라 보고 계속 저축 모드로 잡아둬서 무기 재뽑기(=합성
    /// 재료)가 막혀 있었다 — 그 결과 스테20 진입 시점 최고 무기 티어가 평균 5.2에서 정체되는 원인이었다.
    /// 인원 4명・그리드 만땅이 보통 스테15~20 사이에 끝나므로, 스테20부터는 자판기도 최대 레벨(5)까지 마저
    /// 밀어붙인다 — 그 시점부터는 다른 라운드로빈 목표가 거의 소진돼 있어 저축과도 충돌하지 않는다.</summary>
    private const int VendUpgradeSoftCap = 3;
    private const int VendUpgradeSoftCapUnlockStage = 20;

    private static int VendCap(BotContext ctx)
        => ctx.StageLoop.CurrentStage >= VendUpgradeSoftCapUnlockStage ? VendingMachine.MaxUpgradeLevel : VendUpgradeSoftCap;

    /// <summary>캐릭터별로 한 번 정한 무기 타입은 계속 유지한다(VendingRushBot과 동일 패턴). 타입을 안 가리고
    /// "더 높은 티어면 무조건 교체"하면 같은 타입+같은 티어가 쌓여야 하는 합성 체인이 계속 끊겨 무기가 안 큰다.</summary>
    private readonly Dictionary<Guid, WeaponType> _committedType = new();

    private static readonly HashSet<WeaponType> FrontRowTypes = new() { WeaponType.Sword, WeaponType.Shield, WeaponType.Dagger };

    public string Name => _profile.Name;

    public void OnCombatTick(BotContext ctx)
    {
        ctx.ApplyFreeActions();
        // 프로토타입은 rollWeapon() 안에서 매 뽑기 직후 tryAutoMerge()를 호출해 즉시 합성한다. 예전엔
        // 여기서 재뽑기를 몇 번이고 몰아서 하고 나서야 ApplyFreeActions(자동합성)를 한 번 부르는 구조라,
        // 한 틱에 골드가 남아 재뽑기 5~10번이 몰리면 그리드(16칸)가 합성 없이 그대로 채워지다 넘쳐서
        // 절반 가까이가 병합 기회도 없이 강제 판매됐다(스테20 시점 최고 티어가 5~6에서 정체된 원인 중 하나).
        if (!ShouldSaveForNextGoal(ctx))
            RollWhilePossible(ctx);
    }

    public void OnMaintenancePhase(BotContext ctx)
    {
        ctx.ApplyFreeActions();
        CullGrid(ctx);

        // 전열 탱커 고정: FormationManager.SelectMonsterTarget은 파티 리스트 순서상 "가장 먼저 나오는 전열
        // 캐릭터"를 몬스터의 고정 타겟으로 삼는다(프로토타입도 동일 로직). Party[0]이 사실상 항상 그 자리다.
        // 방패는 방어력 기여(BonusHealth 60)가 다른 전열 무기(검20/단검10)의 3~6배라 이 자리에 최적이므로,
        // 그냥 아무 무기나 먼저 뽑히는 대로 맡기지 않고 Party[0]에게는 방패를 우선 배정한다.
        EquipTankShield(ctx);

        // 전열 백업: 탱커가 죽으면 곧장 다음 전열 캐릭터(있다면)로 타겟이 넘어간다. 전열이 방패 한 명뿐이면
        // 탱커가 죽는 순간 바로 약한 후열 캐릭터가 얻어맞기 시작한다 — 전열을 최소 2명으로 유지한다.
        EquipSecondFrontliner(ctx);

        // 부트스트랩: 맨몸 캐릭터는 최소 한 자루는 쥐어준다.
        while (ctx.Party.Any(c => c.EquippedWeapon is null))
        {
            EquipUnarmed(ctx);
            if (ctx.Party.All(c => c.EquippedWeapon is not null)) break;
            if (!ctx.TryRollWeapon(AlwaysSellSet())) break;
        }
        EquipUnarmed(ctx);

        // 뒤처진 무기 타입 포기: 다른 파티원보다 3티어 이상 뒤처진 타입은 판매하고 재배정받는다
        // (타입을 계속 넓게 벌리는 대신 성공적인 소수 타입에 자원을 집중시키기 위함).
        PruneLaggingWeaponTypes(ctx);
        while (ctx.Party.Any(c => c.EquippedWeapon is null))
        {
            EquipUnarmed(ctx);
            if (ctx.Party.All(c => c.EquippedWeapon is not null)) break;
            if (!ctx.TryRollWeapon(AlwaysSellSet())) break;
        }
        EquipUnarmed(ctx);

        // 최소 방어 투자: 맨몸 방어구 캐릭터는 최소 한 벌은 챙긴다.
        EquipMinimalArmor(ctx);

        // 숙련자 오프닝: 첫 정비에서 시작 골드로 무기를 정해진 개수까지 뽑아 둔다(남는 한 자루는 곧 열 3번째 슬롯용).
        if (_profile.OpeningWeapons > 0 && ctx.StageLoop.CurrentStage == 1)
            while (OwnedWeaponCount(ctx) < _profile.OpeningWeapons && ctx.TryRollWeapon(AlwaysSellSet())) { }

        // 인원 확보를 최우선으로: 4번째 슬롯(700G)이 공격/방어 업그레이드 3티어(각 700G)와 정확히 같은
        // 가격대라 "제일 싼 것부터" 방식으로는 둘 다 먼저 걸린 공격/방어에 매번 밀려서 인원이 영영 안 늘었다
        // (실측: 800런 전부 스테15까지 파티가 3명 고정). 사람이 실제로 하는 것처럼(유저 본인도 인원부터
        // 채웠다고 확인) 인원 목표(4명)를 채울 때까지는 슬롯 구매를 다른 무엇보다 먼저 시도한다.
        if (_profile.Plan is not null) RunPlan(ctx);
        ExpandGridIfStuck(ctx);
        bool planActive = _profile.Plan is not null && CurrentGoal(ctx) is not null;
        if (!planActive)
            while (ctx.Party.Count < PreferredMaxPartySize && ctx.TryUnlockCharacterSlot()) { }

        // 인원이 다 찼거나 더는 못 사면, 남은 자판기 공격/방어 업그레이드·그리드 해금은 "지금 제일 싼 것"부터
        // 돌아가며 산다 — 한쪽에 올인하지 않고 여러 시스템에 골고루 투자하기 위함.
        while (!planActive)
        {
            int vendCap = VendCap(ctx);
            var options = new List<(int Cost, Func<bool> Buy)>();
            if (ctx.Machine.AttackUpgradeLevel < vendCap && ctx.Machine.CanUpgradeAttack) options.Add((ctx.Machine.AttackUpgradeCost, ctx.TryUpgradeAttack));
            if (ctx.Machine.DefenseUpgradeLevel < vendCap && ctx.Machine.CanUpgradeDefense) options.Add((ctx.Machine.DefenseUpgradeCost, ctx.TryUpgradeDefense));
            if (!ctx.Inventory.Grid.IsFullyUnlocked)
                options.Add((ctx.Inventory.Grid.NextUnlockGoldCost(), ctx.TryUnlockGrid));

            if (options.Count == 0) break;
            var cheapest = options.OrderBy(o => o.Cost).First();
            if (!cheapest.Buy()) break; // 가장 싼 것도 못 사면 나머지도 다 못 산다
        }

        EquipUnarmed(ctx);
        EquipMinimalArmor(ctx);

        // 남는 골드는 무기 재뽑기로. 다음 라운드로빈 목표가 1~2스테 수입으로 곧 감당 가능하면 저축.
        // 프로토타입처럼 뽑을 때마다 즉시 자동합성해서, 그리드(16칸)가 합성 없이 쌓이다 넘쳐 강제 판매되는
        // 낭비를 막는다.
        if (!ShouldSaveForNextGoal(ctx))
            RollWhilePossible(ctx);

        foreach (var character in ctx.Party)
        {
            WeaponType? committed = _committedType.TryGetValue(character.Id, out var t) ? t : character.EquippedWeapon?.Type;
            ctx.TryEquipFromGrid(character, w => !IsAlwaysSold(w.Type) && (committed is null || w.Type == committed) && (character.EquippedWeapon is null || w.Tier > character.EquippedWeapon.Tier));
            ctx.TryEquipArmorFromGrid(character);
        }
    }

    /// <summary>파티 전원이 이미 무기 타입을 정했다면(맨몸 캐릭터가 없으면), 그 committed 타입들 외의
    /// 나머지는 이 파티에서 절대 쓸 일이 없다(인원 5명이어도 무기 6종이라 최소 1종은 항상 남음).
    /// 그리드만 차지하다 환급 없이 강제 폐기되느니 뽑히는 즉시 판매하는 편이 낫다.
    /// 아직 구성 중(맨몸 캐릭터 존재)이거나 탱커(Party[0])가 아직 방패를 못 구했으면 방패는 절대 배제하지
    /// 않는다 — 안 그러면 "아무도 아직 방패를 안 썼으니 배제 대상"이 되어 방패가 그리드에 쌓일 기회 자체가
    /// 없어져 EquipTankShield가 영원히 방패를 못 찾는 모순이 생긴다.</summary>
    /// <summary>파티 전원이 무장하고 전열 2명(그중 탱커는 방패)까지 갖추고 나면, 그 뒤로는 이미 커밋한
    /// 타입 외에는 전부 배제한다. 예전엔 이 조건이 갖춰지기 "전"까지는 6종 전체를 무제한으로 굴렸는데,
    /// 그 동안 이미 파티원 수만큼(보통 3~4종) 서로 다른 타입이 흩어져 커밋되어 버려서, 합성 재료(같은
    /// 타입+같은 티어 짝)가 타입별로 너무 얇게 나뉘어 스테20 시점에도 최고 티어가 5~6에서 정체됐다
    /// (재뽑기 양을 아무리 늘려도 동일 — 이미 흩어진 뒤라 배제 규칙 자체가 손을 못 씀). 이제는 방패
    /// 확보 전까지만 방패를 배제 대상에서 빼주고, 그 외엔 이미 정해진 소수 타입으로 처음부터 좁힌다.</summary>
    private HashSet<WeaponType> UnwantedWeaponTypes(BotContext ctx)
    {
        var set = UnwantedWeaponTypesCore(ctx);
        if (_profile.AlwaysSell is { } sold)
            foreach (var t in sold) set.Add(t);
        return set;
    }

    private bool IsAlwaysSold(WeaponType type) => _profile.AlwaysSell?.Contains(type) == true;

    /// <summary>뽑자마자 팔 종류만 담은 집합(없으면 null — 기존 동작 그대로).</summary>
    private HashSet<WeaponType>? AlwaysSellSet()
        => _profile.AlwaysSell is { Count: > 0 } sold ? new HashSet<WeaponType>(sold) : null;

    /// <summary>골드가 있는 한 무기를 계속 뽑는다. 뽑을 때마다 합성하고 정리하며, MakeRoomWhenFull이면 가득 찼을 때 칸을 만들어 본다.</summary>
    private void RollWhilePossible(BotContext ctx)
    {
        while (true)
        {
            if (ctx.Inventory.Grid.IsFull)
            {
                if (_profile.MakeRoomWhenFull) MakeRoom(ctx);
                if (ctx.Inventory.Grid.IsFull)
                {
                    ctx.TryRollWeapon(UnwantedWeaponTypes(ctx)); // 막힌 뽑기를 한 번 시도해 그리드 병목 횟수에 집계(골드는 안 나감)
                    return;
                }
            }
            if (!ctx.TryRollWeapon(UnwantedWeaponTypes(ctx))) return;
            ctx.ApplyFreeActions();
            CullGrid(ctx);
        }
    }

    /// <summary>가득 찬 그리드에서 합성 짝이 없고 파티 최저 무기 레벨 이하인(앞으로 쓸 일이 없는) 무기 중 가장 낮은 것을 판다.
    /// 지금 장착 중인 무기와 같은 종류·같은 레벨(전투 중 동종 강화 재료)은 건드리지 않는다.</summary>
    private void MakeRoom(BotContext ctx)
    {
        var armed = ctx.Party.Where(c => c.EquippedWeapon is not null).Select(c => c.EquippedWeapon!).ToList();
        if (armed.Count == 0) return;
        int floor = armed.Min(w => w.Tier);

        while (ctx.Inventory.Grid.IsFull)
        {
            var all = ctx.Inventory.Grid.OccupiedWeapons().ToList();
            var candidate = all
                .Where(o => o.Weapon.Tier <= floor)
                .Where(o => !armed.Any(a => a.Type == o.Weapon.Type && a.Tier == o.Weapon.Tier))
                .Where(o => !all.Any(x => x.Index != o.Index && x.Weapon.Type == o.Weapon.Type && x.Weapon.Tier == o.Weapon.Tier))
                .OrderBy(o => o.Weapon.Tier)
                .Select(o => ((int Index, Weapon Weapon)?)o)
                .FirstOrDefault();
            if (candidate is not { } c) return;

            ctx.Inventory.Grid.RemoveAt(c.Index);
            ctx.Currency.SellWeapon(c.Weapon);
        }
    }

    /// <summary>MakeRoomWhenFull 프로필 전용: 그리드가 가득 차 뽑기가 막혔는데 골드가 남으면(사람이 "보관함 확장"을 누르는 상황)
    /// 다음 칸 블록을 연다. 계획이 구매를 위해 저축 중일 땐(구매형 단계) 건드리지 않는다.</summary>
    private void ExpandGridIfStuck(BotContext ctx)
    {
        if (!_profile.MakeRoomWhenFull || !ctx.Inventory.Grid.IsFull || ctx.Inventory.Grid.IsFullyUnlocked) return;
        if (_profile.Plan is not null && CurrentGoal(ctx) is { } g && g.Kind != PlanGoalKind.AllWeaponTier) return;
        if (ctx.Currency.Gold >= ctx.Inventory.Grid.NextUnlockGoldCost())
            ctx.TryUnlockGrid();
    }

    /// <summary>그리드 정리(사람 습관 모사): 항상 파는 종류는 레벨 무관하게 팔고, 무기 자판기가 CullVendLevel 이상이면
    /// 합성 짝(같은 종류·같은 레벨)이 없는 CullMaxLevel 이하 무기를 판다. 파티 전원이 무장하기 전에는 정리하지 않는다
    /// (맨몸 캐릭터에게 쥐어줄 무기가 필요하다).</summary>
    private void CullGrid(BotContext ctx)
    {
        bool anyRule = _profile.AlwaysSell is { Count: > 0 } || _profile.CullVendLevel > 0;
        if (!anyRule) return;
        if (ctx.Party.Any(c => c.EquippedWeapon is null)) return;

        bool cullLow = _profile.CullVendLevel > 0 && ctx.Machine.AttackUpgradeLevel >= _profile.CullVendLevel;
        foreach (var (index, weapon) in ctx.Inventory.Grid.OccupiedWeapons().ToList())
        {
            bool sell = IsAlwaysSold(weapon.Type);
            if (!sell && cullLow && weapon.Tier <= _profile.CullMaxLevel)
            {
                // 합성 짝이 이미 있으면 남겨 둔다(자동합성이 곧 합쳐 줌 — 같은 종류·같은 레벨이 한 장 더 있는 경우).
                bool hasPair = ctx.Inventory.Grid.OccupiedWeapons().Any(o => o.Index != index && o.Weapon.Type == weapon.Type && o.Weapon.Tier == weapon.Tier);
                sell = !hasPair;
            }
            if (!sell) continue;

            ctx.Inventory.Grid.RemoveAt(index);
            ctx.Currency.SellWeapon(weapon);
        }
    }

    private HashSet<WeaponType> UnwantedWeaponTypesCore(BotContext ctx)
    {
        bool tankHasShield = ctx.Party.Count > 0 && ctx.Party[0].EquippedWeapon?.Type == WeaponType.Shield;

        var protectedTypes = new HashSet<WeaponType>(_committedType.Values);
        if (!tankHasShield) protectedTypes.Add(WeaponType.Shield);

        // 전열 확보용 타입(검/방패/단검) 중 아직 하나도 커밋 안 됐으면 전열이 생길 때까지 넓게 굴린다.
        if (!protectedTypes.Overlaps(FrontRowTypes)) return new HashSet<WeaponType>();

        return Enum.GetValues(typeof(WeaponType)).Cast<WeaponType>().Where(t => !protectedTypes.Contains(t)).ToHashSet();
    }

    /// <summary>파티원 중 가장 높은 무기 티어보다 3티어 이상 뒤처진 캐릭터는 그 타입 투자를 포기한다 —
    /// 판매하고 커밋을 해제해서 다음 롤에 새 타입(다른 파티원이 이미 잘 키운 타입일 수도 있음)으로
    /// 재배정받게 한다. 타입을 넓게 쫙 벌리는 대신 성공적인 소수 타입에 합성 자원을 몰아주기 위함.</summary>
    private void PruneLaggingWeaponTypes(BotContext ctx)
    {
        var equipped = ctx.Party.Where(c => c.EquippedWeapon is not null).ToList();
        if (equipped.Count < 2) return;

        int maxTier = equipped.Max(c => c.EquippedWeapon!.Tier);
        foreach (var c in equipped)
        {
            if (maxTier - c.EquippedWeapon!.Tier < 3) continue;

            ctx.Currency.SellWeapon(c.EquippedWeapon);
            c.UnequipWeapon();
            _committedType.Remove(c.Id);
        }
    }

    /// <summary>Party[0]은 사실상 고정 탱커 자리라 방패가 그리드에 있으면 최우선으로 넘겨준다.
    /// 방패를 노리고 별도로 강제 굴리지는 않는다(그러면 초반 골드가 거기 묶여 다른 캐릭터 무장이 늦어짐 —
    /// 실제로 테스트해보니 스테5 승률이 크게 떨어졌다). 대신 UnwantedWeaponTypes가 탱커에게 방패가
    /// 생기기 전까지는 방패를 배제 대상에서 빼놓아서, 평소 굴리던 무기들 중에 방패가 섞여 나오면
    /// 그리드에 쌓이게 하고 그걸 여기서 집어간다.</summary>
    private void EquipTankShield(BotContext ctx)
    {
        if (ctx.Party.Count == 0) return;
        var tank = ctx.Party[0];
        if (tank.EquippedWeapon?.Type == WeaponType.Shield) return;

        if (ctx.TryEquipFromGrid(tank, w => w.Type == WeaponType.Shield))
            _committedType[tank.Id] = WeaponType.Shield;
    }

    /// <summary>전열이 아직 2명이 안 되면, 투자가 별로 안 된(3티어 이하 또는 맨몸) 캐릭터 한 명을 전열
    /// 타입으로 돌린다. 그리드에 전열 타입이 있을 때만(강제로 굴리지 않음 — 방패와 같은 이유) 동작하고,
    /// 이미 많이 큰(4티어+) 캐릭터의 투자는 건드리지 않는다.</summary>
    private void EquipSecondFrontliner(BotContext ctx)
    {
        int frontCount = ctx.Party.Count(c => c.EquippedWeapon is { } w && FrontRowTypes.Contains(w.Type));
        if (frontCount >= 2) return;

        foreach (var character in ctx.Party.Skip(1))
        {
            if (character.EquippedWeapon is { } cw)
            {
                if (FrontRowTypes.Contains(cw.Type)) continue; // 이미 전열
                if (cw.Tier > 2) continue; // 이미 투자가 진행된 캐릭터는 건드리지 않음
            }

            if (ctx.TryEquipFromGrid(character, w => FrontRowTypes.Contains(w.Type)))
            {
                _committedType[character.Id] = character.EquippedWeapon!.Type;
                return; // 한 번에 한 명만 전환
            }
        }
    }

    /// <summary>맨몸 캐릭터에게 그리드에서 무기를 배정한다. 이미 다른 파티원이 쓰고 있는 타입이 그리드에
    /// 있으면 그걸 우선 배정한다 — 타입을 넓게 벌릴수록 합성 재료(같은 타입+같은 티어 짝)가 여러 타입에
    /// 얇게 흩어져서 고티어까지 못 올라간다(실측: 스테20 진입 시점 최고 티어가 5~6에서 정체). 인원마다
    /// 다른 타입을 하나씩 배정하는 대신 소수 타입에 몰아줘야 합성 확률이 올라간다.</summary>
    private void EquipUnarmed(BotContext ctx)
    {
        foreach (var character in ctx.Party.Where(c => c.EquippedWeapon is null))
        {
            var alreadyCommitted = new HashSet<WeaponType>(_committedType.Values);
            if (alreadyCommitted.Count > 0 && ctx.TryEquipFromGrid(character, w => alreadyCommitted.Contains(w.Type)))
            {
                _committedType[character.Id] = character.EquippedWeapon!.Type;
                continue;
            }

            if (ctx.TryEquipFromGrid(character, w => !IsAlwaysSold(w.Type)))
                _committedType[character.Id] = character.EquippedWeapon!.Type;
        }
    }

    private static void EquipMinimalArmor(BotContext ctx)
    {
        foreach (var character in ctx.Party.Where(c => c.EquippedWeapon is not null && c.EquippedArmor is null))
        {
            if (ctx.TryEquipArmorFromGrid(character)) continue;
            if (ctx.TryRollArmor())
                ctx.TryEquipArmorFromGrid(character);
        }
    }

    /// <summary>라운드로빈이 다음으로 노리는 목표 중 가장 저렴한 것의 비용(더 살 게 없으면 null). 그리드 해금은
    /// 포함하지 않는다 — 100~500G로 워낙 자주 저렴하게 열려서 이것까지 저축 대상에 넣으면 "항상 뭔가 거의
    /// 살 수 있는" 상태가 되어 재뽑기가 게임 내내 거의 멈춰버린다(그리드는 라운드로빈 루프 자체가 알아서 산다).</summary>
    private static int? NextGoalCost(BotContext ctx)
    {
        int vendCap = VendCap(ctx);
        var costs = new List<int>();
        if (ctx.Machine.AttackUpgradeLevel < vendCap && ctx.Machine.CanUpgradeAttack) costs.Add(ctx.Machine.AttackUpgradeCost);
        if (ctx.Machine.DefenseUpgradeLevel < vendCap && ctx.Machine.CanUpgradeDefense) costs.Add(ctx.Machine.DefenseUpgradeCost);
        if (ctx.Party.Count < PreferredMaxPartySize && ctx.StageLoop.CanUnlockCharacterSlot) costs.Add(ctx.StageLoop.NextCharacterSlotGoldCost());
        return costs.Count > 0 ? costs.Min() : null;
    }

    /// <summary>기본 ShouldSaveFor 창(2스테)은 자판기 업그레이드(150~1300G)엔 맞지만 캐릭터 슬롯 4번째
    /// (700G)엔 턱없이 짧다 — 이 시점 스테이지당 수입은 100~150G 남짓이라 2스테 수입으로는 700G의
    /// 절반도 안 돼 "저축해도 못 모은다"고 판단해 포기하고 매 틱 재뽑기로 다 태워버린다(실측: 스테15까지
    /// 800런 전부 파티 3명 고정, 평균 잔여 골드 9). 창을 6스테로 넓혀서 비싼 목표도 실제로 저축 대상이
    /// 되게 한다.</summary>
    private bool ShouldSaveForNextGoal(BotContext ctx)
    {
        if (_profile.SaveStagesAhead <= 0) return false;

        int? cost;
        if (_profile.Plan is not null && CurrentGoal(ctx) is { } planGoal)
        {
            cost = PlanGoalCost(ctx, planGoal);
            if (_profile.HoldGold && cost is not null) return true; // 목표 금액을 다 모을 때까지 재뽑기 금지
        }
        else cost = NextGoalCost(ctx);

        return cost is { } c && ctx.ShouldSaveFor(c, stagesAhead: _profile.SaveStagesAhead);
    }

    /// <summary>계획에서 아직 안 끝난 첫 단계(없으면 null = 계획 완료). GiveUpStage가 지난 단계는 끝난 것으로 본다.</summary>
    private PlanGoal? CurrentGoal(BotContext ctx)
        => _profile.Plan?.FirstOrDefault(g => ctx.StageLoop.CurrentStage < g.GiveUpStage && !IsMet(ctx, g));

    private static bool IsMet(BotContext ctx, PlanGoal g) => g.Kind switch
    {
        PlanGoalKind.PartySize => ctx.Party.Count >= g.Target || !ctx.StageLoop.CanUnlockCharacterSlot,
        PlanGoalKind.AttackLevel => ctx.Machine.AttackUpgradeLevel >= g.Target || !ctx.Machine.CanUpgradeAttack,
        PlanGoalKind.GridUnlocks => ctx.Inventory.Grid.UnlockedCells >= GridStartCells * (1 + g.Target) || ctx.Inventory.Grid.IsFullyUnlocked,
        PlanGoalKind.AllWeaponTier => ctx.Party.All(c => c.EquippedWeapon is { } w && w.Tier >= g.Target),
        _ => true,
    };

    private const int GridStartCells = 4;

    private static int OwnedWeaponCount(BotContext ctx)
        => ctx.Party.Count(c => c.EquippedWeapon is not null) + ctx.Inventory.Grid.OccupiedWeapons().Count();

    /// <summary>구매형 목표의 비용. 마일스톤(무기 티어)은 저축할 대상이 아니라 골드를 뽑기에 쓰는 단계라 null.</summary>
    private static int? PlanGoalCost(BotContext ctx, PlanGoal g) => g.Kind switch
    {
        PlanGoalKind.PartySize => ctx.StageLoop.NextCharacterSlotGoldCost(),
        PlanGoalKind.AttackLevel => ctx.Machine.AttackUpgradeCost,
        PlanGoalKind.GridUnlocks => ctx.Inventory.Grid.NextUnlockGoldCost(),
        _ => null,
    };

    /// <summary>계획의 현재 단계가 구매형이면 살 수 있는 만큼 사 가며 앞으로 나아간다. 마일스톤 단계에서는 멈춘다.</summary>
    private void RunPlan(BotContext ctx)
    {
        while (CurrentGoal(ctx) is { } g)
        {
            bool bought = g.Kind switch
            {
                PlanGoalKind.PartySize => ctx.TryUnlockCharacterSlot(),
                PlanGoalKind.AttackLevel => ctx.TryUpgradeAttack(),
                PlanGoalKind.GridUnlocks => ctx.TryUnlockGrid(),
                _ => false,
            };
            if (!bought) break;
        }
    }

    /// <summary>선택보상이 3개 상자(장비/특수/능력치)로 바뀌면서 골드 선택지는 없어졌다. 인원이 목표치(4명)에
    /// 도달하기 전까지는 새 캐릭터에게 줄 장비가 필요하므로 장비 상자를, 그 이후로는 공격력%/체력/유틸을 주는
    /// 능력치 상자를 고른다(보스전은 단일 대상 DPS 경쟁이라 누적 스탯이 중요하다는 기존 판단을 그대로 유지).</summary>
    public int ChooseStageReward(BotContext ctx, StageRewardChoice choice) => _profile.Reward(ctx, choice);

    public int ChooseRewardCandidate(BotContext ctx, StageRewardReveal reveal) => RewardCandidatePolicies.Default(ctx, reveal, IsAlwaysSold);

    /// <summary>보스 유물 선택 우선순위(스테이지마다 후보가 달라 한 목록으로 이어 붙였다). 새 유물 세트(공격/방어/특수)에서
    /// 무난하게 강한 쪽을 앞에 둔 것으로, 정밀한 실측 순위는 아니다 — 유물별 실제 격차는 평가기의 "유물 격차" 지표(relicBalance)로 본다.</summary>
    private static readonly List<string> RelicPreferenceOrder = new()
    {
        "command_flag", "chief_epaulette", "goblin_wallet",       // 5스테
        "gauntlet", "ogre_blood", "orc_shaman",                   // 10스테
        "mystic_cloak", "flame_tail", "salamander_claw",          // 15스테
        "ancient_guardian", "rune_tablet", "ancient_coin",        // 20스테
        "scale_wing", "dragon_orb",                               // 25스테
        "crimson_grail",                                          // 30스테
    };

    /// <summary>고대 주화로 막을 무기: 이 봇이 항상 파는 종류(지팡이)가 있으면 그것, 없으면 기본 규칙.</summary>
    public WeaponType ChooseBannedWeapon(BotContext ctx)
        => _profile.AlwaysSell is { Count: > 0 } sold ? sold[0] : RewardCandidatePolicies.DefaultBannedWeapon(ctx);

    public int ChooseBossRelic(BotContext ctx, IReadOnlyList<BossRelic> candidates)
    {
        foreach (var preferredId in RelicPreferenceOrder)
        {
            int idx = candidates.ToList().FindIndex(r => r.Id == preferredId);
            if (idx >= 0) return idx;
        }
        return 0;
    }
}
