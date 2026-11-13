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
        if (winningTeam != Campaign.playerTeam) { EndRun(false, "战役失利"); return; }
        int clearedSlot, _cp; string _pp; ResolveCurrentStage(out clearedSlot, out _pp);   // ✅ 接缝:刚通关槽位(推进前捕获)
        SnapshotCarry();   // ✅ 真生存:推进前抓取我军残部+资金
        StageIndex++;
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
        CallDeferred(nameof(DeferredStageLoad));
    }

    // ✅ 装载下一关：走 ExtraSave.LoadMapToGame 全量换图（清理实体→重建网格→地形单位CO全落位）
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
                    su.fieldValues["health"] = System.Convert.ToString(RefGet(u, "health") ?? "10");
                    su.fieldValues["fuel"] = System.Convert.ToString(RefGet(u, "fuel") ?? "99");
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
        RefSet(su, "srcX", g.posX); RefSet(su, "srcY", g.posY);
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
                { _hqSrcX = g.posX; _hqSrcY = g.posY; break; }
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
            ["remainingBudget"] = RemainingBudget,
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
        RemainingBudget = parsed["remainingBudget"].AsDouble();
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
}
