// GameManager.cs - 添加完整的单位统计系统
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// GameManager · StatsPanel（由 GameManager.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class GameManager
{
    public enum StatsMode { Units, Facilities, Funds, Value }



    private int _statsBtnRetry;

    // ✅ 开创人2026-11-25: CanvasLayer全树兜底+延迟重试——方案甲boot模式下CurrentScene还是加载屏,
    //    原先两处静默return导致统计按钮整排消失;现在重试20帧后仍失败会PushError自报家门
    private static CanvasLayer FindCanvasLayerNamed(Node node, string name)
    {
        if (node is CanvasLayer cl && cl.Name == name) return cl;
        foreach (var ch in node.GetChildren())
        {
            var hit = FindCanvasLayerNamed(ch, name);
            if (hit != null) return hit;
        }
        return null;
    }

    // ✅ 创建统计面板切换按钮
    private void CreateStatsToggleButtons()
    {
        var canvasLayer = GetTree().CurrentScene?.GetNodeOrNull<CanvasLayer>("CanvasLayer")
                          ?? FindCanvasLayerNamed(GetTree().Root, "CanvasLayer");
        if (canvasLayer == null)
        {
            _statsBtnRetry++;
            if (_statsBtnRetry <= 20) { CallDeferred(nameof(CreateStatsToggleButtons)); return; }
            GD.PushError("[StatsPanel] 统计按钮创建失败：20帧内找不到CanvasLayer(boot模式UI骨架异常)");
            return;
        }
        _statsBtnRetry = 0;

        // 5个按钮排成一排（价值、单位、设施、资金、统计）
        // 每个按钮宽80px，统计按钮宽60px

        // 价值按钮
        valueStatsBtn = new Button();
        valueStatsBtn.Name = "ValueStatsBtn";
        TranslateGroup.SetText(valueStatsBtn, "💎 价值");
        valueStatsBtn.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        valueStatsBtn.OffsetLeft = -400;
        valueStatsBtn.OffsetTop = 0;
        valueStatsBtn.OffsetRight = -320;
        valueStatsBtn.OffsetBottom = 32;
        valueStatsBtn.AddThemeFontSizeOverride("font_size", 14);
        valueStatsBtn.AddThemeColorOverride("font_color", Colors.White);
        var vStyle = new StyleBoxFlat();
        vStyle.BgColor = new Color(0.7f, 0.4f, 0.6f, 0.9f);
        vStyle.SetCornerRadiusAll(8);
        valueStatsBtn.AddThemeStyleboxOverride("normal", vStyle);
        var vStyleHover = new StyleBoxFlat();
        vStyleHover.BgColor = new Color(0.8f, 0.5f, 0.7f, 0.95f);
        vStyleHover.SetCornerRadiusAll(8);
        valueStatsBtn.AddThemeStyleboxOverride("hover", vStyleHover);
        valueStatsBtn.ZIndex = 200;
        valueStatsBtn.Pressed += () => { statsMode = StatsMode.Value; UpdateUnitLists(); };
        canvasLayer.AddChild(valueStatsBtn);

        // 单位按钮
        unitStatsBtn = new Button();
        unitStatsBtn.Name = "UnitStatsBtn";
        TranslateGroup.SetText(unitStatsBtn, "📊 单位");
        unitStatsBtn.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        unitStatsBtn.OffsetLeft = -320;
        unitStatsBtn.OffsetTop = 0;
        unitStatsBtn.OffsetRight = -240;
        unitStatsBtn.OffsetBottom = 32;
        unitStatsBtn.AddThemeFontSizeOverride("font_size", 14);
        unitStatsBtn.AddThemeColorOverride("font_color", Colors.White);
        var uStyle = new StyleBoxFlat();
        uStyle.BgColor = new Color(0.3f, 0.5f, 0.7f, 0.9f);
        uStyle.SetCornerRadiusAll(8);
        unitStatsBtn.AddThemeStyleboxOverride("normal", uStyle);
        var uStyleHover = new StyleBoxFlat();
        uStyleHover.BgColor = new Color(0.4f, 0.6f, 0.8f, 0.95f);
        uStyleHover.SetCornerRadiusAll(8);
        unitStatsBtn.AddThemeStyleboxOverride("hover", uStyleHover);
        unitStatsBtn.ZIndex = 200;
        unitStatsBtn.Pressed += () => { statsMode = StatsMode.Units; UpdateUnitLists(); };
        canvasLayer.AddChild(unitStatsBtn);

        // 设施按钮
        facilityStatsBtn = new Button();
        facilityStatsBtn.Name = "FacilityStatsBtn";
        TranslateGroup.SetText(facilityStatsBtn, "🏙️ 设施");
        facilityStatsBtn.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        facilityStatsBtn.OffsetLeft = -240;
        facilityStatsBtn.OffsetTop = 0;
        facilityStatsBtn.OffsetRight = -160;
        facilityStatsBtn.OffsetBottom = 32;
        facilityStatsBtn.AddThemeFontSizeOverride("font_size", 14);
        facilityStatsBtn.AddThemeColorOverride("font_color", Colors.White);
        var fStyle = new StyleBoxFlat();
        fStyle.BgColor = new Color(0.6f, 0.5f, 0.3f, 0.9f);
        fStyle.SetCornerRadiusAll(8);
        facilityStatsBtn.AddThemeStyleboxOverride("normal", fStyle);
        var fStyleHover = new StyleBoxFlat();
        fStyleHover.BgColor = new Color(0.7f, 0.6f, 0.4f, 0.95f);
        fStyleHover.SetCornerRadiusAll(8);
        facilityStatsBtn.AddThemeStyleboxOverride("hover", fStyleHover);
        facilityStatsBtn.ZIndex = 200;
        facilityStatsBtn.Pressed += () => { statsMode = StatsMode.Facilities; UpdateUnitLists(); };
        canvasLayer.AddChild(facilityStatsBtn);

        // 资金按钮
        fundsStatsBtn = new Button();
        fundsStatsBtn.Name = "FundsStatsBtn";
        TranslateGroup.SetText(fundsStatsBtn, "💰 资金");
        fundsStatsBtn.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        fundsStatsBtn.OffsetLeft = -160;
        fundsStatsBtn.OffsetTop = 0;
        fundsStatsBtn.OffsetRight = -80;
        fundsStatsBtn.OffsetBottom = 32;
        fundsStatsBtn.AddThemeFontSizeOverride("font_size", 14);
        fundsStatsBtn.AddThemeColorOverride("font_color", Colors.White);
        var mStyle = new StyleBoxFlat();
        mStyle.BgColor = new Color(0.3f, 0.6f, 0.4f, 0.9f);
        mStyle.SetCornerRadiusAll(8);
        fundsStatsBtn.AddThemeStyleboxOverride("normal", mStyle);
        var mStyleHover = new StyleBoxFlat();
        mStyleHover.BgColor = new Color(0.4f, 0.7f, 0.5f, 0.95f);
        mStyleHover.SetCornerRadiusAll(8);
        fundsStatsBtn.AddThemeStyleboxOverride("hover", mStyleHover);
        fundsStatsBtn.ZIndex = 200;
        fundsStatsBtn.Pressed += () => { statsMode = StatsMode.Funds; UpdateUnitLists(); };
        canvasLayer.AddChild(fundsStatsBtn);

        // ✅ 切换回合按钮（替代右键/三连击）
        nextPhaseButton = new Button();
        nextPhaseButton.Name = "NextPhaseButton";
        TranslateGroup.SetText(nextPhaseButton, "🔄 切换回合");
        nextPhaseButton.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        nextPhaseButton.OffsetLeft = -400;
        nextPhaseButton.OffsetTop = 36;
        nextPhaseButton.OffsetRight = -240;
        nextPhaseButton.OffsetBottom = 72;
        nextPhaseButton.AddThemeFontSizeOverride("font_size", 16);
        nextPhaseButton.AddThemeColorOverride("font_color", Colors.White);
        var npStyle = new StyleBoxFlat();
        npStyle.BgColor = new Color(0.9f, 0.3f, 0.2f, 0.9f);
        npStyle.SetCornerRadiusAll(8);
        nextPhaseButton.AddThemeStyleboxOverride("normal", npStyle);
        var npStyleHover = new StyleBoxFlat();
        npStyleHover.BgColor = new Color(1.0f, 0.4f, 0.3f, 0.95f);
        npStyleHover.SetCornerRadiusAll(8);
        nextPhaseButton.AddThemeStyleboxOverride("hover", npStyleHover);
        nextPhaseButton.ZIndex = 200;
        nextPhaseButton.Pressed += () => { NextPhase(); };
        canvasLayer.AddChild(nextPhaseButton);
    }
    public void UpdateUnitLists()
    {

        if (UnitLists == null) return;
        switch (statsMode)
        {
            case StatsMode.Facilities:
                UpdateFacilityLists();
                return;
            case StatsMode.Funds:
                UpdateFundsLists();
                return;
            case StatsMode.Value:
                UpdateValueLists();
                return;
        }

        // ✅ 直接从源头实时计算，不依赖维护的列表
        var all = unitManager?.AllUnits ?? new List<Infantry>();

        // 四队伍计数器: P-1(Player), P0(Player0), P1(Player1), P2(Player2)
        int pn1Infantry = 0, p0Infantry = 0, p1Infantry = 0, p2Infantry = 0;
        int pn1Mech = 0, p0Mech = 0, p1Mech = 0, p2Mech = 0;
        int pn1Oozium = 0, p0Oozium = 0, p1Oozium = 0, p2Oozium = 0;
        int pn1Tank = 0, p0Tank = 0, p1Tank = 0, p2Tank = 0;
        int pn1MdTank = 0, p0MdTank = 0, p1MdTank = 0, p2MdTank = 0;
        int pn1Artillery = 0, p0Artillery = 0, p1Artillery = 0, p2Artillery = 0;
        int pn1Rocket = 0, p0Rocket = 0, p1Rocket = 0, p2Rocket = 0;
        int pn1APC = 0, p0APC = 0, p1APC = 0, p2APC = 0;
        int pn1Recon = 0, p0Recon = 0, p1Recon = 0, p2Recon = 0;
        int pn1AntiAir = 0, p0AntiAir = 0, p1AntiAir = 0, p2AntiAir = 0;
        int pn1AntiTank = 0, p0AntiTank = 0, p1AntiTank = 0, p2AntiTank = 0;
        int pn1FlyBomb = 0, p0FlyBomb = 0, p1FlyBomb = 0, p2FlyBomb = 0;
        int pn1Flare = 0, p0Flare = 0, p1Flare = 0, p2Flare = 0;
        int pn1Fighter = 0, p0Fighter = 0, p1Fighter = 0, p2Fighter = 0;
        int pn1Bomber = 0, p0Bomber = 0, p1Bomber = 0, p2Bomber = 0;
        int pn1BCopter = 0, p0BCopter = 0, p1BCopter = 0, p2BCopter = 0;
        int pn1TCopter = 0, p0TCopter = 0, p1TCopter = 0, p2TCopter = 0;
        int pn1AntiAirMissile = 0, p0AntiAirMissile = 0, p1AntiAirMissile = 0, p2AntiAirMissile = 0;
        int pn1Stealth = 0, p0Stealth = 0, p1Stealth = 0, p2Stealth = 0;
        int pn1Dog = 0, p0Dog = 0, p1Dog = 0, p2Dog = 0;
        int pn1Bike = 0, p0Bike = 0, p1Bike = 0, p2Bike = 0;
        int pn1PipeRunner = 0, p0PipeRunner = 0, p1PipeRunner = 0, p2PipeRunner = 0;
        int pn1RepairBoat = 0, p0RepairBoat = 0, p1RepairBoat = 0, p2RepairBoat = 0;

    
        

        // 兵器统计 - 四队伍
        int pn1Weapons = 0, p0Weapons = 0, p1Weapons = 0, p2Weapons = 0;
        int pn1Cannons = 0, p0Cannons = 0, p1Cannons = 0, p2Cannons = 0;
        int pn1Lasers = 0, p0Lasers = 0, p1Lasers = 0, p2Lasers = 0;
        if (weaponManager != null)
        {
            foreach (var w in weaponManager.AllWeapons.ToList())
            {
                if (w == null || !IsInstanceValid(w)) continue;
                string team = w.team;
                if (w is BlackCannon) 
                { 
                    if (team == "Player") pn1Cannons++; else if (team == "Player0") p0Cannons++;
                    else if (team == "Player1") p1Cannons++; else if (team == "Player2") p2Cannons++;
                }
                else if (w is Laser) 
                { 
                    if (team == "Player") pn1Lasers++; else if (team == "Player0") p0Lasers++;
                    else if (team == "Player1") p1Lasers++; else if (team == "Player2") p2Lasers++;
                }
                else 
                { 
                    if (team == "Player") pn1Weapons++; else if (team == "Player0") p0Weapons++;
                    else if (team == "Player1") p1Weapons++; else if (team == "Player2") p2Weapons++;
                }
            }
        }

        // ✅ 按类型和势力实时计数
        foreach (var u in all)
        {
            if (u == null || !IsInstanceValid(u)) continue;
            string t = u.team;
            switch (u)
            {
                case Mech _: if (t == "Player") pn1Mech++; else if (t == "Player0") p0Mech++; else if (t == "Player1") p1Mech++; else if (t == "Player2") p2Mech++; break;
                case MdTank _: if (t == "Player") pn1MdTank++; else if (t == "Player0") p0MdTank++; else if (t == "Player1") p1MdTank++; else if (t == "Player2") p2MdTank++; break;
                case LightTank _: if (t == "Player") pn1Tank++; else if (t == "Player0") p0Tank++; else if (t == "Player1") p1Tank++; else if (t == "Player2") p2Tank++; break;
                case Artillery _: if (t == "Player") pn1Artillery++; else if (t == "Player0") p0Artillery++; else if (t == "Player1") p1Artillery++; else if (t == "Player2") p2Artillery++; break;
                case Rocket _: if (t == "Player") pn1Rocket++; else if (t == "Player0") p0Rocket++; else if (t == "Player1") p1Rocket++; else if (t == "Player2") p2Rocket++; break;
                case APC _: if (t == "Player") pn1APC++; else if (t == "Player0") p0APC++; else if (t == "Player1") p1APC++; else if (t == "Player2") p2APC++; break;
                case Recon _: if (t == "Player") pn1Recon++; else if (t == "Player0") p0Recon++; else if (t == "Player1") p1Recon++; else if (t == "Player2") p2Recon++; break;
                case AntiAir _: if (t == "Player") pn1AntiAir++; else if (t == "Player0") p0AntiAir++; else if (t == "Player1") p1AntiAir++; else if (t == "Player2") p2AntiAir++; break;
                case AntiTank _: if (t == "Player") pn1AntiTank++; else if (t == "Player0") p0AntiTank++; else if (t == "Player1") p1AntiTank++; else if (t == "Player2") p2AntiTank++; break;
                case FlyBomb _: if (t == "Player") pn1FlyBomb++; else if (t == "Player0") p0FlyBomb++; else if (t == "Player1") p1FlyBomb++; else if (t == "Player2") p2FlyBomb++; break;
                case Fighter _: if (t == "Player") pn1Fighter++; else if (t == "Player0") p0Fighter++; else if (t == "Player1") p1Fighter++; else if (t == "Player2") p2Fighter++; break;
                case Bomber _: if (t == "Player") pn1Bomber++; else if (t == "Player0") p0Bomber++; else if (t == "Player1") p1Bomber++; else if (t == "Player2") p2Bomber++; break;
                case BCopter _: if (t == "Player") pn1BCopter++; else if (t == "Player0") p0BCopter++; else if (t == "Player1") p1BCopter++; else if (t == "Player2") p2BCopter++; break;
                case TCopter _: if (t == "Player") pn1TCopter++; else if (t == "Player0") p0TCopter++; else if (t == "Player1") p1TCopter++; else if (t == "Player2") p2TCopter++; break;
                case AntiAirMissile _: if (t == "Player") pn1AntiAirMissile++; else if (t == "Player0") p0AntiAirMissile++; else if (t == "Player1") p1AntiAirMissile++; else if (t == "Player2") p2AntiAirMissile++; break;
                case Stealth _: if (t == "Player") pn1Stealth++; else if (t == "Player0") p0Stealth++; else if (t == "Player1") p1Stealth++; else if (t == "Player2") p2Stealth++; break;
                case Dog _: if (t == "Player") pn1Dog++; else if (t == "Player0") p0Dog++; else if (t == "Player1") p1Dog++; else if (t == "Player2") p2Dog++; break;
                case Flare _: if (t == "Player") pn1Flare++; else if (t == "Player0") p0Flare++; else if (t == "Player1") p1Flare++; else if (t == "Player2") p2Flare++; break;
                case Bike _: if (t == "Player") pn1Bike++; else if (t == "Player0") p0Bike++; else if (t == "Player1") p1Bike++; else if (t == "Player2") p2Bike++; break;
                case PipeRunner _: if (t == "Player") pn1PipeRunner++; else if (t == "Player0") p0PipeRunner++; else if (t == "Player1") p1PipeRunner++; else if (t == "Player2") p2PipeRunner++; break;
                case RepairBoat _: if (t == "Player") pn1RepairBoat++; else if (t == "Player0") p0RepairBoat++; else if (t == "Player1") p1RepairBoat++; else if (t == "Player2") p2RepairBoat++; break;
                case Oozium _: if (t == "Player") pn1Oozium++; else if (t == "Player0") p0Oozium++; else if (t == "Player1") p1Oozium++; else if (t == "Player2") p2Oozium++; break;
                case Infantry _: if (t == "Player") pn1Infantry++; else if (t == "Player0") p0Infantry++; else if (t == "Player1") p1Infantry++; else if (t == "Player2") p2Infantry++; break;
            }
        }

        int totalUnits = all.Count(u => u != null && IsInstanceValid(u));
        int totalWeapons = weaponManager?.AllWeapons.Count(w => w != null && IsInstanceValid(w)) ?? 0;

        // 颜色定义
        string cP1 = "#ff4d4d";   // 红
        string cP2 = "#4d80ff";   // 蓝
        string cP0 = "#b3b3b3";   // 灰
        string cPN1 = "#b366ff";  // 紫
        string cTitle = "#ffaa33"; // 橙黄

        string text = $" [color={cTitle}]⚔ 单位统计[/color] \n";
        text += $"  [color={cPN1}]🟣P-1[/color]  [color={cP0}]⚪P0[/color]  [color={cP1}]🔴P1[/color]  [color={cP2}]🔵P2[/color]\n";
        text += $"步 [color={cPN1}]{pn1Infantry,2}|[color={cP0}]{p0Infantry,2}|[color={cP1}]{p1Infantry,2}|[color={cP2}]{p2Infantry,2}[/color]\n";
        text += $"机 [color={cPN1}]{pn1Mech,2}|[color={cP0}]{p0Mech,2}|[color={cP1}]{p1Mech,2}|[color={cP2}]{p2Mech,2}[/color]\n";
        text += $"O [color={cPN1}]{pn1Oozium,2}|[color={cP0}]{p0Oozium,2}[color={cP1}]|{p1Oozium,2}|[color={cP2}]{p2Oozium,2}[/color]\n";

        if (pn1Tank > 0 || p0Tank > 0 || p1Tank > 0 || p2Tank > 0)
            text += $"轻 [color={cPN1}]{pn1Tank,2}|[color={cP0}]{p0Tank,2}|[color={cP1}]{p1Tank,2}|[color={cP2}]{p2Tank,2}[/color]\n";

        if (pn1MdTank > 0 || p0MdTank > 0 || p1MdTank > 0 || p2MdTank > 0)
            text += $"重 [color={cPN1}]{pn1MdTank,2}|[color={cP0}]{p0MdTank,2}|[color={cP1}]{p1MdTank,2}|[color={cP2}]{p2MdTank,2}[/color]\n";

        if (pn1APC > 0 || p0APC > 0 || p1APC > 0 || p2APC > 0)
            text += $"运 [color={cPN1}]{pn1APC,2}|[color={cP0}]{p0APC,2}|[color={cP1}]{p1APC,2}|[color={cP2}]{p2APC,2}[/color]\n";

        if (pn1Recon > 0 || p0Recon > 0 || p1Recon > 0 || p2Recon > 0)
            text += $"侦 [color={cPN1}]{pn1Recon,2}|[color={cP0}]{p0Recon,2}|[color={cP1}]{p1Recon,2}|[color={cP2}]{p2Recon,2}[/color]\n";

        if (pn1Artillery > 0 || p0Artillery > 0 || p1Artillery > 0 || p2Artillery > 0)
            text += $"炮[color={cPN1}] {pn1Artillery,2}|[color={cP0}]{p0Artillery,2}|[color={cP1}]{p1Artillery,2}|[color={cP2}]{p2Artillery,2}[/color]\n";

        if (pn1Rocket > 0 || p0Rocket > 0 || p1Rocket > 0 || p2Rocket > 0)
            text += $"箭[color={cPN1}] {pn1Rocket,2}|[color={cP0}]{p0Rocket,2}|[color={cP1}]{p1Rocket,2}|[color={cP2}]{p2Rocket,2}[/color]\n";

        if (pn1AntiAir > 0 || p0AntiAir > 0 || p1AntiAir > 0 || p2AntiAir > 0)
            text += $"防[color={cPN1}] {pn1AntiAir,2}|[color={cP0}]{p0AntiAir,2}|[color={cP1}]{p1AntiAir,2}|[color={cP2}]{p2AntiAir,2}[/color]\n";

        if (pn1AntiTank > 0 || p0AntiTank > 0 || p1AntiTank > 0 || p2AntiTank > 0)
            text += $"反[color={cPN1}] {pn1AntiTank,2}|[color={cP0}]{p0AntiTank,2}|[color={cP1}]{p1AntiTank,2}|[color={cP2}]{p2AntiTank,2}[/color]\n";

        if (pn1FlyBomb > 0 || p0FlyBomb > 0 || p1FlyBomb > 0 || p2FlyBomb > 0)
            text += $"弹 [color={cPN1}]{pn1FlyBomb,2}|[color={cP0}]{p0FlyBomb,2}|[color={cP1}]{p1FlyBomb,2}|[color={cP2}]{p2FlyBomb,2}[/color]\n";

        if (pn1Flare > 0 || p0Flare > 0 || p1Flare > 0 || p2Flare > 0)
            text += $"照 [color={cPN1}]{pn1Flare,2}|[color={cP0}]{p0Flare,2}|[color={cP1}]{p1Flare,2}|[color={cP2}]{p2Flare,2}[/color]\n";

        if (pn1Bike > 0 || p0Bike > 0 || p1Bike > 0 || p2Bike > 0)
            text += $"摩[color={cPN1}] {pn1Bike,2}|[color={cP0}]{p0Bike,2}|[color={cP1}]{p1Bike,2}|[color={cP2}]{p2Bike,2}[/color]\n";

        if (pn1PipeRunner > 0 || p0PipeRunner > 0 || p1PipeRunner > 0 || p2PipeRunner > 0)
            text += $"管[color={cPN1}] {pn1PipeRunner,2}|[color={cP0}]{p0PipeRunner,2}|[color={cP1}]{p1PipeRunner,2}|[color={cP2}]{p2PipeRunner,2}[/color]\n";

        if (pn1RepairBoat > 0 || p0RepairBoat > 0 || p1RepairBoat > 0 || p2RepairBoat > 0)
            text += $"修[color={cPN1}] {pn1RepairBoat,2}|[color={cP0}]{p0RepairBoat,2}|[color={cP1}]{p1RepairBoat,2}|[color={cP2}]{p2RepairBoat,2}[/color]\n";

        // 兵器统计
        bool hasAnyWeapon = pn1Cannons > 0 || p0Cannons > 0 || p1Cannons > 0 || p2Cannons > 0
                         || pn1Lasers > 0 || p0Lasers > 0 || p1Lasers > 0 || p2Lasers > 0
                         || pn1Weapons > 0 || p0Weapons > 0 || p1Weapons > 0 || p2Weapons > 0;

        if (hasAnyWeapon)
        {
            text += $"\n[color={cTitle}]===兵器===[/color]\n";
            if (pn1Cannons > 0 || p0Cannons > 0 || p1Cannons > 0 || p2Cannons > 0)
                text += $"炮 [color={cPN1}]{pn1Cannons,2}|[color={cP0}]{p0Cannons,2}|[color={cP1}]{p1Cannons,2}|[color={cP2}]{p2Cannons,2}[/color]\n";
            if (pn1Lasers > 0 || p0Lasers > 0 || p1Lasers > 0 || p2Lasers > 0)
                text += $"激[color={cPN1}] {pn1Lasers,2}|[color={cP0}]{p0Lasers,2}|[color={cP1}]{p1Lasers,2}|[color={cP2}]{p2Lasers,2}[/color]\n";
            if (pn1Weapons > 0 || p0Weapons > 0 || p1Weapons > 0 || p2Weapons > 0)
                text += $"其[color={cPN1}] {pn1Weapons,2}|[color={cP0}]{p0Weapons,2}|[color={cP1}]{p1Weapons,2}|[color={cP2}]{p2Weapons,2}[/color]\n";
        }


        int totalAll = totalUnits + totalWeapons;

                if (pn1Fighter > 0 || p0Fighter > 0 || p1Fighter > 0 || p2Fighter > 0)
            text += $"歼 [color={cPN1}]{pn1Fighter,2}|[color={cP0}]{p0Fighter,2}|[color={cP1}]{p1Fighter,2}|[color={cP2}]{p2Fighter,2}[/color]\n";
        if (pn1Bomber > 0 || p0Bomber > 0 || p1Bomber > 0 || p2Bomber > 0)
            text += $"轰 [color={cPN1}]{pn1Bomber,2}|[color={cP0}]{p0Bomber,2}|[color={cP1}]{p1Bomber,2}|[color={cP2}]{p2Bomber,2}[/color]\n";
        if (pn1BCopter > 0 || p0BCopter > 0 || p1BCopter > 0 || p2BCopter > 0)
            text += $"武 [color={cPN1}]{pn1BCopter,2}|[color={cP0}]{p0BCopter,2}|[color={cP1}]{p1BCopter,2}|[color={cP2}]{p2BCopter,2}[/color]\n";
        if (pn1TCopter > 0 || p0TCopter > 0 || p1TCopter > 0 || p2TCopter > 0)
            text += $"直 [color={cPN1}]{pn1TCopter,2}|[color={cP0}]{p0TCopter,2}|[color={cP1}]{p1TCopter,2}|[color={cP2}]{p2TCopter,2}[/color]\n";
        if (pn1AntiAirMissile > 0 || p0AntiAirMissile > 0 || p1AntiAirMissile > 0 || p2AntiAirMissile > 0)
            text += $"导 [color={cPN1}]{pn1AntiAirMissile,2}|[color={cP0}]{p0AntiAirMissile,2}|[color={cP1}]{p1AntiAirMissile,2}|[color={cP2}]{p2AntiAirMissile,2}[/color]\n";
        if (pn1Stealth > 0 || p0Stealth > 0 || p1Stealth > 0 || p2Stealth > 0)
            text += $"隐 [color={cPN1}]{pn1Stealth,2}|[color={cP0}]{p0Stealth,2}|[color={cP1}]{p1Stealth,2}|[color={cP2}]{p2Stealth,2}[/color]\n";
        if (pn1Dog > 0 || p0Dog > 0 || p1Dog > 0 || p2Dog > 0)
            text += $"狗 [color={cPN1}]{pn1Dog,2}|[color={cP0}]{p0Dog,2}|[color={cP1}]{p1Dog,2}|[color={cP2}]{p2Dog,2}[/color]\n";

        int pn1TotalUnits = pn1Infantry + pn1Mech + pn1Oozium + pn1Tank + pn1MdTank + pn1Artillery + pn1Rocket + pn1APC + pn1Recon + pn1AntiAir + pn1AntiTank + pn1FlyBomb + pn1Flare + pn1Bike + pn1PipeRunner + pn1RepairBoat + pn1Fighter + pn1Bomber + pn1BCopter + pn1TCopter + pn1AntiAirMissile + pn1Stealth + pn1Dog;
        int p0TotalUnits = p0Infantry + p0Mech + p0Oozium + p0Tank + p0MdTank + p0Artillery + p0Rocket + p0APC + p0Recon + p0AntiAir + p0AntiTank + p0FlyBomb + p0Flare + p0Bike + p0PipeRunner + p0RepairBoat + p0Fighter + p0Bomber + p0BCopter + p0TCopter + p0AntiAirMissile + p0Stealth + p0Dog;
        int p1TotalUnits = p1Infantry + p1Mech + p1Oozium + p1Tank + p1MdTank + p1Artillery + p1Rocket + p1APC + p1Recon + p1AntiAir + p1AntiTank + p1FlyBomb + p1Flare + p1Bike + p1PipeRunner + p1RepairBoat + p1Fighter + p1Bomber + p1BCopter + p1TCopter + p1AntiAirMissile + p1Stealth + p1Dog;
        int p2TotalUnits = p2Infantry + p2Mech + p2Oozium + p2Tank + p2MdTank + p2Artillery + p2Rocket + p2APC + p2Recon + p2AntiAir + p2AntiTank + p2FlyBomb + p2Flare + p2Bike + p2PipeRunner + p2RepairBoat + p2Fighter + p2Bomber + p2BCopter + p2TCopter + p2AntiAirMissile + p2Stealth + p2Dog;

        int pn1TotalWeapons = pn1Cannons + pn1Lasers + pn1Weapons;
        int p0TotalWeapons = p0Cannons + p0Lasers + p0Weapons;
        int p1TotalWeapons = p1Cannons + p1Lasers + p1Weapons;
        int p2TotalWeapons = p2Cannons + p2Lasers + p2Weapons;

        int pn1TotalAll = pn1TotalUnits + pn1TotalWeapons;
        int p0TotalAll = p0TotalUnits + p0TotalWeapons;
        int p1TotalAll = p1TotalUnits + p1TotalWeapons;
        int p2TotalAll = p2TotalUnits + p2TotalWeapons;

        text += $"\n[color={cTitle}]===总计===[/color]\n";
        text += $"单 {pn1TotalUnits,2}|{p0TotalUnits,2}|{p1TotalUnits,2}|{p2TotalUnits,2}\n";
        if (totalWeapons > 0)
            text += $"兵 {pn1TotalWeapons,2}|{p0TotalWeapons,2}|{p1TotalWeapons,2}|{p2TotalWeapons,2}\n";
        text += $"共 {pn1TotalAll,2}|{p0TotalAll,2}|{p1TotalAll,2}|{p2TotalAll,2}\n";

        UnitLists.Text = text;
    }
public void UpdateFacilityLists()
    {
        if (UnitLists == null) return;
        if (gridManager == null) return;

        var cities = gridManager.GetAllCities();
        int p0Cities = 0, p1Cities = 0, p2Cities = 0, pN1Cities = 0;

        foreach (var city in cities)
        {
            if (city == null) continue;
            switch (city.facilityTeam)
            {
                case "Player0": p0Cities++; break;
                case "Player1": p1Cities++; break;
                case "Player2": p2Cities++; break;
                case "Player": pN1Cities++; break;
            }
        }

        string text = TranslateGroup.T(" [color=yellow]设施统计[/color] \n");
        text += $"[color=gray]中立(P0): {p0Cities}[/color]\n";
        text += $"[color=red]🔴P1: {p1Cities}[/color]\n";
        text += $"[color=blue]🔵P2: {p2Cities}[/color]\n";
        if (pN1Cities > 0)
            text += $"[color=orange]P-1: {pN1Cities}[/color]\n";
        text += $"总计: {cities.Count} 设施";

        UnitLists.Text = text;
    }
    public void UpdateFundsLists()
    {
        if (UnitLists == null) return;

        // 计算P1/P2/P-1每回合收入
        int p1Income = 0, p2Income = 0, pN1Income = 0;
        if (gridManager?.grids != null)
        {
            foreach (var grid in gridManager.grids)
            {
                if (grid?.city == null) continue;
                var c = grid.city;
                int income = c.GetCurrentIncome();
                if (income <= 0) continue;

                // ✅ P-1公共设施：给所有势力提供收入
                if (c.facilityTeam == TeamHelper.Player || c.isPublicFacility)
                {
                    p1Income += income;
                    p2Income += income;
                    pN1Income += income;
                }
                else if (c.facilityTeam == "Player1")
                {
                    p1Income += income;
                }
                else if (c.facilityTeam == "Player2")
                {
                    p2Income += income;
                }
            }
        }

        string cP1 = "#ff4d4d";
        string cP2 = "#4d80ff";
        string cPN1 = "#b366ff";
        string cTitle = "#ffaa33";

        string text = $" [color={cTitle}]💰 资金统计[/color] \n\n";
        text += $"[color={cPN1}]🟣 P-1 资金[/color]\n";
        text += $"  现有: {GetTeamFunds(TeamHelper.Player)} G\n";
        text += $"  收入: +{pN1Income} G/回合\n\n";
        text += $"[color={cP1}]🔴 P1 资金[/color]\n";
        text += $"  现有: {p1Funds} G\n";
        text += $"  收入: +{p1Income} G/回合\n";
        text += $"  上限: {p1FundsMax} G\n\n";
        text += $"[color={cP2}]🔵 P2 资金[/color]\n";
        text += $"  现有: {p2Funds} G\n";
        text += $"  收入: +{p2Income} G/回合\n";
        text += $"  上限: {p2FundsMax} G\n";

        UnitLists.Text = text;
    }

    public void UpdateValueLists()
    {
        if (UnitLists == null) return;

        long pn1Funds = 0, p0Funds = 0, p1Funds = 0, p2Funds = 0;

        // 统计单位造价
        var all = unitManager?.AllUnits ?? new List<Infantry>();
        foreach (var u in all)
        {
            if (u == null || !IsInstanceValid(u)) continue;
            switch (u.team)
            {
                case "Player": pn1Funds += u.cost; break;
                case "Player0": p0Funds += u.cost; break;
                case "Player1": p1Funds += u.cost; break;
                case "Player2": p2Funds += u.cost; break;
            }
        }

        // 统计兵器造价
        if (weaponManager != null)
        {
            foreach (var w in weaponManager.AllWeapons.ToList())
            {
                if (w == null || !IsInstanceValid(w)) continue;
                switch (w.team)
                {
                    case "Player": pn1Funds += w.cost; break;
                    case "Player0": p0Funds += w.cost; break;
                    case "Player1": p1Funds += w.cost; break;
                    case "Player2": p2Funds += w.cost; break;
                }
            }
        }

        long totalFunds = pn1Funds + p0Funds + p1Funds + p2Funds;

        string cP1 = "#ff4d4d";
        string cP2 = "#4d80ff";
        string cP0 = "#b3b3b3";
        string cPN1 = "#b366ff";
        string cTitle = "#ffaa33";

        string text = $" [color={cTitle}]💎 单位价值统计[/color] \n";
        text += $"  [color={cPN1}]🟣P-1[/color]  [color={cP0}]⚪P0[/color]  [color={cP1}]🔴P1[/color]  [color={cP2}]🔵P2[/color]\n";
        text += $"💵 [color={cPN1}]{pn1Funds,7}|[color={cP0}]{p0Funds,7}|[color={cP1}]{p1Funds,7}|[color={cP2}]{p2Funds,7}[/color]\n";
        text += $"\n[color={cTitle}]===总计===[/color]\n";
        text += $"💰 {totalFunds} G\n";

        UnitLists.Text = text;
    }

    // ========== ✅ 新增：统计面板收缩/展开 ==========
    private void SetupUnitListsToggle()
    {
        var mainScene = GetTree().CurrentScene;
        if (UnitLists == null) return;

        // 创建收缩按钮
        unitListsToggleBtn = new Button();
        unitListsToggleBtn.Name = "UnitListsToggleBtn";
        unitListsToggleBtn.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        unitListsToggleBtn.OffsetLeft = -80;
        unitListsToggleBtn.OffsetTop = 0;
        unitListsToggleBtn.OffsetRight = -20;
        unitListsToggleBtn.OffsetBottom = 32;
        TranslateGroup.SetText(unitListsToggleBtn, "📊 统计");
        unitListsToggleBtn.AddThemeFontSizeOverride("font_size", 14);
        unitListsToggleBtn.AddThemeColorOverride("font_color", Colors.White);
        var btnStyle = new StyleBoxFlat();
        btnStyle.BgColor = new Color(0.2f, 0.4f, 0.6f, 0.9f);
        btnStyle.SetCornerRadiusAll(8);
        unitListsToggleBtn.AddThemeStyleboxOverride("normal", btnStyle);
        var btnStyleHover = new StyleBoxFlat();
        btnStyleHover.BgColor = new Color(0.3f, 0.5f, 0.7f, 0.9f);
        btnStyleHover.SetCornerRadiusAll(8);
        unitListsToggleBtn.AddThemeStyleboxOverride("hover", btnStyleHover);
        // ✅ 把按钮放进 CanvasLayer 中（作为 UI 层，不受相机缩放影响）
        var canvasLayer = mainScene.GetNodeOrNull<CanvasLayer>("CanvasLayer");
        if (canvasLayer != null)
        {
            canvasLayer.AddChild(unitListsToggleBtn);
        }
        else
        {
            mainScene.AddChild(unitListsToggleBtn);
        }

        // 点击切换展开/收缩
        unitListsToggleBtn.Pressed += ToggleUnitLists;

        // 初始状态：展开
        unitListsExpanded = true;
    }

    private void ToggleUnitLists()
    {
        unitListsExpanded = !unitListsExpanded;

        if (unitListsExpanded)
        {
            // 展开：显示面板
            UnitLists.Visible = true;
            TranslateGroup.SetText(unitListsToggleBtn, "📊 统计");
        }
        else
        {
            // 收缩：隐藏面板
            UnitLists.Visible = false;
            TranslateGroup.SetText(unitListsToggleBtn, "📊 统计");
        }
    }
}
