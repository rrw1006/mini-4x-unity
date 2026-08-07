using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Mini 4X prototype — self-bootstrapping, no scene setup required.
// Drop this file anywhere under Assets/Scripts and press Play in any scene.
// Art: Kenney "Tiny Battle" pack (CC0), loaded from Assets/Resources/GameArt.
// Civ6-inspired systems: separate gold/production/science/culture yields,
// a tech tree and civic tree (GameData.cs), governments with policy card
// slots, and per-city production queues for units/buildings/districts.
public class GameManager : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("GameManager");
        go.AddComponent<GameManager>();
        Object.DontDestroyOnLoad(go);
    }

    // ---------------- Constants ----------------
    const int GRID_W = 32;
    const int GRID_H = 20;
    const float TILE_SIZE = 44f;
    const float BOARD_X = 16f;
    const float BOARD_Y = 100f;
    const int VISION_RADIUS = 2;
    const int LOYALTY_RADIUS = 5;
    const float RUSH_BUY_GOLD_PER_PRODUCTION = 4f;

    static readonly Color FogColor = new Color(0.06f, 0.06f, 0.08f);
    static readonly Color WaterColor = new Color(0.22f, 0.48f, 0.78f);
    static readonly Color HillsTint = new Color(0.78f, 0.68f, 0.45f);
    static readonly Color[] PlayerColors = { new Color(0.25f, 0.55f, 0.95f), new Color(0.88f, 0.22f, 0.22f) };
    static readonly Dictionary<string, Color> DistrictColors = new Dictionary<string, Color>
    {
        { "campus", new Color(0.35f, 0.55f, 0.95f) },
        { "commercial", new Color(0.95f, 0.75f, 0.20f) },
        { "industrial", new Color(0.55f, 0.40f, 0.30f) },
    };
    static readonly Dictionary<string, string> DistrictLabels = new Dictionary<string, string>
    {
        { "campus", "캠" }, { "commercial", "상" }, { "industrial", "산" },
    };
    static readonly Dictionary<string, string> DistrictNames = new Dictionary<string, string>
    {
        { "campus", "캠퍼스" }, { "commercial", "상업 허브" }, { "industrial", "산업 구역" },
    };
    static readonly Dictionary<string, string> UnitLetters = new Dictionary<string, string>
    {
        { "archer", "궁" }, { "horseman", "기" }, { "swordsman", "검" }, { "trader", "무" },
    };
    static readonly Dictionary<string, Color> UnitColors = new Dictionary<string, Color>
    {
        { "archer", new Color(0.45f, 0.75f, 0.35f) }, { "horseman", new Color(0.75f, 0.55f, 0.25f) }, { "swordsman", new Color(0.6f, 0.6f, 0.65f) },
        { "trader", new Color(0.85f, 0.75f, 0.35f) },
    };
    static readonly Color CityStateColor = new Color(0.55f, 0.55f, 0.58f);

    // ---------------- Data ----------------
    class UnitData
    {
        public int id, owner, x, y, hp, maxHp, attack, movesLeft, maxMoves;
        public string type;
    }

    class DistrictData
    {
        public int x, y;
        public string type;
    }

    class BuildOrder
    {
        public string id;
        public int cost;
        public float progress;
    }

    class TradeRoute
    {
        public int originCityId, destCityId;
        public int turnsLeft;
    }

    class CityData
    {
        public int id, owner, x, y, hp, maxHp;
        public string name;
        public int population = 1;
        public float food = 0;
        public int foodToGrow = 15;
        public int housing;
        public int amenities;
        public float loyalty = 100f;
        public List<DistrictData> districts = new List<DistrictData>();
        public HashSet<string> buildings = new HashSet<string>();
        public List<BuildOrder> queue = new List<BuildOrder>();
        public float goldPerTurn, productionPerTurn, sciencePerTurn, culturePerTurn;
    }

    class PlayerState
    {
        public int gold = 120;
        public float scienceStock = 0, cultureStock = 0;
        public HashSet<string> techs = new HashSet<string>();
        public HashSet<string> civics = new HashSet<string>();
        public string currentTech = null, currentCivic = null;
        public string government = "chiefdom";
        public HashSet<string> policies = new HashSet<string>();

        // Wonders / eras / golden & dark ages
        public HashSet<string> wonders = new HashSet<string>();
        public float eraScore = 0;
        public int era = 0;
        public int goldenTurns = 0, darkTurns = 0;

        // Great people
        public float gsPoints = 0, gePoints = 0, gmPoints = 0;
        public int gpThreshold = 60;

        // City-states: cityId -> envoys sent by this player
        public Dictionary<int, int> envoys = new Dictionary<int, int>();
    }

    string[,] tiles;
    bool[,] revealed; // ever explored (terrain memory, stays true forever)
    bool[,] visible;  // currently in line of sight (live units/cities only shown here)
    bool[,] freshwater;
    List<UnitData> units = new List<UnitData>();
    List<CityData> cities = new List<CityData>();
    PlayerState[] players;
    int nextUnitId = 1;
    int nextCityId = 1;

    Dictionary<string, int> wonderOwner = new Dictionary<string, int>(); // wonder id -> player index
    Dictionary<int, CityStateDef> cityStateDefById = new Dictionary<int, CityStateDef>(); // city id -> archetype
    List<TradeRoute> tradeRoutes = new List<TradeRoute>();

    int currentPlayer = 0;
    int turnNumber = 1;
    int selectedUnitId = -1;
    int selectedCityId = -1;
    bool gameOver = false;
    bool aiTurnRunning = false;
    string statusText = "";

    bool showTechPanel = false, showCivicPanel = false, showGovPanel = false, showGreatPanel = false;

    Texture2D flatTex;
    GUIStyle labelStyle;
    GUIStyle smallStyle;
    GUIStyle bigLabelStyle;
    GUIStyle goldStyle;
    GUIStyle goldShadowStyle;
    Font koreanFont;

    // ---------------- Art ----------------
    Dictionary<string, Texture2D> terrainTex = new Dictionary<string, Texture2D>();
    Texture2D[] cityTex = new Texture2D[2];
    Dictionary<string, Texture2D[]> unitTex = new Dictionary<string, Texture2D[]>();
    static readonly string[] UnitTypesWithArt = { "settler", "warrior", "archer", "horseman", "swordsman", "trader" };

    void LoadArt()
    {
        terrainTex["plains"] = Resources.Load<Texture2D>("GameArt/terrain_plains");
        terrainTex["forest"] = Resources.Load<Texture2D>("GameArt/terrain_forest");
        terrainTex["mountain"] = Resources.Load<Texture2D>("GameArt/terrain_mountain");
        terrainTex["hills"] = terrainTex["plains"]; // same base art, tinted at draw time
        cityTex[0] = Resources.Load<Texture2D>("GameArt/city_p0");
        cityTex[1] = Resources.Load<Texture2D>("GameArt/city_p1");

        foreach (var type in UnitTypesWithArt)
        {
            unitTex[type] = new[]
            {
                Resources.Load<Texture2D>($"GameArt/unit_{type}_p0"),
                Resources.Load<Texture2D>($"GameArt/unit_{type}_p1"),
            };
        }

        foreach (var t in terrainTex.Values) if (t != null) t.filterMode = FilterMode.Point;
        foreach (var t in cityTex) if (t != null) t.filterMode = FilterMode.Point;
        foreach (var arr in unitTex.Values)
            foreach (var t in arr) if (t != null) t.filterMode = FilterMode.Point;
    }

    void Awake()
    {
        flatTex = new Texture2D(1, 1);
        flatTex.SetPixel(0, 0, Color.white);
        flatTex.Apply();

        players = new PlayerState[] { new PlayerState(), new PlayerState() };

        LoadArt();
        GenerateMap();
        SpawnStart(0, 2, 2);
        SpawnStart(1, GRID_W - 3, GRID_H - 3);
        SpawnCityStates();
        RevealAroundPlayer0();
    }

    // ---------------- City-states ----------------
    void SpawnCityStates()
    {
        var targets = new[] { new Vector2Int(GRID_W / 2, 2), new Vector2Int(2, GRID_H - 4), new Vector2Int(GRID_W - 4, GRID_H / 2) };
        foreach (var t in targets)
        {
            var spot = FindNearestSettleable(t.x, t.y);
            if (spot.x < 0) continue;
            var c = AddCity(2, spot.x, spot.y);
            var def = GameData.CityStates[cityStateDefById.Count % GameData.CityStates.Count];
            c.name = def.name;
            cityStateDefById[c.id] = def;
        }
    }

    // Spirals outward from (cx,cy) to find the nearest land tile with no existing city,
    // so fixed city-state target coordinates still work on a randomly generated map.
    Vector2Int FindNearestSettleable(int cx, int cy)
    {
        for (int r = 0; r < Mathf.Max(GRID_W, GRID_H); r++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                    int xx = cx + dx, yy = cy + dy;
                    if (!InBounds(xx, yy)) continue;
                    if (GetTile(xx, yy) == "water" || GetTile(xx, yy) == "mountain") continue;
                    if (CityAt(xx, yy) != null) continue;
                    return new Vector2Int(xx, yy);
                }
            }
        }
        return new Vector2Int(-1, -1);
    }

    void SendEnvoy(int cityStateId, int playerIdx)
    {
        var pl = players[playerIdx];
        const int cost = 30;
        if (pl.gold < cost) { if (playerIdx == 0) statusText = "골드가 부족합니다."; return; }
        pl.gold -= cost;
        pl.envoys.TryGetValue(cityStateId, out int cur);
        pl.envoys[cityStateId] = cur + 1;
        if (playerIdx == 0) statusText = "특사를 파견했습니다.";
    }

    int SuzerainOf(int cityStateId)
    {
        players[0].envoys.TryGetValue(cityStateId, out int e0);
        players[1].envoys.TryGetValue(cityStateId, out int e1);
        if (e0 == 0 && e1 == 0) return -1;
        if (e0 == e1) return -1;
        return e0 > e1 ? 0 : 1;
    }

    // ---------------- Map generation ----------------
    string GetTile(int x, int y) => tiles[x, y];

    void GenerateMap()
    {
        tiles = new string[GRID_W, GRID_H];
        revealed = new bool[GRID_W, GRID_H];
        visible = new bool[GRID_W, GRID_H];

        // Perlin noise gives every tile a smoothly-varying elevation, so water and
        // land naturally form single continuous regions with no holes or fragments
        // (unlike a random walk/flood-fill, which can leave gaps or stray patches).
        float noiseScale = 0.20f;
        float elevOffX = Random.Range(0f, 1000f);
        float elevOffY = Random.Range(0f, 1000f);
        float coverOffX = Random.Range(0f, 1000f);
        float coverOffY = Random.Range(0f, 1000f);

        for (int y = 0; y < GRID_H; y++)
        {
            for (int x = 0; x < GRID_W; x++)
            {
                float elevation = Mathf.PerlinNoise(elevOffX + x * noiseScale, elevOffY + y * noiseScale);
                string t;
                if (elevation < 0.36f)
                {
                    t = "water";
                }
                else
                {
                    float cover = Mathf.PerlinNoise(coverOffX + x * noiseScale, coverOffY + y * noiseScale);
                    if (cover > 0.68f) t = "mountain";
                    else if (cover > 0.52f) t = "forest";
                    else if (cover > 0.42f) t = "hills";
                    else t = "plains";
                }
                tiles[x, y] = t;
                revealed[x, y] = false;
            }
        }

        foreach (var pos in new[] { new Vector2Int(2, 2), new Vector2Int(GRID_W - 3, GRID_H - 3) })
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int xx = pos.x + dx, yy = pos.y + dy;
                    if (InBounds(xx, yy)) tiles[xx, yy] = "plains";
                }
        }

        // Land next to water counts as freshwater (river/lake) access — no separate
        // river-edge system, just a settle/adjacency bonus for coastal-ish tiles.
        freshwater = new bool[GRID_W, GRID_H];
        for (int y = 0; y < GRID_H; y++)
        {
            for (int x = 0; x < GRID_W; x++)
            {
                if (tiles[x, y] == "water") continue;
                for (int dy = -1; dy <= 1 && !freshwater[x, y]; dy++)
                    for (int dx = -1; dx <= 1 && !freshwater[x, y]; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int xx = x + dx, yy = y + dy;
                        if (InBounds(xx, yy) && tiles[xx, yy] == "water") freshwater[x, y] = true;
                    }
            }
        }
    }

    void SpawnStart(int owner, int x, int y)
    {
        AddUnit(owner, "settler", x, y);
        AddUnit(owner, "warrior", x + 1, y);
    }

    // ---------------- Units / Cities ----------------
    static readonly Dictionary<string, Vector3Int> BaseUnitStats = new Dictionary<string, Vector3Int>
    {
        // x=attack, y=moves, z=hp
        { "settler", new Vector3Int(0, 1, 10) },
        { "warrior", new Vector3Int(3, 2, 10) },
        { "archer", new Vector3Int(4, 2, 10) },
        { "horseman", new Vector3Int(5, 3, 12) },
        { "swordsman", new Vector3Int(6, 2, 14) },
        { "trader", new Vector3Int(0, 3, 8) },
    };

    UnitData AddUnit(int owner, string type, int x, int y)
    {
        var stats = BaseUnitStats.TryGetValue(type, out var s) ? s : new Vector3Int(0, 1, 10);
        var u = new UnitData
        {
            id = nextUnitId++,
            owner = owner,
            type = type,
            x = x,
            y = y,
            hp = stats.z,
            maxHp = stats.z,
            attack = stats.x,
            movesLeft = stats.y,
            maxMoves = stats.y,
        };
        units.Add(u);
        return u;
    }

    CityData AddCity(int owner, int x, int y)
    {
        bool coastal = freshwater != null && freshwater[x, y];
        bool onHills = tiles[x, y] == "hills";

        var c = new CityData
        {
            id = nextCityId++,
            owner = owner,
            x = x,
            y = y,
            name = cities.Count == 0 ? "수도" : ("도시 " + nextCityId),
            hp = 20,
            maxHp = onHills ? 26 : 20, // hills give a defensive edge, matching their settle appeal
            population = 1,
            food = 0,
            foodToGrow = FoodToGrow(1),
            housing = 3 + (coastal ? 2 : 0) + (onHills ? 1 : 0),
            amenities = 1 + (coastal ? 1 : 0),
            loyalty = 100f,
        };
        cities.Add(c);
        if (owner != 2) RecomputeCityYield(c); // city-states don't have a PlayerState / yields
        return c;
    }

    int FoodToGrow(int population) => 15 + 8 * (population - 1);

    // ---------------- Growth / yield / loyalty ----------------
    int CountAdjacent(int x, int y, System.Func<int, int, bool> match)
    {
        int n = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int xx = x + dx, yy = y + dy;
                if (InBounds(xx, yy) && match(xx, yy)) n++;
            }
        return n;
    }

    float PolicyBonus(int owner, string yieldTarget)
    {
        float sum = 0f;
        foreach (var pid in players[owner].policies)
        {
            var pd = GameData.FindPolicy(pid);
            if (pd != null && pd.yieldTarget == yieldTarget) sum += pd.mult;
        }
        return sum;
    }

    void RecomputeCityYield(CityData c)
    {
        // Housing/amenities are recomputed from current population + district/building count so
        // both track growth and construction as the game progresses.
        bool coastal = freshwater[c.x, c.y];
        bool onHills = tiles[c.x, c.y] == "hills";
        var gov = players[c.owner].government;

        c.housing = 3 + (coastal ? 2 : 0) + (onHills ? 1 : 0) + c.districts.Count + (c.buildings.Contains("granary") ? 1 : 0);
        c.amenities = 1 + (coastal ? 1 : 0) + c.districts.Count + (gov == "classical_republic" ? 1 : 0);

        float baseY = 1f + c.population * 0.5f;
        float production = baseY;
        float gold = baseY * 0.7f;
        float science = baseY * 0.5f;
        float culture = baseY * 0.4f;

        foreach (var d in c.districts)
        {
            if (d.type == "industrial")
            {
                production += CountAdjacent(d.x, d.y, (xx, yy) => tiles[xx, yy] == "mountain");
                production += CountAdjacent(d.x, d.y, (xx, yy) => freshwater[xx, yy]);
            }
            else if (d.type == "commercial")
            {
                gold += 2 * CountAdjacent(d.x, d.y, (xx, yy) => freshwater[xx, yy] || tiles[xx, yy] == "water");
            }
            else if (d.type == "campus")
            {
                science += CountAdjacent(d.x, d.y, (xx, yy) => tiles[xx, yy] == "mountain");
                science += CountAdjacent(d.x, d.y, (xx, yy) => tiles[xx, yy] == "forest");
            }
        }

        if (c.buildings.Contains("market")) gold += 3;
        if (c.buildings.Contains("library")) science += 3;

        var pl = players[c.owner];
        if (pl.wonders.Contains("pyramids")) production += 2f;
        if (pl.wonders.Contains("great_library")) science += 3f;
        if (pl.wonders.Contains("stonehenge")) culture += 3f;

        // Amenities vs population: content cities produce more, unhappy ones less.
        int happiness = c.amenities - c.population / 2;
        float mult = happiness >= 0 ? 1.10f : 0.85f;
        production *= mult; gold *= mult; science *= mult; culture *= mult;

        production *= 1f + PolicyBonus(c.owner, "production");
        gold *= 1f + PolicyBonus(c.owner, "gold");
        science *= 1f + PolicyBonus(c.owner, "science");
        culture *= 1f + PolicyBonus(c.owner, "culture");

        if (gov == "autocracy") production += 1f;

        // Golden ages boost every yield; dark ages dampen them.
        float ageMult = pl.goldenTurns > 0 ? 1.15f : (pl.darkTurns > 0 ? 0.9f : 1f);
        production *= ageMult; gold *= ageMult; science *= ageMult; culture *= ageMult;

        c.productionPerTurn = production;
        c.goldPerTurn = gold;
        c.sciencePerTurn = science;
        c.culturePerTurn = culture;
    }

    void GrowCity(CityData c)
    {
        if (c.population >= c.housing)
        {
            // No room to grow: food stalls at just under the threshold instead of piling up.
            c.food = Mathf.Min(c.food, c.foodToGrow - 1);
            return;
        }
        c.food += 2 + c.population + (c.buildings.Contains("granary") ? 2 : 0);
        if (c.food >= c.foodToGrow)
        {
            c.food -= c.foodToGrow;
            c.population += 1;
            c.foodToGrow = FoodToGrow(c.population);
        }
    }

    void UpdateLoyalty(CityData c)
    {
        int owned = cities.Count(o => o.owner == c.owner && (Mathf.Abs(o.x - c.x) + Mathf.Abs(o.y - c.y)) <= LOYALTY_RADIUS);
        int enemy = cities.Count(o => o.owner != c.owner && (Mathf.Abs(o.x - c.x) + Mathf.Abs(o.y - c.y)) <= LOYALTY_RADIUS);
        float pressure = Mathf.Clamp((owned - 1) - enemy * 1.5f, -3f, 3f);
        c.loyalty = Mathf.Clamp(c.loyalty + pressure, 0f, 100f);

        if (c.loyalty <= 0f)
        {
            int oldOwner = c.owner;
            c.owner = 1 - c.owner;
            c.loyalty = 40f;
            statusText = $"충성심 붕괴 — {c.name} 도시가 반란을 일으켜 {(c.owner == 0 ? "우리" : "적")} 편으로 넘어왔습니다!";
            if (oldOwner == 0) RevealAroundPlayer0();
            CheckGameOver();
        }
    }

    UnitData UnitAt(int x, int y) => units.FirstOrDefault(u => u.x == x && u.y == y);
    CityData CityAt(int x, int y) => cities.FirstOrDefault(c => c.x == x && c.y == y);
    UnitData FindUnit(int id) => units.FirstOrDefault(u => u.id == id);
    CityData FindCity(int id) => cities.FirstOrDefault(c => c.id == id);

    bool IsAdjacent(int x1, int y1, int x2, int y2)
    {
        int dx = Mathf.Abs(x1 - x2);
        int dy = Mathf.Abs(y1 - y2);
        return dx <= 1 && dy <= 1 && (dx + dy) > 0; // 8-directional, including diagonals
    }
    bool InBounds(int x, int y) => x >= 0 && x < GRID_W && y >= 0 && y < GRID_H;
    int ChebyshevDist(int x1, int y1, int x2, int y2) => Mathf.Max(Mathf.Abs(x1 - x2), Mathf.Abs(y1 - y2));
    int UnitRange(string type) => type == "archer" ? 2 : 1;

    // ---------------- Fog of war ----------------
    // `visible` is fully recomputed every call so tiles a unit walks away from correctly
    // drop out of live sight again; `revealed` only ever gains tiles (permanent memory).
    void RevealAroundPlayer0()
    {
        visible = new bool[GRID_W, GRID_H];
        foreach (var u in units) if (u.owner == 0) RevealRadius(u.x, u.y);
        foreach (var c in cities) if (c.owner == 0) RevealRadius(c.x, c.y);
    }

    void RevealRadius(int cx, int cy)
    {
        for (int y = cy - VISION_RADIUS; y <= cy + VISION_RADIUS; y++)
            for (int x = cx - VISION_RADIUS; x <= cx + VISION_RADIUS; x++)
                if (InBounds(x, y)) { revealed[x, y] = true; visible[x, y] = true; }
    }

    // ---------------- Click handling ----------------
    void HandleTileClick(int gx, int gy)
    {
        if (gameOver || currentPlayer != 0) return;

        var clickedUnit = UnitAt(gx, gy);
        var clickedCity = CityAt(gx, gy);

        if (selectedUnitId == -1 && selectedCityId == -1)
        {
            if (clickedUnit != null && clickedUnit.owner == 0)
            {
                selectedUnitId = clickedUnit.id;
                statusText = $"{TypeName(clickedUnit.type)} 선택됨. 인접한 타일을 클릭해 이동/공격하세요.";
            }
            else if (clickedCity != null && (clickedCity.owner == 0 || clickedCity.owner == 2))
            {
                selectedCityId = clickedCity.id;
                statusText = $"{clickedCity.name} 선택됨.";
            }
            return;
        }

        if (selectedUnitId != -1)
        {
            var u = FindUnit(selectedUnitId);
            if (u == null) { selectedUnitId = -1; return; }

            if (u.type == "trader" && clickedCity != null && CityAt(u.x, u.y)?.id != clickedCity.id)
            {
                TryEstablishTradeRoute(u, clickedCity);
                selectedUnitId = -1;
                return;
            }

            if (u.x == gx && u.y == gy)
            {
                selectedUnitId = -1;
                statusText = "";
                return;
            }

            bool adjacent = IsAdjacent(u.x, u.y, gx, gy);
            bool passable = GetTile(gx, gy) != "water" && GetTile(gx, gy) != "mountain";
            bool canMoveHere = u.movesLeft > 0 && adjacent && passable;

            // Ranged units (currently just the archer) can strike a target within their
            // range without moving onto/adjacent to it — checked before the move logic
            // below, which is adjacency-only and would otherwise block a ranged shot.
            int range = UnitRange(u.type);
            if (range > 1 && u.movesLeft > 0 && u.attack > 0)
            {
                int dist = ChebyshevDist(u.x, u.y, gx, gy);
                bool inRange = dist > 0 && dist <= range;
                if (inRange && clickedUnit != null && clickedUnit.owner != 0)
                {
                    ResolveCombat(u, clickedUnit);
                    selectedUnitId = -1;
                    return;
                }
                if (inRange && clickedCity != null && clickedCity.owner != 0)
                {
                    CaptureOrAttackCity(u, clickedCity);
                    selectedUnitId = -1;
                    return;
                }
            }

            // Clicking a friendly unit normally reselects it — unless it's a valid move
            // destination, in which case the selected unit(s) merge onto that tile instead.
            if (clickedUnit != null && clickedUnit.owner == 0 && clickedUnit.id != u.id)
            {
                if (canMoveHere)
                {
                    MoveStack(u, gx, gy);
                    statusText = $"{TypeName(u.type)}이(가) {TypeName(clickedUnit.type)}와 결합했습니다.";
                }
                else
                {
                    selectedUnitId = clickedUnit.id;
                    statusText = $"{TypeName(clickedUnit.type)} 선택됨.";
                    return;
                }
            }
            else if (!canMoveHere)
            {
                if (u.movesLeft <= 0) statusText = "이 유닛은 더 이상 이동할 수 없습니다.";
                else if (!adjacent) statusText = "인접한 타일이 아닙니다.";
                else statusText = "통과할 수 없는 지형입니다.";
                return;
            }
            else if (clickedUnit != null && clickedUnit.owner != 0)
            {
                ResolveCombat(u, clickedUnit);
            }
            else if (clickedCity != null && clickedCity.owner != 0)
            {
                CaptureOrAttackCity(u, clickedCity);
            }
            else
            {
                MoveStack(u, gx, gy);
            }
            selectedUnitId = -1;
            return;
        }

        if (selectedCityId != -1)
        {
            selectedCityId = -1;
            statusText = "";
        }
    }

    string TypeName(string type)
    {
        switch (type)
        {
            case "settler": return "정착민";
            case "warrior": return "전사";
            case "archer": return "궁수";
            case "horseman": return "기마병";
            case "swordsman": return "검사";
            case "trader": return "무역상인";
            default: return type;
        }
    }

    void TryEstablishTradeRoute(UnitData trader, CityData target)
    {
        var origin = CityAt(trader.x, trader.y);
        if (origin == null || origin.owner != 0) { statusText = "무역로는 내 도시 안에서만 개설할 수 있습니다."; return; }
        if (origin.id == target.id) { statusText = "같은 도시입니다."; return; }
        tradeRoutes.Add(new TradeRoute { originCityId = origin.id, destCityId = target.id, turnsLeft = 15 });
        units.Remove(trader);
        statusText = $"{origin.name} → {target.name} 무역로를 개설했습니다!";
    }

    // Moves the given unit, plus any other units of the same owner standing on its
    // current tile (a merged stack — e.g. a warrior escorting a settler), together.
    void MoveStack(UnitData mover, int gx, int gy)
    {
        int fromX = mover.x, fromY = mover.y;
        var stack = units.Where(x => x.owner == mover.owner && x.x == fromX && x.y == fromY && x.movesLeft > 0).ToList();
        foreach (var su in stack)
        {
            su.x = gx;
            su.y = gy;
            su.movesLeft -= 1;
        }
        if (mover.owner == 0) RevealAroundPlayer0(); // full recompute so the vacated tile drops out of live sight
    }

    int EffectiveAttack(UnitData u) => u.attack + (u.attack > 0 && players[u.owner].government == "oligarchy" ? 2 : 0);

    void ResolveCombat(UnitData attacker, UnitData defender)
    {
        if (attacker.attack <= 0) { statusText = "이 유닛은 공격할 수 없습니다."; return; }
        defender.hp -= EffectiveAttack(attacker) + Random.Range(0, 3);
        if (defender.hp <= 0)
        {
            units.Remove(defender);
            statusText = $"적 {TypeName(defender.type)} 파괴!";
            attacker.movesLeft -= 1;
        }
        else
        {
            attacker.hp -= Mathf.Max(EffectiveAttack(defender) - 1, 1);
            statusText = $"전투! 적 {TypeName(defender.type)} 체력: {defender.hp}";
            if (attacker.hp <= 0)
            {
                units.Remove(attacker);
                statusText += " 내 유닛이 파괴되었습니다.";
            }
            else attacker.movesLeft -= 1;
        }
    }

    void CaptureOrAttackCity(UnitData attacker, CityData city)
    {
        if (attacker.attack <= 0) { statusText = "이 유닛은 도시를 공격할 수 없습니다."; return; }
        city.hp -= EffectiveAttack(attacker) + Random.Range(0, 3);
        attacker.movesLeft -= 1;
        if (city.hp <= 0)
        {
            bool wasCityState = city.owner == 2;
            city.owner = attacker.owner;
            city.hp = city.maxHp / 2;
            city.loyalty = 30f;
            RecomputeCityYield(city);
            statusText = wasCityState ? $"{city.name} 도시국가를 정복했습니다!" : $"{city.name} 도시를 점령했습니다!";
            if (attacker.owner == 0) RevealAroundPlayer0();
            CheckGameOver();
        }
        else statusText = $"{city.name} 공격 중. 체력: {city.hp}";
    }

    // ---------------- Production queue ----------------
    void EnqueueBuild(CityData c, string buildableId)
    {
        var def = GameData.FindBuildable(buildableId);
        if (def == null) return;
        if (def.reqTech != null && !players[c.owner].techs.Contains(def.reqTech)) { statusText = "필요 기술이 없습니다."; return; }
        if (def.kind == BuildKind.Building && c.buildings.Contains(def.id)) { statusText = "이미 건설된 건물입니다."; return; }
        if (def.kind == BuildKind.District && c.districts.Any(d => d.type == def.id)) { statusText = "이미 건설된 구역입니다."; return; }
        if (def.kind == BuildKind.Wonder && wonderOwner.ContainsKey(def.id)) { statusText = "이미 다른 문명이 건설한 불가사의입니다."; return; }
        c.queue.Add(new BuildOrder { id = def.id, cost = def.cost, progress = 0 });
        statusText = $"{def.name} 생산 대기열에 추가되었습니다.";
    }

    void ProcessQueue(CityData c)
    {
        if (c.queue.Count == 0) return;
        var order = c.queue[0];
        order.progress += c.productionPerTurn;
        if (order.progress >= order.cost)
        {
            c.queue.RemoveAt(0);
            CompleteBuild(c, order);
        }
    }

    void CompleteBuild(CityData c, BuildOrder order)
    {
        var def = GameData.FindBuildable(order.id);
        if (def == null) return;
        switch (def.kind)
        {
            case BuildKind.Unit:
                SpawnNearCity(c.owner, c, def.id);
                break;
            case BuildKind.Building:
                c.buildings.Add(def.id);
                if (def.id == "walls") { c.maxHp += 15; c.hp += 15; }
                break;
            case BuildKind.District:
                c.districts.Add(new DistrictData { x = c.x, y = c.y, type = def.id });
                break;
            case BuildKind.Wonder:
                players[c.owner].wonders.Add(def.id);
                players[c.owner].eraScore += 10f;
                wonderOwner[def.id] = c.owner;
                break;
        }
        if (c.owner == 0) statusText = $"{c.name}: {def.name} 완성!";
        else if (def.kind == BuildKind.Wonder) statusText = $"상대가 {def.name}을(를) 먼저 건설했습니다.";
    }

    void RushBuyFront(CityData c)
    {
        if (c.queue.Count == 0) return;
        var order = c.queue[0];
        float remaining = Mathf.Max(0, order.cost - order.progress);
        int goldCost = Mathf.CeilToInt(remaining * RUSH_BUY_GOLD_PER_PRODUCTION);
        if (players[c.owner].gold < goldCost) { statusText = "골드가 부족합니다."; return; }
        players[c.owner].gold -= goldCost;
        c.queue.RemoveAt(0);
        CompleteBuild(c, order);
    }

    void SpawnNearCity(int owner, CityData c, string type)
    {
        var offsets = new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
        foreach (var off in offsets)
        {
            int xx = c.x + off.x, yy = c.y + off.y;
            if (InBounds(xx, yy) && UnitAt(xx, yy) == null && GetTile(xx, yy) != "water" && GetTile(xx, yy) != "mountain")
            {
                AddUnit(owner, type, xx, yy);
                return;
            }
        }
        AddUnit(owner, type, c.x, c.y);
    }

    // ---------------- Tech / civic research ----------------
    void SelectTech(string id)
    {
        var pl = players[0];
        var t = GameData.FindTech(id);
        if (t == null || pl.techs.Contains(id) || !GameData.PrereqsMet(t.prereqs, pl.techs)) return;
        pl.currentTech = id;
    }

    void SelectCivic(string id)
    {
        var pl = players[0];
        var c = GameData.FindCivic(id);
        if (c == null || pl.civics.Contains(id) || !GameData.PrereqsMet(c.prereqs, pl.civics)) return;
        pl.currentCivic = id;
    }

    void AutoPickTech(PlayerState pl)
    {
        var pick = GameData.Techs.Where(t => !pl.techs.Contains(t.id) && GameData.PrereqsMet(t.prereqs, pl.techs)).OrderBy(t => t.cost).FirstOrDefault();
        pl.currentTech = pick?.id;
    }

    void AutoPickCivic(PlayerState pl)
    {
        var pick = GameData.Civics.Where(c => !pl.civics.Contains(c.id) && GameData.PrereqsMet(c.prereqs, pl.civics)).OrderBy(c => c.cost).FirstOrDefault();
        pl.currentCivic = pick?.id;
    }

    void AdvanceResearch(int p)
    {
        var pl = players[p];
        if (p == 1 && pl.currentTech == null) AutoPickTech(pl);
        if (p == 1 && pl.currentCivic == null) AutoPickCivic(pl);

        if (pl.currentTech != null)
        {
            var t = GameData.FindTech(pl.currentTech);
            if (pl.scienceStock >= t.cost)
            {
                pl.scienceStock -= t.cost;
                pl.techs.Add(t.id);
                pl.eraScore += 5f;
                if (p == 0) statusText = $"'{t.name}' 연구 완료! {t.unlockDesc}";
                pl.currentTech = null;
                if (p == 1) AutoPickTech(pl);
            }
        }
        if (pl.currentCivic != null)
        {
            var c = GameData.FindCivic(pl.currentCivic);
            if (pl.cultureStock >= c.cost)
            {
                pl.cultureStock -= c.cost;
                pl.civics.Add(c.id);
                pl.eraScore += 5f;
                if (p == 0) statusText = $"'{c.name}' 시민 완료! {c.unlockDesc}";
                pl.currentCivic = null;
                if (p == 1) AutoPickCivic(pl);
            }
        }
    }

    // ---------------- Government / policy ----------------
    void SelectGovernment(string id)
    {
        var pl = players[0];
        var g = GameData.FindGov(id);
        if (g == null) return;
        if (g.reqCivic != null && !pl.civics.Contains(g.reqCivic)) return;
        pl.government = id;
        while (pl.policies.Count > g.slots) pl.policies.Remove(pl.policies.Last());
    }

    void TogglePolicy(string id)
    {
        var pl = players[0];
        var pd = GameData.FindPolicy(id);
        if (pd == null) return;
        if (pd.reqCivic != null && !pl.civics.Contains(pd.reqCivic)) return;
        if (pl.policies.Contains(id)) { pl.policies.Remove(id); return; }
        var gov = GameData.FindGov(pl.government);
        if (pl.policies.Count >= gov.slots) { statusText = "정책 카드 슬롯이 가득 찼습니다."; return; }
        pl.policies.Add(id);
    }

    // ---------------- Turn flow ----------------
    void OnEndTurn()
    {
        if (gameOver || currentPlayer != 0 || aiTurnRunning) return;
        selectedUnitId = -1;
        selectedCityId = -1;
        statusText = "";
        currentPlayer = 1;
        StartCoroutine(EndTurnSequence());
    }

    IEnumerator EndTurnSequence()
    {
        aiTurnRunning = true;
        yield return new WaitForSeconds(0.35f);
        RunAiTurn();
        EndOfTurnUpkeep();
        currentPlayer = 0;
        turnNumber += 1;
        foreach (var u in units) if (u.owner == 0) u.movesLeft = u.maxMoves;
        RevealAroundPlayer0();
        CheckGameOver();
        aiTurnRunning = false;
    }

    void EndOfTurnUpkeep()
    {
        foreach (var c in cities.ToList())
        {
            if (c.owner == 2) continue; // city-states don't grow/produce like player cities
            GrowCity(c);
            UpdateLoyalty(c);
            RecomputeCityYield(c);
            players[c.owner].gold += Mathf.RoundToInt(c.goldPerTurn);
            players[c.owner].scienceStock += c.sciencePerTurn;
            players[c.owner].cultureStock += c.culturePerTurn;
            ProcessQueue(c);
        }

        foreach (var kv in cityStateDefById)
        {
            var csCity = FindCity(kv.Key);
            if (csCity == null || csCity.owner != 2) continue; // conquered city-states become normal cities
            int suz = SuzerainOf(kv.Key);
            if (suz < 0) continue;
            var def = kv.Value;
            var pl = players[suz];
            if (def.bonusType == "gold") pl.gold += Mathf.RoundToInt(def.bonusAmount);
            else if (def.bonusType == "science") pl.scienceStock += def.bonusAmount;
            else if (def.bonusType == "culture") pl.cultureStock += def.bonusAmount;
        }

        for (int p = 0; p < 2; p++)
        {
            AccumulateGreatPeople(p);
            players[p].goldenTurns = Mathf.Max(0, players[p].goldenTurns - 1);
            players[p].darkTurns = Mathf.Max(0, players[p].darkTurns - 1);
        }

        foreach (var route in tradeRoutes.ToList())
        {
            route.turnsLeft -= 1;
            var origin = FindCity(route.originCityId);
            if (origin != null && origin.owner == 0)
            {
                players[0].gold += 3;
                players[0].scienceStock += 1f;
            }
            if (route.turnsLeft <= 0) tradeRoutes.Remove(route);
        }

        if (turnNumber % 20 == 0)
        {
            EvaluateEra(players[0]);
            EvaluateEra(players[1]);
        }

        AdvanceResearch(0);
        AdvanceResearch(1);
        foreach (var u in units) if (u.owner == 1) u.movesLeft = u.maxMoves;
    }

    void EvaluateEra(PlayerState pl)
    {
        if (pl.eraScore < 30) pl.darkTurns = 20;
        else if (pl.eraScore > 60) pl.goldenTurns = 20;
        pl.eraScore = 0;
        pl.era = Mathf.Min(pl.era + 1, 2);
    }

    void AccumulateGreatPeople(int p)
    {
        var pl = players[p];
        foreach (var c in cities.Where(c => c.owner == p))
        {
            if (c.districts.Any(d => d.type == "campus")) pl.gsPoints += c.sciencePerTurn * 0.3f;
            if (c.districts.Any(d => d.type == "industrial")) pl.gePoints += c.productionPerTurn * 0.3f;
            if (c.districts.Any(d => d.type == "commercial")) pl.gmPoints += c.goldPerTurn * 0.3f;
        }
        if (pl.gsPoints >= pl.gpThreshold) RecruitGreatPerson(p, "scientist");
        else if (pl.gePoints >= pl.gpThreshold) RecruitGreatPerson(p, "engineer");
        else if (pl.gmPoints >= pl.gpThreshold) RecruitGreatPerson(p, "merchant");
    }

    void RecruitGreatPerson(int p, string kind)
    {
        var pl = players[p];
        string msg;
        switch (kind)
        {
            case "scientist":
                pl.scienceStock += 40f;
                pl.gsPoints -= pl.gpThreshold;
                msg = "위대한 과학자를 영입했습니다! 과학 +40";
                break;
            case "engineer":
                var target = cities.FirstOrDefault(c => c.owner == p && c.queue.Count > 0);
                if (target != null) target.queue[0].progress += 40f;
                else pl.gold += 20;
                pl.gePoints -= pl.gpThreshold;
                msg = "위대한 기술자를 영입했습니다! 생산력 +40";
                break;
            default:
                pl.gold += 60;
                pl.gmPoints -= pl.gpThreshold;
                msg = "위대한 상인을 영입했습니다! 골드 +60";
                break;
        }
        pl.gpThreshold += 40;
        if (p == 0) statusText = msg;
    }

    // ---------------- Simple AI ----------------
    void RunAiTurn()
    {
        var aiUnits = units.Where(u => u.owner == 1).ToList();
        foreach (var u in aiUnits)
        {
            if (!units.Contains(u)) continue;
            AiActUnit(u);
        }

        foreach (var c in cities.Where(c => c.owner == 1).ToList())
        {
            if (c.queue.Count > 0) continue;
            string pick = PickAiBuildOrder(c);
            if (pick != null) EnqueueBuild(c, pick);
        }

        if (players[1].gold >= 60 && Random.value < 0.15f && cityStateDefById.Count > 0)
        {
            var csId = cityStateDefById.Keys.ElementAt(Random.Range(0, cityStateDefById.Count));
            SendEnvoy(csId, 1);
        }
    }

    string PickAiMilitaryUnit()
    {
        var pl = players[1];
        var options = new List<string> { "warrior" };
        if (pl.techs.Contains("bronze_working")) options.Add("archer");
        if (pl.techs.Contains("horseback_riding")) options.Add("horseman");
        if (pl.techs.Contains("iron_working")) options.Add("swordsman");
        return options[Random.Range(0, options.Count)];
    }

    // Without this, the AI only ever queued settlers/military and never touched
    // districts, buildings or wonders — leaving every wonder free for the player
    // to grab uncontested and the AI's cities permanently under-developed.
    string PickAiBuildOrder(CityData c)
    {
        var pl = players[1];
        if (cities.Count(o => o.owner == 1) < 3 && Random.value < 0.3f) return "settler";

        var available = GameData.Buildables.Where(b =>
            (b.reqTech == null || pl.techs.Contains(b.reqTech)) &&
            !(b.kind == BuildKind.Building && c.buildings.Contains(b.id)) &&
            !(b.kind == BuildKind.District && c.districts.Any(d => d.type == b.id)) &&
            !(b.kind == BuildKind.Wonder && wonderOwner.ContainsKey(b.id))
        ).ToList();

        var wonder = available.Where(b => b.kind == BuildKind.Wonder).OrderBy(_ => Random.value).FirstOrDefault();
        if (wonder != null && Random.value < 0.25f) return wonder.id;

        var district = available.Where(b => b.kind == BuildKind.District).OrderBy(_ => Random.value).FirstOrDefault();
        if (district != null && Random.value < 0.35f) return district.id;

        var building = available.Where(b => b.kind == BuildKind.Building).OrderBy(_ => Random.value).FirstOrDefault();
        if (building != null && Random.value < 0.3f) return building.id;

        return PickAiMilitaryUnit();
    }

    void AiActUnit(UnitData u)
    {
        if (u.type == "settler")
        {
            if (CityAt(u.x, u.y) == null && Random.value < 0.35f)
            {
                AddCity(1, u.x, u.y);
                units.Remove(u);
                return;
            }
            AiMoveRandom(u);
            return;
        }

        object target = FindNearestEnemyTarget(u);
        if (target == null) { AiMoveRandom(u); return; }

        int tx, ty;
        if (target is UnitData tu) { tx = tu.x; ty = tu.y; }
        else { var tc = (CityData)target; tx = tc.x; ty = tc.y; }

        int dist = ChebyshevDist(u.x, u.y, tx, ty);
        if (dist <= UnitRange(u.type))
        {
            var eu = UnitAt(tx, ty);
            var ec = CityAt(tx, ty);
            if (eu != null && eu.owner == 0) ResolveCombat(u, eu);
            else if (ec != null && ec.owner == 0) CaptureOrAttackCity(u, ec);
            return;
        }
        AiMoveToward(u, tx, ty);
    }

    object FindNearestEnemyTarget(UnitData u)
    {
        object best = null;
        int bestDist = int.MaxValue;
        foreach (var eu in units)
        {
            if (eu.owner != 0) continue;
            int d = Mathf.Abs(eu.x - u.x) + Mathf.Abs(eu.y - u.y);
            if (d < bestDist) { bestDist = d; best = eu; }
        }
        foreach (var ec in cities)
        {
            if (ec.owner != 0) continue;
            int d = Mathf.Abs(ec.x - u.x) + Mathf.Abs(ec.y - u.y);
            if (d < bestDist) { bestDist = d; best = ec; }
        }
        return best;
    }

    void AiMoveToward(UnitData u, int tx, int ty)
    {
        int dx = (int)Mathf.Sign(tx - u.x);
        int dy = (int)Mathf.Sign(ty - u.y);
        var candidates = new List<Vector2Int>();
        // Prefer the diagonal step when it heads straight at the target.
        if (dx != 0 && dy != 0) candidates.Add(new Vector2Int(u.x + dx, u.y + dy));
        if (tx != u.x) candidates.Add(new Vector2Int(u.x + dx, u.y));
        if (ty != u.y) candidates.Add(new Vector2Int(u.x, u.y + dy));
        foreach (var c in candidates)
        {
            if (InBounds(c.x, c.y) && GetTile(c.x, c.y) != "water" && GetTile(c.x, c.y) != "mountain" && UnitAt(c.x, c.y) == null)
            {
                u.x = c.x; u.y = c.y;
                return;
            }
        }
    }

    void AiMoveRandom(UnitData u)
    {
        var dirs = new List<Vector2Int> {
            new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
            new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1)
        };
        for (int i = dirs.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (dirs[i], dirs[j]) = (dirs[j], dirs[i]);
        }
        foreach (var d in dirs)
        {
            int xx = u.x + d.x, yy = u.y + d.y;
            if (InBounds(xx, yy) && GetTile(xx, yy) != "water" && GetTile(xx, yy) != "mountain" && UnitAt(xx, yy) == null)
            {
                u.x = xx; u.y = yy;
                return;
            }
        }
    }

    // ---------------- Win condition ----------------
    const int TURN_LIMIT = 150;

    void CheckGameOver()
    {
        bool p0Alive = cities.Any(c => c.owner == 0) || units.Any(u => u.owner == 0 && u.type == "settler");
        bool p1Alive = cities.Any(c => c.owner == 1) || units.Any(u => u.owner == 1 && u.type == "settler");
        if (!p0Alive) { gameOver = true; statusText = "게임 오버 — AI가 승리했습니다."; return; }
        if (!p1Alive) { gameOver = true; statusText = "승리 — 적을 전멸시켰습니다!"; return; }

        if (turnNumber >= TURN_LIMIT)
        {
            float score0 = ComputeScore(0), score1 = ComputeScore(1);
            gameOver = true;
            if (score0 > score1) statusText = $"시대 종료 (턴 {TURN_LIMIT}) — 점수 승리! (내 점수 {score0:0} vs 상대 {score1:0})";
            else if (score1 > score0) statusText = $"시대 종료 (턴 {TURN_LIMIT}) — 패배. (내 점수 {score0:0} vs 상대 {score1:0})";
            else statusText = $"시대 종료 (턴 {TURN_LIMIT}) — 무승부! (점수 {score0:0} 동률)";
        }
    }

    // A simple Civ-style score: cities/population show up, but era-defining
    // achievements (tech, civics, wonders) are weighted heavily on purpose so
    // turtling behind walls isn't automatically the best long-game strategy.
    float ComputeScore(int owner)
    {
        var pl = players[owner];
        int pop = cities.Where(c => c.owner == owner).Sum(c => c.population);
        return pop * 3f + pl.techs.Count * 10f + pl.civics.Count * 8f + pl.wonders.Count * 15f + pl.era * 20f;
    }

    // ---------------- Rendering (IMGUI) ----------------
    void EnsureStyles()
    {
        if (koreanFont == null)
        {
            // Built-in IMGUI fonts have no Hangul glyphs; pull a Korean-capable font from the OS.
            koreanFont = Font.CreateDynamicFontFromOSFont("Malgun Gothic", 16);
        }
        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, normal = { textColor = Color.white }, font = koreanFont };
            smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = new Color(0.85f, 0.85f, 0.85f) }, font = koreanFont };
            bigLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, normal = { textColor = Color.white }, font = koreanFont };
            goldStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.84f, 0.25f) },
                font = koreanFont
            };
            goldShadowStyle = new GUIStyle(goldStyle) { normal = { textColor = new Color(0.25f, 0.15f, 0f, 0.8f) } };
        }
    }

    void OnGUI()
    {
        EnsureStyles();
        GUI.skin.font = koreanFont;

        float barW = BOARD_X * 2 + GRID_W * TILE_SIZE;

        // Top bar
        GUI.color = new Color(0.12f, 0.12f, 0.14f);
        GUI.DrawTexture(new Rect(0, 0, barW, 90), flatTex);
        GUI.color = Color.white;

        var pl0 = players[0];
        var resRect = new Rect(16, 6, 700, 26);
        string resText = $"골드 {pl0.gold}   과학 {pl0.scienceStock:0}   문화 {pl0.cultureStock:0}";
        GUI.Label(new Rect(resRect.x + 1, resRect.y + 1, resRect.width, resRect.height), resText, goldShadowStyle);
        GUI.Label(resRect, resText, goldStyle);

        string techLabel = pl0.currentTech != null ? $"연구 중: {GameData.FindTech(pl0.currentTech).name} ({pl0.scienceStock:0}/{GameData.FindTech(pl0.currentTech).cost})" : "연구 대상 없음 — 기술 선택 필요";
        string civicLabel = pl0.currentCivic != null ? $"시민 중: {GameData.FindCivic(pl0.currentCivic).name} ({pl0.cultureStock:0}/{GameData.FindCivic(pl0.currentCivic).cost})" : "시민 대상 없음 — 시민 선택 필요";
        string ageLabel = pl0.goldenTurns > 0 ? $"황금기 ({pl0.goldenTurns}턴)" : pl0.darkTurns > 0 ? $"암흑기 ({pl0.darkTurns}턴)" : "평시";
        GUI.Label(new Rect(16, 32, 900, 22), $"턴 {turnNumber}  |  {(currentPlayer == 0 ? "내 턴" : "AI 턴...")}  |  {techLabel}  |  {civicLabel}  |  {ageLabel}", smallStyle);
        GUI.Label(new Rect(16, 54, 900, 28), statusText, labelStyle);

        if (GUI.Button(new Rect(barW - 740, 8, 90, 32), showTechPanel ? "기술 ▲" : "기술 ▼")) { showTechPanel = !showTechPanel; showCivicPanel = false; showGovPanel = false; showGreatPanel = false; }
        if (GUI.Button(new Rect(barW - 640, 8, 90, 32), showCivicPanel ? "시민 ▲" : "시민 ▼")) { showCivicPanel = !showCivicPanel; showTechPanel = false; showGovPanel = false; showGreatPanel = false; }
        if (GUI.Button(new Rect(barW - 540, 8, 110, 32), showGovPanel ? "정부/정책 ▲" : "정부/정책 ▼")) { showGovPanel = !showGovPanel; showTechPanel = false; showCivicPanel = false; showGreatPanel = false; }
        if (GUI.Button(new Rect(barW - 420, 8, 90, 32), showGreatPanel ? "위인 ▲" : "위인 ▼")) { showGreatPanel = !showGreatPanel; showTechPanel = false; showCivicPanel = false; showGovPanel = false; }

        if (GUI.Button(new Rect(barW - 140, 8, 120, 32), "턴 종료") && !gameOver)
        {
            OnEndTurn();
            showTechPanel = showCivicPanel = showGovPanel = showGreatPanel = false;
        }

        if (!gameOver && currentPlayer == 0 && selectedUnitId != -1)
        {
            var u = FindUnit(selectedUnitId);
            if (u != null && u.type == "settler" && u.owner == 0 && u.movesLeft > 0 && CityAt(u.x, u.y) == null)
            {
                if (GUI.Button(new Rect(barW - 280, 8, 120, 32), "도시 건설"))
                    OnFoundCity();
            }
        }

        // Board terrain
        for (int y = 0; y < GRID_H; y++)
        {
            for (int x = 0; x < GRID_W; x++)
            {
                var rect = new Rect(BOARD_X + x * TILE_SIZE, BOARD_Y + y * TILE_SIZE, TILE_SIZE - 2, TILE_SIZE - 2);
                if (revealed[x, y])
                {
                    // Explored-but-not-currently-visible tiles are shown dimmed (remembered
                    // terrain), same as classic 4X "fog" — only tiles in live sight are full brightness.
                    Color shade = visible[x, y] ? Color.white : new Color(0.42f, 0.42f, 0.42f);
                    string t = tiles[x, y];
                    if (t == "water")
                    {
                        // The pack's water art is a single lake graphic cut into bordered
                        // pieces, so tiling it produces a patchwork of separate "ponds".
                        // A flat fill reads as one continuous sea instead.
                        GUI.color = WaterColor * shade;
                        GUI.DrawTexture(rect, flatTex);
                    }
                    else
                    {
                        GUI.color = t == "hills" ? HillsTint * shade : shade;
                        // Forest/mountain art is a transparent overlay sprite, so draw grass underneath first.
                        if (t == "forest" || t == "mountain")
                        {
                            var baseTex = terrainTex["plains"];
                            if (baseTex != null) GUI.DrawTexture(rect, baseTex);
                            GUI.color = shade;
                        }
                        var tex = terrainTex[t];
                        if (tex != null) GUI.DrawTexture(rect, tex);
                        else GUI.DrawTexture(rect, flatTex);
                    }
                }
                else
                {
                    GUI.color = FogColor;
                    GUI.DrawTexture(rect, flatTex);
                }
                GUI.color = Color.white;
                if (GUI.Button(rect, "", GUIStyle.none))
                    HandleTileClick(x, y);
            }
        }

        // Cities — remembered (dimmed) once explored, full brightness only in live sight.
        // Ownership shown may be stale if it changed since this tile was last actually seen,
        // same as classic 4X fog behavior.
        foreach (var c in cities)
        {
            if (!revealed[c.x, c.y]) continue;
            var rect = new Rect(BOARD_X + c.x * TILE_SIZE, BOARD_Y + c.y * TILE_SIZE, TILE_SIZE - 2, TILE_SIZE - 2);
            Color shade = visible[c.x, c.y] ? Color.white : new Color(0.42f, 0.42f, 0.42f);
            if (c.owner == 2)
            {
                GUI.color = CityStateColor * shade;
                GUI.DrawTexture(rect, flatTex);
                GUI.color = shade;
                GUI.Label(rect, "국", bigLabelStyle);
            }
            else
            {
                GUI.color = shade;
                var tex = cityTex[c.owner];
                if (tex != null) GUI.DrawTexture(rect, tex);
            }
            GUI.color = Color.white;

            // District badges: a small colored+lettered chip per district the city owns,
            // lined up along the top edge of the city tile.
            for (int i = 0; i < c.districts.Count; i++)
            {
                var d = c.districts[i];
                var badge = new Rect(rect.x + 2 + i * 12, rect.y + 2, 10, 10);
                GUI.color = (DistrictColors.TryGetValue(d.type, out var dc) ? dc : Color.gray) * shade;
                GUI.DrawTexture(badge, flatTex);
            }
            GUI.color = Color.white;
            if (selectedCityId == c.id) DrawOutline(rect, Color.yellow);
        }

        // Units — only ever shown while actually in line of sight (no memory/ghosting),
        // matching standard 4X fog of war: you don't know where an unseen enemy unit is now.
        foreach (var u in units)
        {
            if (!visible[u.x, u.y]) continue;
            var rect = new Rect(BOARD_X + u.x * TILE_SIZE, BOARD_Y + u.y * TILE_SIZE, TILE_SIZE - 2, TILE_SIZE - 2);
            if (unitTex.TryGetValue(u.type, out var texArr) && texArr[u.owner] != null)
            {
                GUI.DrawTexture(rect, texArr[u.owner]);
            }
            else
            {
                // Fallback for any unit type without art: a player-tinted swatch and a letter.
                GUI.color = UnitColors.TryGetValue(u.type, out var uc) ? uc : Color.gray;
                GUI.DrawTexture(rect, flatTex);
                GUI.color = PlayerColors[u.owner];
                GUI.DrawTexture(new Rect(rect.x, rect.yMax - 6, rect.width, 6), flatTex);
                GUI.color = Color.white;
                GUI.Label(rect, UnitLetters.TryGetValue(u.type, out var ul) ? ul : "?", bigLabelStyle);
            }
            if (u.id == selectedUnitId) DrawOutline(rect, Color.yellow);
        }

        DrawCityPanel();
        if (showTechPanel) DrawTechPanel();
        if (showCivicPanel) DrawCivicPanel();
        if (showGovPanel) DrawGovPanel();
        if (showGreatPanel) DrawGreatPanel();
    }

    void DrawGreatPanel()
    {
        var pl = players[0];
        const float panelW = 320f;
        const float panelH = 220f;
        var rect = new Rect(BOARD_X, BOARD_Y - 4, panelW, panelH);
        DrawPanelBackground(rect);

        float y = rect.y + 6;
        GUI.Label(new Rect(rect.x + 10, y, panelW - 20, 22), "위인 포인트", labelStyle); y += 30;
        GUI.Label(new Rect(rect.x + 10, y, panelW - 20, 20), $"과학자 {pl.gsPoints:0}/{pl.gpThreshold}  (캠퍼스 도시가 누적)", smallStyle); y += 24;
        GUI.Label(new Rect(rect.x + 10, y, panelW - 20, 20), $"기술자 {pl.gePoints:0}/{pl.gpThreshold}  (산업 구역이 누적)", smallStyle); y += 24;
        GUI.Label(new Rect(rect.x + 10, y, panelW - 20, 20), $"상인 {pl.gmPoints:0}/{pl.gpThreshold}  (상업 허브가 누적)", smallStyle); y += 30;
        GUI.Label(new Rect(rect.x + 10, y, panelW - 20, 20), "임계값 도달 시 자동으로 영입됩니다.", smallStyle); y += 24;
        GUI.Label(new Rect(rect.x + 10, y, panelW - 20, 20), $"시대 점수 {pl.eraScore:0} (20턴마다 정산)", smallStyle); y += 24;
        GUI.Label(new Rect(rect.x + 10, y, panelW - 20, 20), $"보유 불가사의: {(pl.wonders.Count == 0 ? "없음" : string.Join(", ", pl.wonders.Select(w => GameData.FindBuildable(w).name)))}", smallStyle);
    }

    void DrawCityPanel()
    {
        if (gameOver || currentPlayer != 0 || selectedCityId == -1) return;
        var c = FindCity(selectedCityId);
        if (c == null) return;
        if (c.owner == 2) { DrawCityStatePanel(c); return; }
        if (c.owner != 0) return;

        var buildOptions = GameData.Buildables.Where(b =>
            (b.reqTech == null || players[0].techs.Contains(b.reqTech)) &&
            !(b.kind == BuildKind.Building && c.buildings.Contains(b.id)) &&
            !(b.kind == BuildKind.District && c.districts.Any(d => d.type == b.id)) &&
            !(b.kind == BuildKind.Wonder && wonderOwner.ContainsKey(b.id))
        ).ToList();

        const float panelW = 260f;
        float panelH = 130f + buildOptions.Count * 26f + (c.queue.Count > 0 ? 50f : 0f);
        var cityRect = new Rect(BOARD_X + c.x * TILE_SIZE, BOARD_Y + c.y * TILE_SIZE, TILE_SIZE - 2, TILE_SIZE - 2);

        float boardRight = BOARD_X + GRID_W * TILE_SIZE;
        float boardBottom = BOARD_Y + GRID_H * TILE_SIZE;

        // Prefer opening to the right of the city; flip to the left if it would run off the board.
        float px = cityRect.xMax + 8;
        if (px + panelW > boardRight) px = cityRect.x - panelW - 8;
        float py = cityRect.y;
        if (py + panelH > boardBottom) py = boardBottom - panelH;
        if (py < BOARD_Y) py = BOARD_Y;

        var panelRect = new Rect(px, py, panelW, panelH);
        GUI.color = new Color(0.10f, 0.10f, 0.13f, 0.96f);
        GUI.DrawTexture(panelRect, flatTex);
        GUI.color = Color.white;
        DrawOutline(panelRect, new Color(0.8f, 0.8f, 0.8f));

        float y = panelRect.y + 6;
        GUI.Label(new Rect(panelRect.x + 10, y, panelW - 20, 24), c.name, labelStyle); y += 24;
        GUI.Label(new Rect(panelRect.x + 10, y, panelW - 20, 20), $"체력 {c.hp}/{c.maxHp}  인구 {c.population}", smallStyle); y += 20;
        GUI.Label(new Rect(panelRect.x + 10, y, panelW - 20, 20), $"골드 {c.goldPerTurn:0.0}  생산 {c.productionPerTurn:0.0}", smallStyle); y += 18;
        GUI.Label(new Rect(panelRect.x + 10, y, panelW - 20, 20), $"과학 {c.sciencePerTurn:0.0}  문화 {c.culturePerTurn:0.0}", smallStyle); y += 22;

        if (c.queue.Count > 0)
        {
            var order = c.queue[0];
            var def = GameData.FindBuildable(order.id);
            GUI.Label(new Rect(panelRect.x + 10, y, panelW - 20, 20), $"생산 중: {def.name} ({order.progress:0}/{order.cost})", smallStyle);
            y += 20;
            if (GUI.Button(new Rect(panelRect.x + 10, y, panelW - 20, 26), $"골드로 즉시 완성 ({Mathf.CeilToInt(Mathf.Max(0, order.cost - order.progress) * RUSH_BUY_GOLD_PER_PRODUCTION)})"))
                RushBuyFront(c);
            y += 30;
        }

        foreach (var def in buildOptions)
        {
            string kindTag = def.kind == BuildKind.Unit ? "유닛" : def.kind == BuildKind.Building ? "건물" : def.kind == BuildKind.District ? "구역" : "불가사의";
            if (GUI.Button(new Rect(panelRect.x + 10, y, panelW - 20, 24), $"[{kindTag}] {def.name} ({def.cost})"))
                EnqueueBuild(c, def.id);
            y += 26;
        }
    }

    void DrawCityStatePanel(CityData c)
    {
        var def = cityStateDefById.TryGetValue(c.id, out var d) ? d : null;
        const float panelW = 240f;
        const float panelH = 150f;
        var cityRect = new Rect(BOARD_X + c.x * TILE_SIZE, BOARD_Y + c.y * TILE_SIZE, TILE_SIZE - 2, TILE_SIZE - 2);
        float boardRight = BOARD_X + GRID_W * TILE_SIZE;
        float boardBottom = BOARD_Y + GRID_H * TILE_SIZE;
        float px = cityRect.xMax + 8;
        if (px + panelW > boardRight) px = cityRect.x - panelW - 8;
        float py = cityRect.y;
        if (py + panelH > boardBottom) py = boardBottom - panelH;
        if (py < BOARD_Y) py = BOARD_Y;

        var panelRect = new Rect(px, py, panelW, panelH);
        DrawPanelBackground(panelRect);

        float y = panelRect.y + 6;
        GUI.Label(new Rect(panelRect.x + 10, y, panelW - 20, 24), $"{c.name} (도시국가)", labelStyle); y += 26;
        if (def != null) GUI.Label(new Rect(panelRect.x + 10, y, panelW - 20, 20), $"종주국 보너스: +{def.bonusAmount:0} {YieldKorean(def.bonusType)}/턴", smallStyle);
        y += 22;
        int suz = SuzerainOf(c.id);
        players[0].envoys.TryGetValue(c.id, out int myEnvoys);
        players[1].envoys.TryGetValue(c.id, out int theirEnvoys);
        GUI.Label(new Rect(panelRect.x + 10, y, panelW - 20, 20), $"내 특사 {myEnvoys}  |  상대 특사 {theirEnvoys}", smallStyle); y += 20;
        GUI.Label(new Rect(panelRect.x + 10, y, panelW - 20, 20), suz == -1 ? "종주국 없음" : (suz == 0 ? "종주국: 나" : "종주국: 상대"), smallStyle); y += 26;

        if (GUI.Button(new Rect(panelRect.x + 10, y, panelW - 20, 28), "특사 파견 (30골드)"))
            SendEnvoy(c.id, 0);
    }

    void DrawTechPanel()
    {
        var pl = players[0];
        const float panelW = 340f;
        float panelH = 40f + GameData.Techs.Count * 46f;
        var rect = new Rect(BOARD_X, BOARD_Y - 4, panelW, Mathf.Min(panelH, GRID_H * TILE_SIZE - 4));
        DrawPanelBackground(rect);

        float y = rect.y + 6;
        GUI.Label(new Rect(rect.x + 10, y, panelW - 20, 22), "기술 트리", labelStyle); y += 26;

        foreach (var t in GameData.Techs)
        {
            bool done = pl.techs.Contains(t.id);
            bool locked = !GameData.PrereqsMet(t.prereqs, pl.techs);
            bool current = pl.currentTech == t.id;
            string status = done ? "완료" : current ? $"{pl.scienceStock:0}/{t.cost}" : $"{t.cost} 과학";
            GUI.enabled = !done && !locked;
            if (GUI.Button(new Rect(rect.x + 10, y, panelW - 20, 22), $"{t.name} — {status}"))
                SelectTech(t.id);
            GUI.enabled = true;
            GUI.Label(new Rect(rect.x + 10, y + 22, panelW - 20, 20), locked ? "(선행 기술 필요)" : t.unlockDesc, smallStyle);
            y += 46;
        }
    }

    void DrawCivicPanel()
    {
        var pl = players[0];
        const float panelW = 340f;
        float panelH = 40f + GameData.Civics.Count * 46f;
        var rect = new Rect(BOARD_X, BOARD_Y - 4, panelW, Mathf.Min(panelH, GRID_H * TILE_SIZE - 4));
        DrawPanelBackground(rect);

        float y = rect.y + 6;
        GUI.Label(new Rect(rect.x + 10, y, panelW - 20, 22), "시민 트리", labelStyle); y += 26;

        foreach (var c in GameData.Civics)
        {
            bool done = pl.civics.Contains(c.id);
            bool locked = !GameData.PrereqsMet(c.prereqs, pl.civics);
            bool current = pl.currentCivic == c.id;
            string status = done ? "완료" : current ? $"{pl.cultureStock:0}/{c.cost}" : $"{c.cost} 문화";
            GUI.enabled = !done && !locked;
            if (GUI.Button(new Rect(rect.x + 10, y, panelW - 20, 22), $"{c.name} — {status}"))
                SelectCivic(c.id);
            GUI.enabled = true;
            GUI.Label(new Rect(rect.x + 10, y + 22, panelW - 20, 20), locked ? "(선행 시민 필요)" : c.unlockDesc, smallStyle);
            y += 46;
        }
    }

    void DrawGovPanel()
    {
        var pl = players[0];
        var gov = GameData.FindGov(pl.government);
        const float panelW = 360f;
        float panelH = 40f + GameData.Governments.Count * 44f + 30f + GameData.Policies.Count * 26f;
        var rect = new Rect(BOARD_X, BOARD_Y - 4, panelW, Mathf.Min(panelH, GRID_H * TILE_SIZE - 4));
        DrawPanelBackground(rect);

        float y = rect.y + 6;
        GUI.Label(new Rect(rect.x + 10, y, panelW - 20, 22), "정부", labelStyle); y += 26;
        foreach (var g in GameData.Governments)
        {
            bool locked = g.reqCivic != null && !pl.civics.Contains(g.reqCivic);
            bool active = pl.government == g.id;
            GUI.enabled = !locked;
            if (GUI.Button(new Rect(rect.x + 10, y, panelW - 20, 22), (active ? "[선택됨] " : "") + $"{g.name}"))
                SelectGovernment(g.id);
            GUI.enabled = true;
            GUI.Label(new Rect(rect.x + 10, y + 22, panelW - 20, 18), locked ? "(정치 철학 필요)" : g.bonusDesc, smallStyle);
            y += 44;
        }

        y += 6;
        GUI.Label(new Rect(rect.x + 10, y, panelW - 20, 22), $"정책 카드 ({pl.policies.Count}/{gov.slots})", labelStyle); y += 26;
        foreach (var p in GameData.Policies)
        {
            bool locked = p.reqCivic != null && !pl.civics.Contains(p.reqCivic);
            bool active = pl.policies.Contains(p.id);
            GUI.enabled = !locked;
            if (GUI.Button(new Rect(rect.x + 10, y, panelW - 20, 22), (active ? "[장착] " : "") + $"{p.name} (+{p.mult * 100:0}% {YieldKorean(p.yieldTarget)})"))
                TogglePolicy(p.id);
            GUI.enabled = true;
            y += 26;
        }
    }

    string YieldKorean(string y)
    {
        switch (y)
        {
            case "gold": return "골드";
            case "production": return "생산력";
            case "science": return "과학";
            case "culture": return "문화";
            default: return y;
        }
    }

    void DrawPanelBackground(Rect rect)
    {
        GUI.color = new Color(0.10f, 0.10f, 0.13f, 0.97f);
        GUI.DrawTexture(rect, flatTex);
        GUI.color = Color.white;
        DrawOutline(rect, new Color(0.8f, 0.8f, 0.8f));
    }

    void OnFoundCity()
    {
        var u = FindUnit(selectedUnitId);
        if (u == null || u.type != "settler") return;
        if (CityAt(u.x, u.y) != null) return;
        AddCity(0, u.x, u.y);
        units.Remove(u);
        selectedUnitId = -1;
        statusText = "도시를 건설했습니다!";
        RevealAroundPlayer0();
    }

    void DrawOutline(Rect r, Color col)
    {
        GUI.color = col;
        GUI.DrawTexture(new Rect(r.x - 2, r.y - 2, r.width + 4, 2), flatTex);
        GUI.DrawTexture(new Rect(r.x - 2, r.yMax, r.width + 4, 2), flatTex);
        GUI.DrawTexture(new Rect(r.x - 2, r.y - 2, 2, r.height + 4), flatTex);
        GUI.DrawTexture(new Rect(r.xMax, r.y - 2, 2, r.height + 4), flatTex);
        GUI.color = Color.white;
    }
}
