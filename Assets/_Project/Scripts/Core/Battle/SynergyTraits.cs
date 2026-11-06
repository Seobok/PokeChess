using System;
using System.Collections.Generic;
using System.Linq;
namespace PokeChess.Core.Battle {
// v0.9 workbook: evolution families and unit assignments. Original Pokemon types remain separate.
public static class SynergyTraits {
public const string Flame = "flame";
public const string Torrent = "torrent";
public const string Storm = "storm";
public const string Earth = "earth";
public const string Nature = "nature";
public const string Venom = "venom";
public const string Mystic = "mystic";
public const string Adversity = "adversity";
public const string Guardian = "guardian";
public const string Bruiser = "bruiser";
public const string Duelist = "duelist";
public const string Tracker = "tracker";
public const string Sorcerer = "sorcerer";
public const string Invoker = "invoker";
public const string Disruptor = "disruptor";
public const string Assassin = "assassin";
public const string Supporter = "supporter";
public static IReadOnlyList<string> Origins { get; } = Array.AsReadOnly(new[] { Flame, Torrent, Storm, Earth, Nature, Venom, Mystic, Adversity });
public static IReadOnlyList<string> CombatTraits { get; } = Array.AsReadOnly(new[] { Guardian, Bruiser, Duelist, Tracker, Sorcerer, Invoker, Disruptor, Assassin, Supporter });
public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(Origins.Concat(CombatTraits).ToArray());
private static readonly Dictionary<string,(string family,string origin,string trait,int dex)> units = new Dictionary<string,(string,string,string,int)>(StringComparer.Ordinal) {
{ "caterpie", ("F01", Mystic, Disruptor, 10) },
{ "metapod", ("F01", Mystic, Disruptor, 11) },
{ "butterfree", ("F01", Mystic, Disruptor, 12) },
{ "weedle", ("F02", Venom, Duelist, 13) },
{ "kakuna", ("F02", Venom, Duelist, 14) },
{ "beedrill", ("F02", Venom, Duelist, 15) },
{ "pidgey", ("F03", Storm, Tracker, 16) },
{ "pidgeotto", ("F03", Storm, Tracker, 17) },
{ "pidgeot", ("F03", Storm, Tracker, 18) },
{ "ekans", ("F04", Venom, Disruptor, 23) },
{ "arbok", ("F04", Venom, Disruptor, 24) },
{ "sandshrew", ("F05", Earth, Bruiser, 27) },
{ "sandslash", ("F05", Earth, Bruiser, 28) },
{ "paras", ("F06", Venom, Disruptor, 46) },
{ "parasect", ("F06", Venom, Disruptor, 47) },
{ "diglett", ("F07", Earth, Assassin, 50) },
{ "dugtrio", ("F07", Earth, Assassin, 51) },
{ "psyduck", ("F08", Torrent, Invoker, 54) },
{ "golduck", ("F08", Torrent, Invoker, 55) },
{ "mankey", ("F09", Adversity, Duelist, 56) },
{ "primeape", ("F09", Adversity, Duelist, 57) },
{ "tentacool", ("F10", Torrent, Disruptor, 72) },
{ "tentacruel", ("F10", Torrent, Disruptor, 73) },
{ "farfetchd", ("F11", Adversity, Duelist, 83) },
{ "onix", ("F12", Earth, Guardian, 95) },
{ "voltorb", ("F13", Flame, Bruiser, 100) },
{ "electrode", ("F13", Flame, Bruiser, 101) },
{ "pikachu", ("F14", Storm, Sorcerer, 25) },
{ "raichu", ("F14", Storm, Sorcerer, 26) },
{ "clefairy", ("F15", Mystic, Supporter, 35) },
{ "clefable", ("F15", Mystic, Supporter, 36) },
{ "jigglypuff", ("F16", Nature, Supporter, 39) },
{ "wigglytuff", ("F16", Nature, Supporter, 40) },
{ "oddish", ("F17", Nature, Sorcerer, 43) },
{ "gloom", ("F17", Nature, Sorcerer, 44) },
{ "vileplume", ("F17", Nature, Sorcerer, 45) },
{ "meowth", ("F18", Adversity, Assassin, 52) },
{ "persian", ("F18", Adversity, Assassin, 53) },
{ "ponyta", ("F19", Flame, Bruiser, 77) },
{ "rapidash", ("F19", Flame, Bruiser, 78) },
{ "magnemite", ("F20", Storm, Invoker, 81) },
{ "magneton", ("F20", Storm, Invoker, 82) },
{ "doduo", ("F21", Storm, Tracker, 84) },
{ "dodrio", ("F21", Storm, Tracker, 85) },
{ "shellder", ("F22", Torrent, Guardian, 90) },
{ "cloyster", ("F22", Torrent, Guardian, 91) },
{ "cubone", ("F23", Earth, Tracker, 104) },
{ "marowak", ("F23", Earth, Tracker, 105) },
{ "koffing", ("F24", Venom, Sorcerer, 109) },
{ "weezing", ("F24", Venom, Sorcerer, 110) },
{ "tangela", ("F25", Nature, Supporter, 114) },
{ "horsea", ("F26", Torrent, Sorcerer, 116) },
{ "seadra", ("F26", Torrent, Sorcerer, 117) },
{ "bulbasaur", ("F27", Nature, Invoker, 1) },
{ "ivysaur", ("F27", Nature, Invoker, 2) },
{ "venusaur", ("F27", Nature, Invoker, 3) },
{ "charmander", ("F28", Flame, Sorcerer, 4) },
{ "charmeleon", ("F28", Flame, Sorcerer, 5) },
{ "charizard", ("F28", Flame, Sorcerer, 6) },
{ "squirtle", ("F29", Torrent, Guardian, 7) },
{ "wartortle", ("F29", Torrent, Guardian, 8) },
{ "blastoise", ("F29", Torrent, Guardian, 9) },
{ "poliwag", ("F30", Torrent, Bruiser, 60) },
{ "poliwhirl", ("F30", Torrent, Bruiser, 61) },
{ "poliwrath", ("F30", Torrent, Bruiser, 62) },
{ "slowpoke", ("F31", Torrent, Guardian, 79) },
{ "slowbro", ("F31", Torrent, Guardian, 80) },
{ "hitmonlee", ("F32", Adversity, Assassin, 106) },
{ "chansey", ("F33", Nature, Supporter, 113) },
{ "kangaskhan", ("F34", Adversity, Bruiser, 115) },
{ "mr-mime", ("F35", Mystic, Supporter, 122) },
{ "jynx", ("F36", Mystic, Disruptor, 124) },
{ "electabuzz", ("F37", Storm, Duelist, 125) },
{ "kabuto", ("F38", Earth, Assassin, 140) },
{ "kabutops", ("F38", Earth, Assassin, 141) },
{ "aerodactyl", ("F39", Storm, Tracker, 142) },
{ "nidoran-f", ("F40", Venom, Guardian, 29) },
{ "nidorina", ("F40", Venom, Guardian, 30) },
{ "nidoqueen", ("F40", Venom, Guardian, 31) },
{ "nidoran-m", ("F41", Venom, Duelist, 32) },
{ "nidorino", ("F41", Venom, Duelist, 33) },
{ "nidoking", ("F41", Venom, Duelist, 34) },
{ "growlithe", ("F42", Flame, Sorcerer, 58) },
{ "arcanine", ("F42", Flame, Sorcerer, 59) },
{ "abra", ("F43", Mystic, Invoker, 63) },
{ "kadabra", ("F43", Mystic, Invoker, 64) },
{ "alakazam", ("F43", Mystic, Invoker, 65) },
{ "machop", ("F44", Adversity, Bruiser, 66) },
{ "machoke", ("F44", Adversity, Bruiser, 67) },
{ "machamp", ("F44", Adversity, Bruiser, 68) },
{ "geodude", ("F45", Earth, Guardian, 74) },
{ "graveler", ("F45", Earth, Guardian, 75) },
{ "golem", ("F45", Earth, Guardian, 76) },
{ "gastly", ("F46", Venom, Disruptor, 92) },
{ "haunter", ("F46", Venom, Disruptor, 93) },
{ "gengar", ("F46", Venom, Disruptor, 94) },
{ "scyther", ("F47", Nature, Duelist, 123) },
{ "pinsir", ("F48", Nature, Duelist, 127) },
{ "dratini", ("F49", Adversity, Tracker, 147) },
{ "dragonair", ("F49", Adversity, Tracker, 148) },
{ "dragonite", ("F49", Adversity, Tracker, 149) },
{ "lapras", ("F50", Torrent, Disruptor, 131) },
{ "porygon", ("F51", Mystic, Invoker, 137) },
{ "snorlax", ("F52", Earth, Bruiser, 143) },
{ "articuno", ("F53", Storm, Disruptor, 144) },
{ "zapdos", ("F54", Storm, Invoker, 145) },
{ "moltres", ("F55", Flame, Sorcerer, 146) },
{ "mewtwo", ("F56", Mystic, Sorcerer, 150) },
{ "mew", ("F57", Nature, Supporter, 151) },
};
public static string Family(string id) => units.TryGetValue(id, out var u) ? u.family : id;
public static int Dex(string id) => units.TryGetValue(id, out var u) ? u.dex : int.MaxValue;
public static string[] ForPokemon(string id) => units.TryGetValue(id,out var u) ? new[] {u.origin,u.trait} : Array.Empty<string>();
public static string Name(string id) { switch(id) {
case Flame: return "화염";
case Torrent: return "격류";
case Storm: return "폭풍";
case Earth: return "대지";
case Nature: return "자연";
case Venom: return "맹독";
case Mystic: return "신비";
case Adversity: return "역경";
case Guardian: return "수호자";
case Bruiser: return "투사";
case Duelist: return "결투가";
case Tracker: return "추적자";
case Sorcerer: return "술사";
case Invoker: return "기원자";
case Disruptor: return "교란자";
case Assassin: return "암살자";
case Supporter: return "지원가";
default: return id; } }
public static int[] Thresholds(string id) { switch(id) {
case Flame: return new[] {2,3,5};
case Torrent: case Nature: return new[] {2,4,6,10};
case Guardian: return new[] {2,4,5};
case Tracker: return new[] {2,3,4};
case Assassin: return new[] {2,3};
case Invoker: case Supporter: return new[] {2,4};
default: return new[] {2,4,6}; } }
public static string Description(string id) { switch(id) {
case Flame: return "(2) 화염 유닛의 첫 피해 스킬 적중 시, 대상 타일에 3초간 불길 생성. 불길 위의 적은 초당 특수 피해를 받음.\n(3) 불길 위에서 화상 상태의 적이 처치되면 인접 1칸의 빈 불길 타일 1개에 2초짜리 불길 생성.\n(5) 불길의 범위가 넓어지고, 화염 유닛이 타일에 불을 붙일 때 주변 적에게 1회 폭발 피해.";
case Torrent: return "(2) 전투 시작 4초 후부터 7초마다 밀물/썰물 교대. 밀물: 전방 적 최대 2명 1칸 밀침. 썰물: 격류 아군에게 소형 보호막.\n(4) 밀물 영향을 받는 적이 최대 3명으로 증가. 썰물 보호막은 인접 아군에게도 적용.\n(6) 밀물은 전방 2열을 관통하여 적 최대 5명에게 소형 피해와 밀침. 썰물은 마나를 추가 지급.\n(10) 전투 시작 시 적 전원에게 대해일을 일으켜 크게 밀쳐내고 1.5초 기절. 이후 밀물·썰물 주기가 4초로 단축되고, 매 밀물에 전장 전체 파도 피해.";
case Storm: return "(2) 전투 시작 시 서로 인접한 폭풍 유닛끼리 회로를 연결. 연결된 유닛의 스킬 사용 시 전류 1회가 근처 적에게 이동.\n(4) 한 회로의 연결 유닛이 3명 이상이면 전류가 추가 적 1명에게 연쇄.\n(6) 회로에서 전류 4회가 발생하면 전체 회로가 한 번 방전하고 충전량 초기화.";
case Earth: return "(2) 준비 단계에 빈 아군 칸 1개를 지정해 바위 구조물 설치. 인접 아군은 받는 원거리 피해 일부 감소.\n(4) 바위 구조물 2개 설치 가능. 구조물 주변 아군이 처음 공격당할 때 작은 보호막.\n(6) 바위 구조물이 일정 주기마다 충격파를 발생시켜 인접 적을 잠시 둔화.";
case Nature: return "(2) 별도 배치 슬롯을 차지하지 않는 숲의 수호자 1기 소환. 플레이어 전투에서 생존 또는 처치 관여 시 성장 경험치 획득.\n(4) 수호자가 성장 단계에 따라 뿌리 속박/회복 중 하나를 해금. 자연 유닛의 회복 효과를 받으면 성장 경험치 추가 1회.\n(6) 자연 유닛이 스킬을 3회 사용할 때마다 수호자가 보조 스킬 시전. 수호자 생존 시 전투 종료 보너스 경험치.\n(10) 숲의 수호자가 세계수 형태로 전투에 등장. 시작 시 적 전체 뿌리 속박, 주기적으로 적 전열 공격 + 아군 광역 회복.";
case Venom: return "(2) 맹독 유닛이 중독시킨 적을 플레이어 전투에서 처치하면 적 1명당 표본 1획득(전투당 최대 4). 표본은 라운드 사이 유지.\n(4) 전투당 표본 획득 상한 6. 표본 10개부터 중독된 적이 사망하면 가장 가까운 적에게 독 전이.\n(6) 표본 25개부터 독 피해 및 치유 억제 강화. 45개부터 중독된 적이 스킬을 사용하면 작은 추가 피해.";
case Mystic: return "(2) 신비가 플레이어 전투 시작 시 활성화된 라운드 2회마다 다음 준비 단계에 무료 새로고침 1회 지급.\n(4) 신비 활성화된 매 플레이어 전투 후 다음 준비 단계에 무료 새로고침 1회 지급.\n(6) 무료 새로고침 전에 계열 하나 지정. 무료 새로고침 결과의 한 칸은, 나온 코스트에 해당 계열 유닛이 존재하면 그 계열을 우선 제시.";
case Adversity: return "(2) 역경 활성화 상태에서 플레이어 전투 패배 시 투지 +1. 역경 활성화 상태에서 첫 승리 시 모은 투지를 보상으로 정산.\n(4) 패배 시 투지 +2. 승리 정산 시 투지에 비례한 골드와 소량 전리품 획득.\n(6) 패배 시 투지 +3. 누적 투지 7 이상으로 승리 정산 시 골드·장비 조각·계열 상징 조각 중 하나를 선택.";
case Guardian: return "(2) 수호자 물리·마법저항력 각각 +10. 준비 단계에 인접한 비수호자 아군 1명을 수호 대상으로 지정하고 그 대상이 받는 피해 -4%.\n(4) 수호자 물리·마법저항력 각각 +20. 수호자당 인접한 비수호자 아군 2명을 수호 대상으로 지정. 대상이 받는 피해 -7%. 첫 적중 피해를 추가 15% 감소.\n(5) 수호자 물리·마법저항력 각각 +28. 수호자당 인접한 비수호자 아군 2명을 수호. 받는 피해 -10%, 첫 적중 피해 추가 20% 감소. 대상 HP 최초 50% 이하 시 최대 HP 12% 보호막(5초).";
case Bruiser: return "(2) 아군 전체 최대 HP +4%. 투사는 추가로 최대 HP +10%(총 +14%).\n(4) 아군 전체 최대 HP +6%. 투사는 추가로 최대 HP +22%(총 +28%).\n(6) 아군 전체 최대 HP +8%. 투사는 추가로 최대 HP +38%(총 +46%). 투사가 최초 HP 40% 이하 진입 시 4초간 받는 피해 -15%.";
case Duelist: return "(2) 결투가 기본 공격 적중 시 공격속도 +4%, 최대 6중첩(최대 +24%). 중첩은 해당 전투 종료까지 유지.\n(4) 기본 공격 적중 시 공격속도 +6%, 최대 6중첩(최대 +36%).\n(6) 기본 공격 적중 시 공격속도 +8%, 최대 6중첩(최대 +48%). 적 처치 관여 시 가장 가까운 적으로 목표 변경, 2초간 이동속도 +40% 및 다음 기본 공격 피해 +25%.";
case Tracker: return "(2) 추적자의 기본 공격 또는 피해 스킬 적중 시 대상에게 개인 추적 중첩 1개(3초), 최대 3개. 2중첩 이상인 적에게 해당 추적자가 주는 피해 +10%.\n(3) 동일 규칙으로 2중첩 이상 대상 추가 피해 +18%. 첫 3중첩 달성 시 대상에게 특수 피해 70을 1회 부여.\n(4) 2중첩 이상 대상 추가 피해 +25%. 첫 3중첩 달성 시 특수 피해 110. 표식 대상 처치 시 가장 가까운 새 적에게 중첩 1개 자동 부여.";
case Sorcerer: return "(2) 술사 주문력 +12. 술사가 스킬을 사용하면 추가 주문력 +3을 획득, 최대 4중첩(총 +24).\n(4) 술사 주문력 +24. 스킬 사용 시 추가 주문력 +5, 최대 5중첩(총 +49).\n(6) 술사 주문력 +36. 스킬 사용 시 추가 주문력 +8, 최대 6중첩(총 +84).";
case Invoker: return "(2) 전투 시작 후 3초마다 모든 아군 마나 +3. 기원자는 이와 별개로 마나 +3을 추가로 획득(총 +6).\n(4) 전투 시작 후 3초마다 모든 아군 마나 +6. 기원자는 추가 +4(총 +10). 각 기원자는 전투 첫 스킬 사용이 끝나면 마나 +10 반환.";
case Disruptor: return "(2) 교란자가 적에게 제어/약화 효과를 실제 적용하면 해당 적의 공격력·주문력 -8%(3초).\n(4) 제어/약화 적용 시 공격력·주문력 -12%(3초). 해당 적이 모든 아군에게 받는 피해 +8%(3초).\n(6) 제어/약화 적용 시 공격력·주문력 -18%(3초), 받는 모든 피해 +12%(3초). 최초 약화 때 3초간 마나 획득량 -25%.";
case Assassin: return "(2) 암살자 치명타 확률 +12%p, 치명타 추가 피해 +15%p. 암살자 피해 스킬도 치명타 가능.\n(3) 치명타 확률 +20%p, 치명타 추가 피해 +25%p. 처음 처치 관여 시 1.2초간 은신 후 다음 공격 피해 +20%.";
case Supporter: return "(2) 지원가가 스킬 사용을 완료하면 생존한 아군 중 HP 비율이 가장 낮은 대상에게 보호막 80(4초).\n(4) 지원가 스킬 완료 시 HP 비율 최저 아군에게 보호막 140(4초), 대상 마나 +8.";
default: return ""; } }
}
}