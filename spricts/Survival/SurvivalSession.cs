// SurvivalSession.cs v2 - 会话运行时+全量换图装载+无尽池 (开创人2026-11-14)
// autoload（组名 survival_session）。预算数学/回合与时间扣减/关卡推进/HUD/结算信号。
using Godot;

public partial class SurvivalSession : Node
{
    public static SurvivalSession Instance { get; private set; }

    [Signal] public delegate void BudgetChangedEventHandler(double remaining, int limitType);
    [Signal] public delegate void StageChangedEventHandler(int stageIndex, int totalStages);
    [Signal] public delegate void SurvivalOverEventHandler(bool victory, int stagesCleared, double remainingBudget);

    public SurvivalCampaign Campaign { get; private set; }
    public int StageIndex { get; private set; }
    public int MoraleRemaining { get; private set; }
    public int Depth { get; private set; }   // ✅ 树状选关:已通关层数(节点号=StageIndex,进度=Depth)
    public double RemainingBudget { get; private set; }
    public bool Active { get; private set; }
    public int StagesCleared => StageIndex;

    private RichTextLabel _hud;
    private double _timeAccum;

    public override void _Ready()
    {
        Instance = this;
        AddToGroup("survival_session");
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Process(double delta)
    {
        if (!Active || Campaign == null || Campaign.limitType != 2) return;
        _timeAccum += delta;
        if (_timeAccum >= 1.0)
        {
            RemainingBudget -= (int)_timeAccum;
            _timeAccum = 0;
            EmitSignal(SignalName.BudgetChanged, RemainingBudget, 2);
            UpdateHud();
            if (RemainingBudget <= 0) EndRun(false, "时间耗尽");
        }
    }

    // ✅ 开局（按钮/调试控制台调）
    public void BeginCampaign(SurvivalCampaign campaign)
    {
        Campaign = campaign;
        StageIndex = 0;
        RemainingBudget = campaign?.budget ?? 0;
        MoraleRemaining = campaign?.moraleEnabled == true ? campaign.moraleMax : 0;
        Active = campaign != null && (campaign.stageSlots.Length > 0 || campaign.stageMapPaths.Length > 0 || (campaign.endlessMode && (campaign.endlessPoolSlots.Length > 0 || campaign.endlessPoolPaths.Length > 0)));
        _timeAccum = 0;
        EnsureHud();
        EmitSignal(SignalName.StageChanged, StageIndex, Campaign?.stageSlots.Length ?? 0);
        UpdateHud();
        if (Active)
        {
            string unit0 = campaign.limitType == 0 ? "G" : campaign.limitType == 1 ? "回合" : campaign.limitType == 3 ? "名额" : "秒";
            ShowStageSplash($"「{campaign.campaignName}」", $"第1关 · 预算 {RemainingBudget}{unit0}", 2.6);
        }
        if (Active) GD.Print($"[Survival] 开局:{campaign.campaignName} 主线{campaign.stageSlots.Length}关" + (campaign.endlessMode ? $"+无尽池{campaign.endlessPoolSlots.Length}" : "") + $" 预算[{campaign.limitType}]={campaign.budget}");
    }

    // ✅ 钩子①：GameManager.Turn.cs isNewTurn 每大回合调用（回合限制）
    public void TickRound()
    {
        if (!Active || Campaign == null || Campaign.limitType != 1) return;
        RemainingBudget--;
        EmitSignal(SignalName.BudgetChanged, RemainingBudget, 1);
        UpdateHud();
        if (RemainingBudget <= 0) EndRun(false, "回合耗尽");
        CheckAttritionDefeat();
    }

    // ✅ 开创人2026-11-14 兵力枯竭判负:上限模式名额耗尽且场上无兵→战役终结
    private void CheckAttritionDefeat()
    {
        if (!Active || Campaign == null || Campaign.limitType != 3 || RemainingBudget > 0) return;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        var units = gm?.unitManager?.AllUnits;
        if (units == null) return;
        int alive = 0;
        foreach (var u in units)
            if (u != null && GodotObject.IsInstanceValid(u) && u.team == Campaign.playerTeam) alive++;
        if (alive == 0) EndRun(false, "兵力枯竭——名额耗尽,部队全灭");
    }

    // ✅ 钩子②：生产扣费闸门（资金限制）。返回false=拒绝生产。调用点待接生产线。
    public bool TrySpendProduction(int cost)
    {
        if (!Active || Campaign == null) return true;
        if (Campaign.limitType == 3)   // ✅ 开创人2026-11-14 单位上限:一名额=一兵,死不起的紧箍咒
        {
            if (RemainingBudget < 1) { GD.Print("[Survival] 名额耗尽:兵力上限模式不可再生产"); return false; }
            RemainingBudget -= 1;
            EmitSignal(SignalName.BudgetChanged, RemainingBudget, 3);
            UpdateHud();
            return true;
        }
        if (Campaign.limitType != 0) return true;
        if (cost > RemainingBudget) { GD.Print($"[Survival] 生产被拒:{cost}G > 余量{RemainingBudget}G"); return false; }
        RemainingBudget -= cost;
        EmitSignal(SignalName.BudgetChanged, RemainingBudget, 0);
        UpdateHud();
        if (RemainingBudget <= 0) GD.Print("[Survival] 资金见底——仍可战，但造不出新单位了");
        return true;
    }

    // ✅ 钩子③：GameManager.Victory.cs ShowVictory 调用——玩家胜→推进下一关
    public void OnMapVictorious(string winningTeam)
    {
        if (!Active || Campaign == null) return;
        if (winningTeam != Campaign.playerTeam)
        {
            bool elite = Campaign.eliteStageIndices != null && System.Array.IndexOf(Campaign.eliteStageIndices, StageIndex) >= 0;
            if (Campaign.moraleEnabled && MoraleRemaining > 0 && !elite)
            {
                MoraleRemaining--;
                GD.Print($"[Survival] 军心-1,剩余{MoraleRemaining},重整旗鼓重打本关");
                UpdateHud(); SaveRun();
                ShowStageSplash("军心 -1 ！", $"剩余军心 {MoraleRemaining} · 再闯本关", 2.0);
                CallDeferred(nameof(DeferredStageLoad));
                return;
            }
            EndRun(false, elite && Campaign.moraleEnabled ? "精英关失守,战役终结" : (Campaign.moraleEnabled ? "军心耗尽" : "战役失利"));
            return;
        }
        int clearedSlot; string _pp; ResolveCurrentStage(out clearedSlot, out _pp);   // ✅ 接缝:刚通关槽位(推进前捕获)
        // ✅ 树状选关(开创人2026-11-14):解析分支DSL,多路弹出选关面板
        var nextNodes = ParseStageTree(Campaign.stageTree, StageIndex);
        if (nextNodes.Count > 1) { ShowBranchPanel(nextNodes); return; }
        int autoNext = nextNodes.Count == 1 ? nextNodes[0] : StageIndex + 1;
        SnapshotCarry();   // ✅ 真生存:推进前抓取我军残部+资金
        StageIndex = autoNext >= 0 ? autoNext : StageIndex + 1;
        Depth++;
        int mainCount = Mathf.Max(Campaign.stageSlots.Length, Campaign.stageMapPaths.Length);
        int total = Campaign.endlessMode
            ? (Campaign.randomDraw ? -1 : mainCount + Mathf.Max(Campaign.endlessPoolSlots.Length, Campaign.endlessPoolPaths.Length))
            : mainCount;
        if (!Campaign.endlessMode && StageIndex >= Campaign.stageSlots.Length) { EndRun(true, "全部关卡通过"); return; }
        if (Campaign.endlessMode && Campaign.endlessPoolSlots.Length == 0 && Campaign.endlessPoolPaths.Length == 0 && Campaign.endlessGenPool.Length == 0) { EndRun(true, "无尽池为空,战役完结"); return; }
        // 无尽模式：深入一层，预算缩减
        if (Campaign.endlessMode && StageIndex >= Campaign.stageSlots.Length && Campaign.budgetShrinkPercentPerStage > 0)
        {
            double before = RemainingBudget;
            RemainingBudget *= (100.0 - Campaign.budgetShrinkPercentPerStage) / 100.0;
            GD.Print($"[Survival] 无尽深入:预算 {before} → {RemainingBudget}(-{Campaign.budgetShrinkPercentPerStage}%)");
        }
        EmitSignal(SignalName.StageChanged, StageIndex, total);
        UpdateHud();
        if (Campaign.limitType == 3 && Campaign.unitCapRefillPerStage > 0)   // ✅ 软帽:每关回充名额
        {
            RemainingBudget = System.Math.Min(Campaign.budget, RemainingBudget + Campaign.unitCapRefillPerStage);
            GD.Print($"[Survival] 名额回充+{Campaign.unitCapRefillPerStage} → {RemainingBudget}");
        }
        ShowStageSplash($"第 {StageIndex} 关 · 通过！", $"即将进入 第 {StageIndex + 1} 关", 1.4);
        SaveRun();   // ✅ 每关自动存档=肉鸽检查点
        // ✅ 接缝①②焊合(开创人2026-11-14): 战役结算层入账 + 关卡解锁进度登记
        var settle = GetTree()?.Root?.FindChild("CampaignSettlement", true, false) as CampaignSettlement;
        settle?.OnVictoryDeclared(Campaign.playerTeam);
        var prof = GetTree()?.Root?.FindChild("CampaignProfile", true, false) as CampaignProfile;
        prof?.RecordStageClear(clearedSlot, StageIndex, false, 0, 0);
        CallDeferred(nameof(ShowIntelPanel));   // ✅ 出战情报:确认后再装载
    }

    // ✅ 装载下一关：走 ExtraSave.LoadMapToGame 全量换图（清理实体→重建网格→地形单位CO全落位）
    // ✅ 树状选关面板(开创人2026-11-14):分支节点卡片,点选后进入情报页
    private Control _branchRoot;
    private RichTextLabel _branchTitle;
    private Godot.Collections.Array<Button> _branchBtns = new();

    private void ShowBranchPanel(System.Collections.Generic.List<int> nodes)
    {
        EnsureBranch();
        _branchTitle.Text = $"[center][font_size=40]第 {Depth + 1} 层 · 选择下一关[/font_size][/center]";
        foreach (var b in _branchBtns) b.QueueFree();
        _branchBtns.Clear();
        var size = GetViewport().GetVisibleRect().Size;
        int i = 0;
        foreach (var n in nodes)
        {
            int slot; string path; string nm = $"第{n + 1}关", sz = "?";
            int saveNode = StageIndex; StageIndex = n;
            ResolveCurrentStage(out slot, out path);
            StageIndex = saveNode;
            if (slot >= 0)
            {
                var es = GetTree()?.Root?.GetNodeOrNull("ExtraSave") as ExtraSave;
                var d = es?.Data?.savedMaps != null && slot < es.Data.savedMaps.Count ? es.Data.savedMaps[slot] : null;
                if (d != null) { nm = d.displayName; sz = $"{d.width}×{d.height}"; }
            }
            else if (path != null && path.StartsWith("__GEN__")) { nm = "随机生成图"; }
            else if (!string.IsNullOrEmpty(path)) nm = path.GetFile().GetBaseName();
            var btn = new Button { Text = $"▶ {nm}  {sz}", Position = new Vector2(size.X / 2 - 160, size.Y * 0.4f + i * 56), Size = new Vector2(320, 46) };
            int captured = n;
            btn.Pressed += () => { _branchRoot.Visible = false; StageIndex = captured; Depth++; SaveRun(); CallDeferred(nameof(ShowIntelPanel)); };
            _branchRoot.AddChild(btn);
            _branchBtns.Add(btn);
            i++;
        }
        _branchRoot.Visible = true;
        _branchRoot.Modulate = new Color(1, 1, 1, 1);
    }

    private void EnsureBranch()
    {
        if (_branchRoot != null && IsInstanceValid(_branchRoot)) return;
        var vp = GetViewport(); if (vp == null) return;
        var size = vp.GetVisibleRect().Size;
        _branchRoot = new Control { Position = Vector2.Zero, Size = size, Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _branchRoot.AddChild(new ColorRect { Color = new Color(0.04f, 0.06f, 0.12f, 0.94f), Size = size, MouseFilter = Control.MouseFilterEnum.Ignore });
        _branchTitle = new RichTextLabel { Position = new Vector2(0, size.Y * 0.26f), Size = new Vector2(size.X, 80), BbcodeEnabled = true, MouseFilter = Control.MouseFilterEnum.Ignore };
        _branchRoot.AddChild(_branchTitle);
        vp.AddChild(_branchRoot);
    }

    private static System.Collections.Generic.List<int> ParseStageTree(string dsl, int node)
    {
        var result = new System.Collections.Generic.List<int>();
        if (string.IsNullOrEmpty(dsl)) { result.Add(node + 1); return result; }
        foreach (var raw in dsl.Split("\n"))
        {
            var line = raw.Trim();
            if (line.Length == 0 || !line.Contains(">")) continue;
            var parts = line.Split(">");
            if (parts.Length < 2 || parts[0].Trim().ToInt() != node) continue;
            foreach (var t in parts[1].Split(","))
            {
                var v = t.Trim();
                if (v.Length > 0) result.Add(v.ToInt());
            }
        }
        if (result.Count == 0) result.Add(node + 1);   // 无定义=线性
        return result;
    }

    // ✅ 出战情报(开创人2026-11-14):出征确认页
    private void ShowIntelPanel()
    {
        int slot; string mapPath;
        ResolveCurrentStage(out slot, out mapPath);
        string title = "第 " + (StageIndex + 1) + " 关";
        string mapName = "?", size = "?", extra = "";
        var root = GetTree()?.Root;
        if (slot >= 0)
        {
            var extraSave = root?.GetNodeOrNull("ExtraSave") as ExtraSave;
            var d = extraSave?.Data?.savedMaps != null && slot < extraSave.Data.savedMaps.Count ? extraSave.Data.savedMaps[slot] : null;
            if (d != null) { mapName = d.displayName; size = $"{d.width}×{d.height}"; }
        }
        else if (mapPath != null && mapPath.StartsWith("__GEN__"))
        {
            var g = Campaign.endlessGenPool[mapPath.Substring(7).ToInt()];
            mapName = "随机生成图"; size = $"{g.width}×{g.height}";
            extra = $"势力{g.factionCount} 对称{g.symmetryMode} 咽喉{(int)(g.chokeDensity * 100)}% 种子{(g.seed == 0 ? "随机" : g.seed.ToString())}";
        }
        else if (!string.IsNullOrEmpty(mapPath)) { mapName = mapPath.GetFile().GetBaseName(); }
        EnsureIntel();
        _intelLabel.Text = $"[center][font_size=44]{title}[/font_size]\n[font_size=26]地图:{mapName}   {size}\n{extra}\n敌方:AI默认编制(技能词条建设中)\n预算余量:{RemainingBudget}{(Campaign.moraleEnabled ? $"   军心:{MoraleRemaining}" : "")}[/font_size][/center]";
        _intelRoot.Visible = true;
        _intelRoot.Modulate = new Color(1, 1, 1, 1);
    }

    private Control _intelRoot;
    private RichTextLabel _intelLabel;
    private Button _intelGo;

    private void EnsureIntel()
    {
        if (_intelRoot != null && IsInstanceValid(_intelRoot)) return;
        var vp = GetViewport(); if (vp == null) return;
        var size = vp.GetVisibleRect().Size;
        _intelRoot = new Control { Position = Vector2.Zero, Size = size, Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        var bg = new ColorRect { Color = new Color(0.05f, 0.08f, 0.15f, 0.92f), Size = size, MouseFilter = Control.MouseFilterEnum.Ignore };
        _intelLabel = new RichTextLabel { Position = new Vector2(0, size.Y * 0.22f), Size = new Vector2(size.X, 320), BbcodeEnabled = true, ScrollActive = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _intelLabel.AddThemeFontSizeOverride("normal_font_size", 26);
        _intelGo = new Button { Text = "⚔ 开战", Position = new Vector2(size.X / 2 - 70, size.Y * 0.66f), Size = new Vector2(140, 46) };
        _intelGo.Pressed += () => { _intelRoot.Visible = false; CallDeferred(nameof(DeferredStageLoad)); };
        _intelRoot.AddChild(bg); _intelRoot.AddChild(_intelLabel); _intelRoot.AddChild(_intelGo);
        vp.AddChild(_intelRoot);
    }

    private void DeferredStageLoad()
    {
        int slot; string mapPath;
        ResolveCurrentStage(out slot, out mapPath);
        var root = GetTree()?.Root;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        var gridMgr = root?.FindChild("GridManager", recursive: true, owned: false) as GridManager;
        var terrainEditor = root?.FindChild("TerrainEditor", recursive: true, owned: false) as TerrainEditor;
        var extraSave = root?.GetNodeOrNull("ExtraSave") as ExtraSave;
        if (gm == null || gridMgr == null || terrainEditor == null || extraSave == null)
        {
            GD.PushWarning($"[Survival] 装载失败:依赖缺失 gm={gm != null} grid={gridMgr != null} editor={terrainEditor != null} save={extraSave != null}");
            return;
        }
        gm.gameEnded = false;   // ✅ 结算后复位，新关开局
        gm.turnPhase = 1;
        if (slot >= 0) ApplyCarryToSlot(extraSave, slot);   // ✅ 真生存:数据空间整军过继(槽位轨)
        bool ok;
        if (slot >= 0)
        {
            ok = extraSave.LoadMapToGame(slot, gm, gridMgr, terrainEditor);
        }
        else if (mapPath != null && mapPath.StartsWith("__GEN__"))
        {
            // ✅ 缺口B:现场生成——MapGenProfile → SavedMapData → 注入槽位 → 现成管线
            int gi = mapPath.Substring(7).ToInt();
            var profile = Campaign.endlessGenPool[gi];
            var genData = MapGenerator.Generate(profile, $"survival_gen_{StageIndex}");
            ApplyCarryToData(genData);   // ✅ 真生存:生成图同样过继
            int genSlot = extraSave.InjectGeneratedMap(genData);
            ok = extraSave.LoadMapToGame(genSlot, gm, gridMgr, terrainEditor);
        }
        else if (!string.IsNullOrEmpty(mapPath))
        {
            // ✅ 缺口A路径轨:仓库地图文件 → campaign_pending.map → 战役导入管线
            using var rf = FileAccess.Open(mapPath, FileAccess.ModeFlags.Read);
            if (rf == null) { GD.PushWarning($"[Survival] 地图文件不存在:{mapPath}"); return; }
            using var wf = FileAccess.Open("user://campaign_pending.map", FileAccess.ModeFlags.Write);
            wf?.StoreString(rf.GetAsText());
            wf?.Close();
            ok = terrainEditor.TryImportPendingCampaign();
        }
        else { GD.PushWarning("[Survival] 该关既无槽位也无路径"); return; }
        GD.Print($"[Survival] 第{StageIndex + 1}关装载 slot={slot} → {(ok ? "成功" : "失败")}");
        if (ok) RestoreCarriedStates(gm, gridMgr);
        if (ok && Campaign.coEnergyResetPerStage) CoModuleSystem.ResetAllCharges();   // ✅ 开创人2026-11-14 CO能量跨关重置
        if (ok)
        {
            string unit = Campaign.limitType == 0 ? "G" : Campaign.limitType == 1 ? "回合" : Campaign.limitType == 3 ? "名额" : "秒";
            ShowStageSplash($"第 {StageIndex + 1} 关", $"剩余预算：{RemainingBudget}{unit}", 2.2);
        }
        EmitSignal(SignalName.BudgetChanged, RemainingBudget, Campaign.limitType);
        UpdateHud();
    }

    // ✅ 缺口A(开创人2026-11-14):双轨解析——槽位优先,路径兜底;主序列与无尽池统一
    private void ResolveCurrentStage(out int slot, out string path)
    {
        slot = -1; path = "";
        int mainCount = Mathf.Max(Campaign.stageSlots.Length, Campaign.stageMapPaths.Length);
        if (!Campaign.endlessMode || StageIndex < mainCount)
        {
            int i = Mathf.Min(StageIndex, mainCount - 1);
            if (i < Campaign.stageSlots.Length) slot = Campaign.stageSlots[i];
            if (slot < 0 && i < Campaign.stageMapPaths.Length) path = Campaign.stageMapPaths[i];
            return;
        }
        int depth = StageIndex - mainCount;
        int poolCount = Mathf.Max(Mathf.Max(Campaign.endlessPoolSlots.Length, Campaign.endlessPoolPaths.Length), Campaign.endlessGenPool.Length);
        if (poolCount == 0) { slot = -2; return; }   // -2=池空
        int draw = Campaign.randomDraw ? GD.RandRange(0, poolCount - 1) : depth % poolCount;
        if (draw < Campaign.endlessPoolSlots.Length) slot = Campaign.endlessPoolSlots[draw];
        if (slot < 0 && draw < Campaign.endlessPoolPaths.Length) path = Campaign.endlessPoolPaths[draw];
        if (slot < 0 && string.IsNullOrEmpty(path) && draw < Campaign.endlessGenPool.Length) { path = "__GEN__" + draw; }
    }

    private void EndRun(bool victory, string reason)
    {
        Active = false;
        if (victory) DeleteSave();   // ✅ 通关清档；战败保留最后检查点
        GD.Print($"[Survival] 战役结束 胜利={victory} 通过={StagesCleared}关 余量={RemainingBudget} ({reason})");
        EmitSignal(SignalName.SurvivalOver, victory, StagesCleared, RemainingBudget);
        UpdateHud();
        ShowEndPanel(victory, StagesCleared, RemainingBudget, reason);
    }

    // ✅ 接缝④(开创人2026-11-14): 菜单"直接开局"——双槽演示战役,无需手配
    public void BeginQuickDemo()
    {
        BeginCampaign(new SurvivalCampaign
        {
            campaignName = "肉鸽试炼",
            stageSlots = new int[] { 0, 1 },
            limitType = 1,
            budget = 30,
            playerTeam = "Player1",
        });
    }

    // ===== 真生存·整军过继 (开创人2026-11-14) =====
    private static object RefGet(object o, string name)
        => o?.GetType().GetField(name)?.GetValue(o) ?? o?.GetType().GetProperty(name)?.GetValue(o);
    private static void RefSet(object o, string name, object v)
    {
        var f = o?.GetType().GetField(name); if (f != null) { f.SetValue(o, v); return; }
        o?.GetType().GetProperty(name)?.SetValue(o, v);
    }

    // ✅ 开创人2026-11-14:坐标/宽度多名称反射——字段名版本差异全兼容,猜名雷区拆除
    private static int GridCoord(object g, params string[] names)
    {
        foreach (var n in names) { var v = RefGet(g, n); if (v != null) return System.Convert.ToInt32(v); }
        return -1;
    }
    private static int MapWidth(GridManager gm) => GridCoord(gm, "width", "mapWidth", "gridWidth", "mapW");

    // 推进前:抓我军幸存单位(残血/油)与资金
    private void SnapshotCarry()
    {
        _carriedUnits.Clear(); _carriedFunds = 0; _hasCarry = false;
        if (Campaign == null || (!Campaign.carryUnitsBetweenStages && !Campaign.carryFundsBetweenStages)) return;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null) return;
        if (Campaign.carryFundsBetweenStages)
        {
            var fundsMap = RefGet(gm, "teamFunds") as System.Collections.IDictionary;
            if (fundsMap != null && fundsMap.Contains(Campaign.playerTeam))
                _carriedFunds = System.Convert.ToDouble(fundsMap[Campaign.playerTeam]);
        }
        if (Campaign.carryUnitsBetweenStages)
        {
            foreach (var u in gm.unitManager?.AllUnits ?? new System.Collections.Generic.List<Infantry>())
            {
                if (u == null || !GodotObject.IsInstanceValid(u) || u.team != Campaign.playerTeam) continue;
                var su = new SavedUnitData { typeName = u.UnitTypeId, team = Campaign.playerTeam };
                if (su.fieldValues != null)
                {
                    // ✅ 开创人2026-11-14 满状态过继:全部原生Export字段打包(血量/燃料/全弹药/字段型多回合效果)
                    foreach (var f in u.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                    {
                        var ft = f.FieldType;
                        bool prim = ft.IsPrimitive || ft == typeof(string);
                        if (!prim) continue;                       // Node/资源引用不可序列化,跳过
                        if (f.Name == "team" || f.Name == "UnitTypeId") continue;   // 结构性字段已有
                        try { su.fieldValues["__carry_" + f.Name] = System.Convert.ToString(f.GetValue(u), System.Globalization.CultureInfo.InvariantCulture); } catch { }
                    }
                }
                SetCarryPos(u, su);
                _carriedUnits.Add(su);
            }
        }
        _hasCarry = _carriedUnits.Count > 0 || _carriedFunds > 0;
        if (_hasCarry) GD.Print($"[Survival] 整军过继:携带{_carriedUnits.Count}单位+{_carriedFunds}G");
    }

    // 记录携带单位相对HQ的位置(下一关按新HQ复现相对阵型)
    private void SetCarryPos(Infantry u, SavedUnitData su)
    {
        var g = u.grid; if (g == null) return;
        RefSet(su, "srcX", GridCoord(g, "posX", "x", "gridX")); RefSet(su, "srcY", GridCoord(g, "posY", "y", "gridY"));
    }

    // 应用到数据对象(装载前):设施中立化/预制清除/携带注入/资金结转
    private void ApplyCarryToData(SavedMapData d)
    {
        if (!_hasCarry || d == null) return;
        if (d.facilities != null && Campaign.neutralizePresetFacilities)
            foreach (var f in d.facilities)
                if (f.team == Campaign.playerTeam) f.team = "Player0";
        if (d.units != null && Campaign.removePresetUnits)
            d.units.RemoveAll(u => u.team == Campaign.playerTeam);
        if (d.units != null && _carriedUnits.Count > 0)
        {
            int hqX = -1, hqY = -1, w = d.width;
            if (d.facilities != null)
                foreach (var f in d.facilities)
                    if (f.typeName == "HQ" && f.team == Campaign.playerTeam)
                    {
                        hqX = System.Convert.ToInt32(RefGet(f, "gridX") ?? RefGet(f, "posX") ?? -1);
                        hqY = System.Convert.ToInt32(RefGet(f, "gridY") ?? RefGet(f, "posY") ?? -1);
                    }
            foreach (var su in _carriedUnits)
            {
                int sx = System.Convert.ToInt32(RefGet(su, "srcX") ?? 0);
                int sy = System.Convert.ToInt32(RefGet(su, "srcY") ?? 0);
                int dx = sx - _hqSrcX, dy = sy - _hqSrcY;
                int tx = hqX + Mathf.Clamp(dx, -Campaign.carryDeployRadius, Campaign.carryDeployRadius);
                int ty = hqY + Mathf.Clamp(dy, -Campaign.carryDeployRadius, Campaign.carryDeployRadius);
                if (hqX < 0) { tx = sx % w; ty = sy % (d.height > 0 ? d.height : 1); }
                MapGenerator.SetPos(su, tx, ty, w);
                d.units.Add(su);
            }
        }
        if (Campaign.carryFundsBetweenStages && _carriedFunds > 0 && d.teamFunds != null)
            d.teamFunds.GetType().GetMethod("set_Item")?.Invoke(d.teamFunds,
                new object[] { Campaign.playerTeam, System.Convert.ChangeType(_carriedFunds, d.teamFunds.GetType().GetGenericArguments()[1]) });
    }

    // ✅ 开创人2026-11-14 满状态过继:装载后按(类型+队伍+坐标)找回携带单位,反射回灌全部__carry_字段
    private void RestoreCarriedStates(GameManager gm, GridManager gridMgr)
    {
        if (!_hasCarry || _carriedUnits.Count == 0 || gridMgr?.grids == null) return;
        foreach (var su in _carriedUnits)
        {
            int x = System.Convert.ToInt32(RefGet(su, "gridX") ?? RefGet(su, "posX") ?? -1);
            int y = System.Convert.ToInt32(RefGet(su, "gridY") ?? RefGet(su, "posY") ?? -1);
            int mw = MapWidth(gridMgr); if (mw <= 0) mw = 1;
            if (x < 0 || y < 0 || y * mw + x >= gridMgr.grids.Count) continue;
            var cell = gridMgr.grids[y * mw + x];
            Infantry target = null;
            if (cell?.infantries != null)
                foreach (var c in cell.infantries)
                    if (c != null && GodotObject.IsInstanceValid(c) && c.UnitTypeId == su.typeName && c.team == Campaign.playerTeam) { target = c; break; }
            if (target == null) continue;
            if (su.fieldValues == null) continue;
            foreach (var kv in su.fieldValues)
            {
                if (!kv.Key.StartsWith("__carry_")) continue;
                var f = target.GetType().GetField(kv.Key.Substring(8));
                if (f == null) continue;
                try { f.SetValue(target, System.Convert.ChangeType(kv.Value, f.FieldType, System.Globalization.CultureInfo.InvariantCulture)); } catch { }
            }
        }
        GD.Print($"[Survival] 满状态过继恢复:{_carriedUnits.Count}单位字段回灌完成");
    }

    private int _hqSrcX, _hqSrcY;
    private void ApplyCarryToSlot(ExtraSave extraSave, int slot)
    {
        var data = RefGet(extraSave, "Data");
        var maps = RefGet(data, "savedMaps") as System.Collections.Generic.IList<SavedMapData>;
        if (maps == null || slot < 0 || slot >= maps.Count) return;
        // 捕获旧HQ(相对阵型基准)
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager?.grids != null)
            foreach (var g in gm.gridManager.grids)
                if (g?.city != null && g.city.facilityTeam == Campaign.playerTeam && g.city is HQ)
                { _hqSrcX = GridCoord(g, "posX", "x", "gridX"); _hqSrcY = GridCoord(g, "posY", "y", "gridY"); break; }
        ApplyCarryToData(maps[slot]);
    }

    // ===== 过场仪式感 (开创人2026-11-14) =====
    private void EnsureSplash()
    {
        if (_splashRoot != null && IsInstanceValid(_splashRoot)) return;
        var vp = GetViewport(); if (vp == null) return;
        var size = vp.GetVisibleRect().Size;
        _splashRoot = new Control { Position = Vector2.Zero, Size = size, MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0) };
        var bg = new ColorRect { Color = new Color(0, 0, 0, 0.6f), Size = size, MouseFilter = Control.MouseFilterEnum.Ignore };
        _splashLabel = new RichTextLabel { Position = new Vector2(0, size.Y * 0.32f), Size = new Vector2(size.X, 260), BbcodeEnabled = true, ScrollActive = false, FitContent = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _splashLabel.AddThemeFontSizeOverride("normal_font_size", 72);
        _closeEndBtn = new Button { Text = "收下结局", Position = new Vector2(size.X / 2 - 60, size.Y * 0.72f), Size = new Vector2(120, 40), Visible = false };
        _closeEndBtn.Pressed += () => { if (_splashRoot != null) _splashRoot.Visible = false; };
        _splashRoot.AddChild(bg); _splashRoot.AddChild(_splashLabel); _splashRoot.AddChild(_closeEndBtn);
        vp.AddChild(_splashRoot);
    }

    private void ShowStageSplash(string title, string subtitle, double holdSec)
    {
        EnsureSplash();
        _splashRoot.Visible = true;
        _closeEndBtn.Visible = false;
        _splashLabel.Text = $"[center]{title}\n[font_size=30]{subtitle}[/font_size][/center]";
        _splashRoot.Modulate = new Color(1, 1, 1, 0);
        var tw = CreateTween();
        tw.TweenProperty(_splashRoot, "modulate", new Color(1, 1, 1, 1), 0.35);
        tw.TweenInterval(holdSec);
        tw.TweenProperty(_splashRoot, "modulate", new Color(1, 1, 1, 0), 0.5);
    }

    private void ShowEndPanel(bool victory, int stages, double remaining, string reason)
    {
        EnsureSplash();
        _splashRoot.Visible = true;
        _splashRoot.Modulate = new Color(1, 1, 1, 1);
        _closeEndBtn.Visible = true;
        _splashLabel.Text = $"[center]{'\u005f'}[/center]"; // 占位防旧内容
        _splashLabel.Text = $"[center][color={(victory ? "#7CFC98" : "#FF6B6B")}]{(victory ? "战 役 通 关" : "战 役 终 结")}[/color]\n[font_size=34]通过 {stages} 关 · 剩余预算 {remaining}\n{reason}[/font_size][/center]";
    }

    // ===== 局中存档 (开创人2026-11-14) =====
    private const string SavePath = "user://survival_save.json";
    private Button _saveBtn, _continueBtn;
    // ✅ 开创人2026-11-14 真生存:整军过继快照
    private System.Collections.Generic.List<SavedUnitData> _carriedUnits = new();
    private double _carriedFunds;
    private bool _hasCarry;

    private Control _splashRoot;
    private RichTextLabel _splashLabel;
    private Button _closeEndBtn;

    public static bool HasSavedRun() => FileAccess.FileExists(SavePath);

    public void SaveRun()
    {
        if (!Active || Campaign == null) return;
        var dict = new Godot.Collections.Dictionary
        {
            ["campaignName"] = Campaign.campaignName,
            ["stageSlots"] = new Godot.Collections.Array<int>(Campaign.stageSlots),
            ["limitType"] = Campaign.limitType,
            ["budget"] = Campaign.budget,
            ["fundsCarry"] = Campaign.fundsCarry,
            ["coEnergyResetPerStage"] = Campaign.coEnergyResetPerStage,
            ["playerTeam"] = Campaign.playerTeam,
            ["endlessMode"] = Campaign.endlessMode,
            ["endlessPoolSlots"] = new Godot.Collections.Array<int>(Campaign.endlessPoolSlots),
            ["randomDraw"] = Campaign.randomDraw,
            ["budgetShrinkPercentPerStage"] = Campaign.budgetShrinkPercentPerStage,
            ["stageIndex"] = StageIndex,
            ["remainingBudget"] = RemainingBudget,\n            ["morale"] = MoraleRemaining,\n            ["depth"] = Depth,
        };
        using var f = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        if (f != null) { f.StoreString(Json.Stringify(dict)); GD.Print($"[Survival] 已存档 第{StageIndex + 1}关 余量{RemainingBudget}"); }
    }

    // ✅ 继续按钮/外部调用：读档→恢复会话→装载该关地图
    public void LoadRun()
    {
        if (!FileAccess.FileExists(SavePath)) return;
        using var f = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
        if (f == null) return;
        var parsed = Json.ParseString(f.GetAsText()).AsGodotDictionary();
        if (parsed.Count == 0) return;
        var c = new SurvivalCampaign
        {
            campaignName = parsed["campaignName"].AsString(),
            stageSlots = parsed["stageSlots"].AsInt32Array(),
            limitType = parsed["limitType"].AsInt32(),
            budget = parsed["budget"].AsDouble(),
            fundsCarry = parsed["fundsCarry"].AsBool(),
            coEnergyResetPerStage = parsed["coEnergyResetPerStage"].AsBool(),
            playerTeam = parsed["playerTeam"].AsString(),
            endlessMode = parsed["endlessPoolSlots"].AsInt32Array().Length > 0 && parsed["endlessMode"].AsBool(),
            endlessPoolSlots = parsed["endlessPoolSlots"].AsInt32Array(),
            randomDraw = parsed["randomDraw"].AsBool(),
            budgetShrinkPercentPerStage = parsed["budgetShrinkPercentPerStage"].AsDouble(),
        };
        BeginCampaign(c);
        StageIndex = parsed["stageIndex"].AsInt32();
        RemainingBudget = parsed["remainingBudget"].AsDouble();\n        MoraleRemaining = parsed.ContainsKey("morale") ? parsed["morale"].AsInt32() : (Campaign.moraleEnabled ? Campaign.moraleMax : 0);
        GD.Print($"[Survival] 读档恢复 第{StageIndex + 1}关 余量{RemainingBudget}");
        UpdateHud();
        CallDeferred(nameof(DeferredStageLoad));
    }

    private static void DeleteSave() { if (FileAccess.FileExists(SavePath)) DirAccess.RemoveAbsolute(SavePath); }

    private void EnsureHud()
    {
        if (_hud != null && IsInstanceValid(_hud)) return;
        var vp = GetViewport(); if (vp == null) return;
        _hud = new RichTextLabel { Position = new Vector2(8, 8), Size = new Vector2(360, 46), BbcodeEnabled = true };
        vp.AddChild(_hud);
        _saveBtn = new Button { Text = "生存·存档", Position = new Vector2(8, 58), Size = new Vector2(100, 30) };
        _saveBtn.Pressed += SaveRun;
        vp.AddChild(_saveBtn);
        _continueBtn = new Button { Text = "生存·继续", Position = new Vector2(114, 58), Size = new Vector2(100, 30) };
        _continueBtn.Pressed += LoadRun;
        vp.AddChild(_continueBtn);
    }

    private void UpdateHud()
    {
        if (_hud == null || !IsInstanceValid(_hud)) return;
        if (_saveBtn != null && IsInstanceValid(_saveBtn)) _saveBtn.Visible = Active;
        if (_continueBtn != null && IsInstanceValid(_continueBtn)) _continueBtn.Visible = !Active && HasSavedRun();
        if (!Active) { if (Campaign != null) _hud.Text = $"[Survival] 结束：通过{StagesCleared}关 余量{RemainingBudget}"; return; }
        string unit = Campaign.limitType == 0 ? "G" : Campaign.limitType == 1 ? "回合" : Campaign.limitType == 3 ? "名额" : "秒";
        int total = Campaign.endlessMode ? -1 : Campaign.stageSlots.Length;
        string prog = total > 0 ? $"{StageIndex + 1}/{total}" : $"第{StageIndex + 1}层";
        _hud.Text = $"[Survival] {Campaign.campaignName} {prog}  预算:{RemainingBudget}{unit}";
    }
} + $"  第{Depth + 1}层" + (Campaign.moraleEnabled ? $"  军心:{MoraleRemaining}" : "")\n        Depth = parsed.ContainsKey("depth") ? parsed["depth"].AsInt32() : 0;;