using System.Collections.Generic;
using System.Linq;

// Static Civ6-style definitions: tech tree, civic tree, governments, policy
// cards, and buildable units/buildings/districts. Pure data — GameManager
// reads these to gate research, production, and UI.

public class TechDef
{
    public string id, name, unlockDesc;
    public int cost;
    public string[] prereqs;
}

public class CivicDef
{
    public string id, name, unlockDesc;
    public int cost;
    public string[] prereqs;
}

public class GovDef
{
    public string id, name;
    public int slots;
    public string reqCivic; // null = available from the start
    public string bonusDesc;
}

public class PolicyDef
{
    public string id, name, yieldTarget; // "gold" | "production" | "science" | "culture"
    public float mult;
    public string reqCivic; // null = available from the start
}

public enum BuildKind { Unit, Building, District, Wonder }

public class BuildableDef
{
    public string id, name;
    public BuildKind kind;
    public int cost;
    public string reqTech; // null = always available
}

public class CityStateDef
{
    public string name, bonusType; // bonusType: "gold" | "science" | "culture"
    public float bonusAmount;
}

public static class GameData
{
    public static readonly List<TechDef> Techs = new List<TechDef>
    {
        new TechDef { id = "mining", name = "채굴", cost = 25, prereqs = new string[0], unlockDesc = "산업 구역 해금" },
        new TechDef { id = "pottery", name = "도자기", cost = 25, prereqs = new string[0], unlockDesc = "곡창 건물 해금" },
        new TechDef { id = "writing", name = "문자", cost = 40, prereqs = new string[0], unlockDesc = "캠퍼스 구역, 도서관 해금" },
        new TechDef { id = "masonry", name = "석공술", cost = 40, prereqs = new string[0], unlockDesc = "성벽 건물 해금" },
        new TechDef { id = "horseback_riding", name = "기마술", cost = 40, prereqs = new string[0], unlockDesc = "기마병 유닛 해금" },
        new TechDef { id = "bronze_working", name = "청동기술", cost = 40, prereqs = new[] { "mining" }, unlockDesc = "궁수 유닛 해금" },
        new TechDef { id = "currency", name = "화폐", cost = 60, prereqs = new[] { "bronze_working" }, unlockDesc = "상업 허브 구역, 시장 해금" },
        new TechDef { id = "iron_working", name = "제철", cost = 60, prereqs = new[] { "bronze_working" }, unlockDesc = "검사 유닛 해금" },
    };

    public static readonly List<CivicDef> Civics = new List<CivicDef>
    {
        new CivicDef { id = "code_of_laws", name = "법전", cost = 25, prereqs = new string[0], unlockDesc = "정치 철학 연구 가능" },
        new CivicDef { id = "foreign_trade", name = "대외 무역", cost = 25, prereqs = new string[0], unlockDesc = "정책: 상인 연합 해금" },
        new CivicDef { id = "craftsmanship", name = "장인 정신", cost = 40, prereqs = new string[0], unlockDesc = "정책: 국가 노동력 해금" },
        new CivicDef { id = "mysticism", name = "신비주의", cost = 40, prereqs = new string[0], unlockDesc = "정책: 신비 의식 해금" },
        new CivicDef { id = "political_philosophy", name = "정치 철학", cost = 60, prereqs = new[] { "code_of_laws" }, unlockDesc = "군주제·과두제·공화정 정부 해금" },
    };

    public static readonly List<GovDef> Governments = new List<GovDef>
    {
        new GovDef { id = "chiefdom", name = "족장국", slots = 2, reqCivic = null, bonusDesc = "정책 카드 2슬롯" },
        new GovDef { id = "autocracy", name = "군주제", slots = 4, reqCivic = "political_philosophy", bonusDesc = "모든 도시 생산력 +1, 정책 카드 4슬롯" },
        new GovDef { id = "oligarchy", name = "과두제", slots = 4, reqCivic = "political_philosophy", bonusDesc = "모든 유닛 전투력 +2, 정책 카드 4슬롯" },
        new GovDef { id = "classical_republic", name = "공화정", slots = 4, reqCivic = "political_philosophy", bonusDesc = "모든 도시 편의시설 +1, 정책 카드 4슬롯" },
    };

    public static readonly List<PolicyDef> Policies = new List<PolicyDef>
    {
        new PolicyDef { id = "tribal_tradition", name = "부족 전통", yieldTarget = "production", mult = 0.10f, reqCivic = null },
        new PolicyDef { id = "improvised_rule", name = "즉흥 통치", yieldTarget = "gold", mult = 0.10f, reqCivic = null },
        new PolicyDef { id = "merchant_confederation", name = "상인 연합", yieldTarget = "gold", mult = 0.15f, reqCivic = "foreign_trade" },
        new PolicyDef { id = "state_workforce", name = "국가 노동력", yieldTarget = "production", mult = 0.15f, reqCivic = "craftsmanship" },
        new PolicyDef { id = "mystic_rites", name = "신비 의식", yieldTarget = "culture", mult = 0.15f, reqCivic = "mysticism" },
        new PolicyDef { id = "scholarly_pursuit", name = "학문 장려", yieldTarget = "science", mult = 0.15f, reqCivic = "political_philosophy" },
    };

    public static readonly List<BuildableDef> Buildables = new List<BuildableDef>
    {
        new BuildableDef { id = "settler", name = "정착민", kind = BuildKind.Unit, cost = 50, reqTech = null },
        new BuildableDef { id = "warrior", name = "전사", kind = BuildKind.Unit, cost = 30, reqTech = null },
        new BuildableDef { id = "archer", name = "궁수", kind = BuildKind.Unit, cost = 45, reqTech = "bronze_working" },
        new BuildableDef { id = "horseman", name = "기마병", kind = BuildKind.Unit, cost = 50, reqTech = "horseback_riding" },
        new BuildableDef { id = "swordsman", name = "검사", kind = BuildKind.Unit, cost = 65, reqTech = "iron_working" },

        new BuildableDef { id = "granary", name = "곡창", kind = BuildKind.Building, cost = 60, reqTech = "pottery" },
        new BuildableDef { id = "library", name = "도서관", kind = BuildKind.Building, cost = 70, reqTech = "writing" },
        new BuildableDef { id = "market", name = "시장", kind = BuildKind.Building, cost = 70, reqTech = "currency" },
        new BuildableDef { id = "walls", name = "성벽", kind = BuildKind.Building, cost = 80, reqTech = "masonry" },

        new BuildableDef { id = "campus", name = "캠퍼스 구역", kind = BuildKind.District, cost = 54, reqTech = "writing" },
        new BuildableDef { id = "commercial", name = "상업 허브 구역", kind = BuildKind.District, cost = 54, reqTech = "currency" },
        new BuildableDef { id = "industrial", name = "산업 구역", kind = BuildKind.District, cost = 54, reqTech = "mining" },

        new BuildableDef { id = "trader", name = "무역상인", kind = BuildKind.Unit, cost = 40, reqTech = "currency" },

        // Wonders are global — only one civilization in the world can complete each one.
        // Effects are flat per-turn bonuses applied to every city the owner controls.
        new BuildableDef { id = "pyramids", name = "피라미드", kind = BuildKind.Wonder, cost = 150, reqTech = "masonry" },
        new BuildableDef { id = "great_library", name = "대도서관", kind = BuildKind.Wonder, cost = 150, reqTech = "writing" },
        new BuildableDef { id = "stonehenge", name = "스톤헨지", kind = BuildKind.Wonder, cost = 120, reqTech = "pottery" },
    };

    public static readonly List<CityStateDef> CityStates = new List<CityStateDef>
    {
        new CityStateDef { name = "과학의 도시", bonusType = "science", bonusAmount = 3f },
        new CityStateDef { name = "상업의 도시", bonusType = "gold", bonusAmount = 4f },
        new CityStateDef { name = "예술의 도시", bonusType = "culture", bonusAmount = 3f },
    };

    public static TechDef FindTech(string id) => Techs.FirstOrDefault(t => t.id == id);
    public static CivicDef FindCivic(string id) => Civics.FirstOrDefault(c => c.id == id);
    public static GovDef FindGov(string id) => Governments.FirstOrDefault(g => g.id == id);
    public static PolicyDef FindPolicy(string id) => Policies.FirstOrDefault(p => p.id == id);
    public static BuildableDef FindBuildable(string id) => Buildables.FirstOrDefault(b => b.id == id);

    public static bool PrereqsMet(string[] prereqs, HashSet<string> completed) => prereqs.All(completed.Contains);
}
