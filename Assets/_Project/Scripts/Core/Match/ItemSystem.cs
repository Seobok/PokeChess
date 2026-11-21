using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Battle;
using PokeChess.Core.Pokemon;
using PokeChess.Core.Random;

namespace PokeChess.Core.Match
{
    public sealed class ItemDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public string Role { get; }
        public bool IsComponent { get; }
        public string FirstComponent { get; }
        public string SecondComponent { get; }
        public string BasicEffect
        {
            get
            {
                var values = new List<string>();
                if (Stats.attack != 0) values.Add("공격력 +" + Stats.attack + "%");
                if (Stats.armor != 0) values.Add("방어력 +" + Stats.armor);
                if (Stats.power != 0) values.Add("주문력 +" + Stats.power);
                if (Stats.resistance != 0) values.Add("마법 저항력 +" + Stats.resistance);
                if (Stats.speed != 0) values.Add("공격속도 +" + Stats.speed + "%");
                if (Stats.crit != 0) values.Add("치명타 확률 +" + Stats.crit + "%p");
                if (Stats.hp != 0) values.Add("최대 HP +" + Stats.hp);
                if (Stats.energy != 0) values.Add("시작 에너지 +" + Stats.energy);
                return values.Count == 0 ? "없음" : string.Join(" · ", values);
            }
        }

        // Keep proc conditions with their effect; a duration alone is not a cooldown.
        public string CooldownAbility => Id switch
        {
            LeftoversId => Description,
            "exp-share" => Description,
            "crafted-punching-glove" => "기본 공격 3회 적중 시 최대 HP의 12% 보호막을 4초간 얻는다. 각 아이템의 재발동 대기시간은 6초.",
            "crafted-muscle-band" => "특수 피해를 받으면 4초간 각 아이템의 추가 피해가 공격력의 24%가 된다(아이템별 재발동 8초).",
            "crafted-expert-belt" => "치명타 적중 시 대상에게 3초간 약점 노출(받는 피해 +8%)을 부여한다(동일 대상·아이템별 재발동 3초).",
            "crafted-focus-band" => "피격 4회마다 최대 HP 12% 보호막(4초)을 획득하고, 다음 기본 공격 1회를 확정 치명타로 만든다(아이템별 재발동 8초).",
            "crafted-covert-cloak" => "각 아이템은 전투 시작 시 디버프 차단 1회 충전. 적의 디버프 1개를 무효화하면 6초 후 다시 충전한다.",
            "crafted-adrenaline-orb" => "특수 피해를 받으면 공격속도 +5%(5초, 아이템당 최대 5중첩). 동일 아이템 재발동 간격 0.5초.",
            "crafted-clear-amulet" => "스킬 사용 후 6초 내 다음 기본 공격 1회가 확정 치명타가 된다(아이템당 재발동 6초).",
            "crafted-big-root" => "스킬 시전 시 최대 HP 4% 회복(아이템당 재발동 5초).",
            _ => "없음"
        };
        private const string LeftoversId = "leftovers";

        public string AdditionalEffect
        {
            get
            {
                if (IsComponent) return Id == ItemCatalog.Everstone ? Description : "없음";
                string effect = System.Text.RegularExpressions.Regex.Replace(Description,
                    @"^(?:(?:AD|AP|방어|마저|공속|치확|HP|시작 E)\s*\+\d+(?:%p|%)?\s*)+", "").Trim();
                if (CooldownAbility != "없음") effect = effect.Replace(CooldownAbility, "").Trim();
                return effect.Length == 0 ? "없음" : effect;
            }
        }
        internal (int attack, int armor, int power, int resistance, int speed, int crit, int hp, int energy) Stats { get; }

        internal ItemDefinition(string id, string name, string description, string role,
            (int attack, int armor, int power, int resistance, int speed, int crit, int hp, int energy) stats = default,
            string firstComponent = null, string secondComponent = null, bool isComponent = false)
        {
            Id = id;
            Name = name;
            Description = description;
            Role = role;
            Stats = stats;
            FirstComponent = firstComponent;
            SecondComponent = secondComponent;
            IsComponent = isComponent;
        }
    }

    // Instance IDs carry an immutable definition key so frozen battles and public snapshots
    // can resolve tools without consulting another player's private inventory.
    public static class ItemCatalog
    {
        public const string Everstone = "everstone", Moonstone = "moonstone";
        public const string Leftovers="leftovers",SitrusBerry="sitrus-berry",AssaultVest="assault-vest",
            FocusSash="focus-sash",WiseGlasses="wise-glasses",RockyHelmet="rocky-helmet",
            ChoiceScarf="choice-scarf",ExpShare="exp-share";
        private static readonly IReadOnlyList<ItemDefinition> Legacy =Array.AsReadOnly(new[]{
            new ItemDefinition(Leftovers,"먹다남은음식","3초마다 최대 HP의 4% 회복","앞줄 / 장기전"),
            new ItemDefinition(SitrusBerry,"자뭉열매","HP 40% 이하에서 최대 HP의 25% 회복 (전투당 1회)","생존"),
            new ItemDefinition(AssaultVest,"돌격조끼","특수(마법) 피해 25% 감소","특수 피해 대응"),
            new ItemDefinition(FocusSash,"기합의띠","첫 치명적 피해를 HP 1로 견디고 1초간 피해 차단","핵심 유닛 보호"),
            new ItemDefinition(WiseGlasses,"박식안경","직접 기술 피해 20% 증가","기술 딜러"),
            new ItemDefinition(RockyHelmet,"울퉁불퉁멧","실제로 받은 직접 피해의 20%를 반사 (지속/반사 제외)","앞줄 / 반격"),
            new ItemDefinition(ChoiceScarf,"구애스카프","공격 속도 25% 증가, 직접 기술 피해 15% 감소","기본 공격 딜러"),
            new ItemDefinition(ExpShare,"학습장치","인접 아군의 기술 발동 시 에너지 10 획득 (2초 쿨다운)","배치 / 기술 지원")} .Concat(SynergyTraits.Origins.Select(CreateStoneEmblem)).ToArray());
        public static IReadOnlyList<ItemDefinition> Components { get; } = Array.AsReadOnly(new[]
        {
            new ItemDefinition(Everstone, "변함없는돌", "단독 효과 없음. 다른 재료 아이템과 조합하여 계열 상징을 만듭니다.", "상징 조합 재료", isComponent: true),
            new ItemDefinition("x-attack", "플러스파워", "공격력 +10%", "조합 재료", (10, 0, 0, 0, 0, 0, 0, 0), isComponent: true),
            new ItemDefinition("x-defense", "디펜드업", "방어력 +20", "조합 재료", (0, 20, 0, 0, 0, 0, 0, 0), isComponent: true),
            new ItemDefinition("x-special", "스페셜업", "주문력 +10", "조합 재료", (0, 0, 10, 0, 0, 0, 0, 0), isComponent: true),
            new ItemDefinition("x-sp-def", "스페셜가드", "마법 저항력 +20", "조합 재료", (0, 0, 0, 20, 0, 0, 0, 0), isComponent: true),
            new ItemDefinition("x-speed", "스피드업", "공격속도 +10%", "조합 재료", (0, 0, 0, 0, 10, 0, 0, 0), isComponent: true),
            new ItemDefinition("dire-hit", "크리티컬커터", "치명타 확률 +20%p", "조합 재료", (0, 0, 0, 0, 0, 20, 0, 0), isComponent: true),
            new ItemDefinition("guard-spec", "이펙트가드", "최대 HP +150", "조합 재료", (0, 0, 0, 0, 0, 0, 150, 0), isComponent: true),
            new ItemDefinition("pp-ether", "PP에이더", "시작 에너지 +15", "조합 재료", (0, 0, 0, 0, 0, 0, 0, 15), isComponent: true),
        });

        public static IReadOnlyList<ItemDefinition> Completed { get; } = Array.AsReadOnly(new[]
        {
            new ItemDefinition(Moonstone, "달의돌", "보유한 달의돌 1개당 필드 배치 한도 +1. 보관함이나 장착 상태 모두 적용됩니다.", "배치 한도", firstComponent: Everstone, secondComponent: Everstone),
            new ItemDefinition("crafted-kings-rock", "왕의징표석", "AD +50%", "AD 전사·AD 원거리", (50, 0, 0, 0, 0, 0, 0, 0), "x-attack", "x-attack"),
            new ItemDefinition("crafted-punching-glove", "펀치글러브", "AD +15%  방어 +15 기본 공격 3회 적중 시 최대 HP의 12% 보호막을 4초간 얻는다. 각 아이템의 재발동 대기시간은 6초.", "AD 전사·AD 탱커", (15, 15, 0, 0, 0, 0, 0, 0), "x-attack", "x-defense"),
            new ItemDefinition("crafted-weakness-policy", "약점보험", "AD +12%  AP +12 전투당 1회, HP가 처음 60% 이하가 되면 잠시 은신·대상 해제된다. 은신 종료 후 전투 종료까지 공격력 +20%, 주문력 +20.", "AD 전사·AP 전사", (12, 0, 12, 0, 0, 0, 0, 0), "x-attack", "x-special"),
            new ItemDefinition("crafted-muscle-band", "힘의머리띠", "AD +18%  마저 +15 기본 공격 시 공격력의 12%에 해당하는 추가 물리 피해를 준다. 특수 피해를 받으면 4초간 각 아이템의 추가 피해가 공격력의 24%가 된다(아이템별 재발동 8초).", "AD 전사·AD 원거리", (18, 0, 0, 15, 0, 0, 0, 0), "x-attack", "x-sp-def"),
            new ItemDefinition("crafted-metronome", "메트로놈", "AD +10%  공속 +15% 기본 공격할 때마다 공격속도 +4%씩 누적(아이템당 최대 8중첩). 대상이 바뀌어도 해당 중첩은 유지한다.", "AD 전사·AD 원거리", (10, 0, 0, 0, 15, 0, 0, 0), "x-attack", "x-speed"),
            new ItemDefinition("crafted-expert-belt", "달인의띠", "AD +15%  치확 +25%p 치명타 적중 시 대상에게 3초간 약점 노출(받는 피해 +8%)을 부여한다(동일 대상·아이템별 재발동 3초).", "AD 전사·AD 원거리", (15, 0, 0, 0, 0, 25, 0, 0), "x-attack", "dire-hit"),
            new ItemDefinition("crafted-shell-bell", "조개껍질방울", "AD +15%  HP +150 가한 단일 대상 피해의 12%를 회복한다. 광역·지속 피해의 경우 4% 회복한다.", "AD 전사", (15, 0, 0, 0, 0, 0, 150, 0), "x-attack", "guard-spec"),
            new ItemDefinition("crafted-choice-band", "구애머리띠", "AD +15%  시작 E +10 스킬 사용 후 5초간 일반 공격 피해 +25%. 스킬 재사용 시 지속시간이 갱신된다.", "AD 전사·AD 원거리", (15, 0, 0, 0, 0, 0, 0, 10), "x-attack", "pp-ether"),
            new ItemDefinition("crafted-rocky-helmet", "울퉁불퉁멧", "방어 +35  HP +150 근접 기본 공격에 피격될 때마다 공격자에게 착용자 최대 HP의 2.5% 물리 피해를 반사한다. 재발동 대기시간과 피해 상한은 없다.", "AD 탱커·AP 탱커", (0, 35, 0, 0, 0, 0, 150, 0), "x-defense", "x-defense"),
            new ItemDefinition("crafted-wise-glasses", "박식안경", "방어 +15  AP +15 현재 방어력의 30%만큼 주문력을 추가로 얻는다. 단, 아이템당 추가 주문력은 최대 +30.", "AP 탱커·AP 전사", (0, 15, 15, 0, 0, 0, 0, 0), "x-defense", "x-special"),
            new ItemDefinition("crafted-eviolite", "진화의휘석", "방어 +25  마저 +25 방어력과 마법저항력 각각 +15%. 현재 진화 가능한 포켓몬은 각각 추가 +15%를 얻는다.", "AD 탱커·AP 탱커", (0, 25, 0, 25, 0, 0, 0, 0), "x-defense", "x-sp-def"),
            new ItemDefinition("crafted-protective-pads", "방호패드", "방어 +20  공속 +15% 기본 공격 3회 적중 시 3초간 받는 피해 12% 감소 및 반사 피해 50% 감소.", "AD 전사·AD 탱커", (0, 20, 0, 0, 15, 0, 0, 0), "x-defense", "x-speed"),
            new ItemDefinition("crafted-focus-band", "기합의머리띠", "방어 +20  치확 +20%p 피격 4회마다 최대 HP 12% 보호막(4초)을 획득하고, 다음 기본 공격 1회를 확정 치명타로 만든다(아이템별 재발동 8초).", "AD 탱커·AD 전사", (0, 20, 0, 0, 0, 20, 0, 0), "x-defense", "dire-hit"),
            new ItemDefinition("crafted-focus-sash", "기합의띠", "방어 +15  HP +200 치명적인 피해를 받을 때 전투당 1회 그 피해를 방지하고 1초 동안 피해 면역·행동 불가가 된다. 종료 시 최대 HP 10% 회복.", "AD 탱커·AP 탱커", (0, 15, 0, 0, 0, 0, 200, 0), "x-defense", "guard-spec"),
            new ItemDefinition("crafted-lucky-egg", "행복의알", "방어 +15  시작 E +20 기본 공격 및 피격으로 획득하는 에너지 +25%.", "AD 탱커·AP 탱커", (0, 15, 0, 0, 0, 0, 0, 20), "x-defense", "pp-ether"),
            new ItemDefinition("crafted-life-orb", "생명의구슬", "AP +25 스킬 피해 +35%. 스킬 1회 시전이 끝나면 최대 HP의 3%를 소모하며, 이 소모만으로는 사망하지 않는다.", "AP 원거리·AP 전사", (0, 0, 25, 0, 0, 0, 0, 0), "x-special", "x-special"),
            new ItemDefinition("crafted-covert-cloak", "은밀망토", "AP +15  마저 +20 각 아이템은 전투 시작 시 디버프 차단 1회 충전. 적의 디버프 1개를 무효화하면 6초 후 다시 충전한다.", "AP 탱커·AP 원거리", (0, 0, 15, 20, 0, 0, 0, 0), "x-special", "x-sp-def"),
            new ItemDefinition("crafted-blunder-policy", "허탕보험", "AP +15  공속 +15% 스킬 사용 후 4초간 공격속도 +25%를 얻고, 그동안 기본 공격의 빗나감 1회를 명중 처리한다.", "AP 전사·AP 원거리", (0, 0, 15, 0, 15, 0, 0, 0), "x-special", "x-speed"),
            new ItemDefinition("crafted-loaded-dice", "속임수주사위", "AP +25  치확 +25%p 스킬 피해에 치명타가 적용된다. 추가로 아이템 하나당 스킬 치명타 피해 +10%p.", "AP 원거리·AP 전사", (0, 0, 25, 0, 0, 25, 0, 0), "x-special", "dire-hit"),
            new ItemDefinition("crafted-light-clay", "빛의점토", "AP +15  HP +200 자신이 생성하는 보호막량 +25%. 전투 시작 시 자신과 가장 가까운 아군에게 각각 착용자 최대 HP 8% 보호막(5초)을 제공한다.", "AP 탱커·AP 원거리", (0, 0, 15, 0, 0, 0, 200, 0), "x-special", "guard-spec"),
            new ItemDefinition("crafted-choice-specs", "구애안경", "AP +20  시작 E +10 전투 중 첫 번째 스킬 1회의 피해·보호막·회복량 및 AP 기반 버프·디버프 효과 +45%. 고정 CC 지속시간은 제외.", "AP 원거리·AP 탱커", (0, 0, 20, 0, 0, 0, 0, 10), "x-special", "pp-ether"),
            new ItemDefinition("crafted-assault-vest", "돌격조끼", "마저 +40  HP +150 받는 스킬 피해 20% 감소.", "AD 탱커·AP 탱커", (0, 0, 0, 40, 0, 0, 150, 0), "x-sp-def", "x-sp-def"),
            new ItemDefinition("crafted-adrenaline-orb", "주눅구슬", "마저 +15  공속 +15% 특수 피해를 받으면 공격속도 +5%(5초, 아이템당 최대 5중첩). 동일 아이템 재발동 간격 0.5초.", "AD 전사·AP 탱커", (0, 0, 0, 15, 15, 0, 0, 0), "x-sp-def", "x-speed"),
            new ItemDefinition("crafted-wide-lens", "광각렌즈", "마저 +15  치확 +20%p 치명타 확률을 추가로 +15%p 얻는다. 치명타 공격은 실명·회피를 무시한다.", "AD 전사·AD 원거리", (0, 0, 0, 15, 0, 20, 0, 0), "x-sp-def", "dire-hit"),
            new ItemDefinition("crafted-safety-goggles", "방진고글", "마저 +25  HP +150 받는 광역 스킬 피해가 30% 감소하고, 가루·포자 기술에 의한 상태이상에 면역이 된다.", "AD 탱커·AP 탱커", (0, 0, 0, 25, 0, 0, 150, 0), "x-sp-def", "guard-spec"),
            new ItemDefinition("crafted-mental-herb", "멘탈허브", "마저 +25  시작 E +20 전투당 1회, 자신에게 적용된 첫 기절·수면·혼란·속박을 즉시 해제하고 에너지 +20.", "AP 탱커·AP 원거리", (0, 0, 0, 25, 0, 0, 0, 20), "x-sp-def", "pp-ether"),
            new ItemDefinition("crafted-quick-claw", "선제공격손톱", "공속 +30% 전투 시작 시 기본 공격을 1회 즉시 예약하고, 전투 시작 후 5초간 공격속도 +25%.", "AD 전사·AD 원거리", (0, 0, 0, 0, 30, 0, 0, 0), "x-speed", "x-speed"),
            new ItemDefinition("crafted-scope-lens", "초점렌즈", "공속 +15%  치확 +25%p 기본 공격마다 치명타 확률 +5%p(아이템당 최대 +25%p). 대상이 바뀌어도 유지한다.", "AD 전사·AD 원거리", (0, 0, 0, 0, 15, 25, 0, 0), "x-speed", "dire-hit"),
            new ItemDefinition("crafted-red-card", "레드카드", "공속 +15%  HP +200 HP가 처음 50% 이하가 되면 주변 2칸에서 가장 가까운 적 1명을 1칸 밀쳐내고 1초 기절시킨다(아이템당 전투 1회).", "AD 전사·AP 탱커", (0, 0, 0, 0, 15, 0, 200, 0), "x-speed", "guard-spec"),
            new ItemDefinition("crafted-choice-scarf", "구애스카프", "공속 +20%  시작 E +15 기본 공격 적중 시 추가 에너지 +2(아이템당 초당 최대 +4).", "AD 원거리·AD 전사", (0, 0, 0, 0, 20, 0, 0, 15), "x-speed", "pp-ether"),
            new ItemDefinition("crafted-razor-claw", "예리한손톱", "치확 +45%p 치명타 피해 배율 +15%p.", "AD 전사·AD 원거리", (0, 0, 0, 0, 0, 45, 0, 0), "dire-hit", "dire-hit"),
            new ItemDefinition("crafted-salac-berry", "랑사열매", "치확 +20%p  HP +200 HP가 처음 50% 이하가 되면 전투 종료까지 치명타 확률 +30%p.", "AD 전사·AD 원거리", (0, 0, 0, 0, 0, 20, 200, 0), "dire-hit", "guard-spec"),
            new ItemDefinition("crafted-clear-amulet", "클리어참", "치확 +25%p  시작 E +20 적의 능력치 감소를 무효화한다. 스킬 사용 후 6초 내 다음 기본 공격 1회가 확정 치명타가 된다(아이템당 재발동 6초).", "AD 전사·AD 원거리", (0, 0, 0, 0, 0, 25, 0, 20), "dire-hit", "pp-ether"),
            new ItemDefinition("crafted-leftovers", "먹다남은음식", "HP +450 전투 중 매초 최대 HP의 1.2% 회복.", "AD 탱커·AP 탱커", (0, 0, 0, 0, 0, 0, 450, 0), "guard-spec", "guard-spec"),
            new ItemDefinition("crafted-big-root", "큰뿌리", "HP +250  시작 E +10 자신이 발생시키는 회복·흡혈량 +25%. 스킬 시전 시 최대 HP 4% 회복(아이템당 재발동 5초).", "AP 탱커·AP 전사", (0, 0, 0, 0, 0, 0, 250, 10), "guard-spec", "pp-ether"),
            new ItemDefinition("crafted-pp-max", "PP맥스", "AP +10  시작 E +25 전투 중 매초 에너지 2 회복.", "AP 원거리·AP 탱커", (0, 0, 10, 0, 0, 0, 0, 25), "pp-ether", "pp-ether"),
        }.Concat(Legacy.Where(item => item.Id.StartsWith("emblem-", StringComparison.Ordinal))).ToArray());

        private static ItemDefinition CreateStoneEmblem(string origin)
        {
            var stone = origin switch
            {
                SynergyTraits.Flame => ("불꽃의돌", "x-attack"),
                SynergyTraits.Torrent => ("물의돌", "x-defense"),
                SynergyTraits.Storm => ("천둥의돌", "x-special"),
                SynergyTraits.Nature => ("리프의돌", "guard-spec"),
                SynergyTraits.Earth => ("대지의돌", "x-sp-def"),
                SynergyTraits.Venom => ("맹독의돌", "pp-ether"),
                SynergyTraits.Mystic => ("신비의돌", "x-speed"),
                SynergyTraits.Adversity => ("격투의돌", "dire-hit"),
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            return new ItemDefinition("emblem-" + origin, stone.Item1,
                SynergyTraits.Name(origin) + " 시너지 상징. 장착한 포켓몬에게 해당 계열을 부여합니다. 같은 진화 계열은 1회 카운트.",
                "계열 상징", firstComponent: Everstone, secondComponent: stone.Item2);
        }
        // Keep legacy definitions first for existing instance IDs and serialized icon indices.
        public static IReadOnlyList<ItemDefinition> All { get; } =
            Array.AsReadOnly(Legacy.Concat(Components.Where(item => item.Id != Everstone))
                .Concat(Completed.Where(item => !item.Id.StartsWith("emblem-", StringComparison.Ordinal) && item.Id != Moonstone))
                .Concat(Components.Where(item => item.Id == Everstone)).Concat(Completed.Where(item => item.Id == Moonstone)).ToArray());

        private static readonly Dictionary<string, ItemDefinition> ById =
            All.ToDictionary(definition => definition.Id, StringComparer.Ordinal);

        public static int DeploymentBonus(IEnumerable<string> items) =>
            items.Count(id => DefinitionId(id) == Moonstone);

        public static bool IsComponent(string instanceId)
        {
            string id = DefinitionId(instanceId);
            return id != null && ById[id].IsComponent;
        }

        public static ItemDefinition Recipe(string firstComponent, string secondComponent)
            => Completed.FirstOrDefault(item =>
                (item.FirstComponent == firstComponent && item.SecondComponent == secondComponent) ||
                (item.FirstComponent == secondComponent && item.SecondComponent == firstComponent));

        public static ItemDefinition Get(string id)=>(id != null && ById.TryGetValue(id, out var definition) ? definition : null)
            ??throw new ArgumentException("Unknown item definition.",nameof(id));
        public static string DefinitionId(string instanceId)
        {
            if(instanceId==null)return null;
            var parts=instanceId.Split(':');
            return parts.Length>=4&&parts[0]=="item"&&ById.ContainsKey(parts[1])?parts[1]:null;
        }
        public static string DisplayName(string instanceId)=>All.FirstOrDefault(d=>d.Id==DefinitionId(instanceId))?.Name??instanceId;
        public static bool Has(IEnumerable<string> ids,string definition)=>ids.Any(id=>DefinitionId(id)==definition);
    }
    public sealed class ItemSystem
    {
        internal static Dictionary<string, List<string>> PlanWildDrops(MatchState match, PlayerState player, BattleState battle)
        {
            int round = match.RoundNumber;
            int count = WildRoundSystem.ItemRewardCount(round);
            int playerIndex = match.Players.ToList().IndexOf(player);
            ulong seed = unchecked(match.MatchSeed ^ 0x4954454D52455744UL
                ^ (ulong)round * 0x9E3779B97F4A7C15UL
                ^ (ulong)(playerIndex + 1) * 0xD1B54A32D192ED03UL);
            var random = new DeterministicRandom(seed);
            var monsters = battle.Units.Where(unit => unit.TeamId == 2 && !unit.IsSynergySummon)
                .Select(unit => unit.UnitInstanceId).ToArray();
            var drops = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            if (monsters.Length == 0) return drops;

            int first = 0;
            var components = ItemCatalog.Components;
            for (int i = 0; i < count; i++)
            {
                // Give each monster at most one drop before allocating any additional items.
                int pick = first + random.NextInt(monsters.Length - first);
                string monster = monsters[pick];
                monsters[pick] = monsters[first];
                monsters[first] = monster;
                first = (first + 1) % monsters.Length;

                if (!drops.TryGetValue(monster, out var items))
                    drops.Add(monster, items = new List<string>());
                string definition = components[random.NextInt(components.Count)].Id;
                items.Add($"item:{definition}:{round}-{i}:{player.PlayerId}");
            }
            return drops;
        }

        private static PlayerState Validate(MatchState match,string playerId,long revision)
        {
            var p=match.GetPlayer(playerId);
            if(p.HP==0||p.IsEliminated)throw new InvalidOperationException("Player is eliminated.");
            if(match.Phase!=MatchPhase.Preparation && match.Phase!=MatchPhase.Combat)throw new InvalidOperationException("Requires Preparation.");
            if(p.ItemRevision!=revision)throw new InvalidOperationException("Stale item revision.");
            return p;
        }
        public ItemDefinition Combine(MatchState match, string playerId, string sourceId, string targetId, long revision)
        {
            var player = Validate(match, playerId, revision);
            if (sourceId == targetId || !player.ItemInventory.Contains(sourceId) || !player.ItemInventory.Contains(targetId))
                throw new InvalidOperationException("Both items must be available in the inventory.");

            var recipe = ItemCatalog.Recipe(ItemCatalog.DefinitionId(sourceId), ItemCatalog.DefinitionId(targetId));
            if (recipe == null) throw new InvalidOperationException("조합할 수 없는 재료입니다.");

            player.CombineInventoryItems(sourceId, targetId, recipe);
            return recipe;
        }

        public void Equip(MatchState match,string playerId,string unitId,string itemId,long revision)
        {
            var p=Validate(match,playerId,revision);var unit=p.GetUnit(unitId);
            if(unit.Placement.Kind==PlacementKind.Unplaced||!p.ItemInventory.Contains(itemId)||ItemCatalog.DefinitionId(itemId)==null)
                throw new InvalidOperationException("Item is not available for this unit.");
            p.EquipInventoryItem(unit,itemId);
            match.RefreshCombatUnit(unit);
        }
    }
}
