using Godot;
using System;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Reflection;

// TerrainEditor · SpecialFieldEditors（由 TerrainEditor.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class TerrainEditor
{
// ========== ✅ 新增：Laser角度配置专用编辑器 ==========
    private Control CreateAngleConfigEditor(Godot.Collections.Dictionary<float, bool> dict, Action<object> onChanged)
    {
        var mainVbox = new VBoxContainer();
        mainVbox.AddThemeConstantOverride("separation", 6);

        var titleLabel = new Label();
        TranslateGroup.SetText(titleLabel, "角度配置 (°)");
        titleLabel.AddThemeFontSizeOverride("font_size", 13);
        titleLabel.AddThemeColorOverride("font_color", Colors.Yellow);
        mainVbox.AddChild(titleLabel);

        var hintLabel = new Label();
        TranslateGroup.SetText(hintLabel, "勾选=启用该角度发射");
        hintLabel.AddThemeFontSizeOverride("font_size", 10);
        hintLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.6f));
        mainVbox.AddChild(hintLabel);

        var anglesContainer = new VBoxContainer();
        anglesContainer.Name = "AnglesContainer";
        anglesContainer.AddThemeConstantOverride("separation", 4);
        mainVbox.AddChild(anglesContainer);

        Action refreshAngles = () => { };
        refreshAngles = () => {
            foreach (var child in anglesContainer.GetChildren())
                child.QueueFree();

            if (dict == null || dict.Count == 0)
            {
                var emptyLabel = new Label();
                TranslateGroup.SetText(emptyLabel, "(无配置)");
                emptyLabel.AddThemeFontSizeOverride("font_size", 11);
                emptyLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
                anglesContainer.AddChild(emptyLabel);
                return;
            }

            var sortedPairs = dict.OrderBy(kvp => kvp.Key).ToList();
            foreach (var kvp in sortedPairs)
            {
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 8);

                var angleLabel = new Label();
                angleLabel.Text = $"{kvp.Key:F1}°";
                angleLabel.CustomMinimumSize = new Vector2(60, 24);
                angleLabel.AddThemeFontSizeOverride("font_size", 12);
                angleLabel.AddThemeColorOverride("font_color", kvp.Value ? Colors.Green : Colors.Red);
                row.AddChild(angleLabel);

                var checkBox = new CheckBox();
                checkBox.ButtonPressed = kvp.Value;
                checkBox.CustomMinimumSize = new Vector2(24, 24);
                float capturedAngle = kvp.Key;
                bool currentValue = kvp.Value;
                checkBox.Toggled += (pressed) => {
                    dict[capturedAngle] = pressed;
                    onChanged(dict);
                    refreshAngles?.Invoke();
                    if (currentEditingTarget is Laser laser)
                    {
                        if (pressed) laser.EnableAngle(capturedAngle);
                        else laser.DisableAngle(capturedAngle);
                    }
                };
                row.AddChild(checkBox);

                var statusLabel = new Label();
                statusLabel.Text = currentValue ? TranslateGroup.T("✓ 启用") : TranslateGroup.T("✗ 禁用");
                statusLabel.AddThemeFontSizeOverride("font_size", 10);
                statusLabel.AddThemeColorOverride("font_color", currentValue ? new Color(0.3f, 1, 0.3f) : new Color(1, 0.3f, 0.3f));
                row.AddChild(statusLabel);

                var deleteBtn = new Button();
                TranslateGroup.SetText(deleteBtn, "删除");
                deleteBtn.CustomMinimumSize = new Vector2(50, 22);
                deleteBtn.AddThemeFontSizeOverride("font_size", 10);
                float deleteAngle = kvp.Key;
                deleteBtn.Pressed += () => {
                    dict.Remove(deleteAngle);
                    onChanged(dict);
                    refreshAngles?.Invoke();
                    if (currentEditingTarget is Laser laser)
                        laser.DisableAngle(deleteAngle);
                };
                row.AddChild(deleteBtn);

                anglesContainer.AddChild(row);
            }
        };

        refreshAngles();

        var addSeparator = new HSeparator();
        addSeparator.CustomMinimumSize = new Vector2(0, 4);
        mainVbox.AddChild(addSeparator);

        var addTitle = new Label();
        TranslateGroup.SetText(addTitle, "添加新角度:");
        addTitle.AddThemeFontSizeOverride("font_size", 12);
        addTitle.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.3f));
        mainVbox.AddChild(addTitle);

        var addRow = new HBoxContainer();
        addRow.AddThemeConstantOverride("separation", 6);

        var angleSpin = new SpinBox();
        angleSpin.MinValue = 0;
        angleSpin.MaxValue = 359;
        angleSpin.Step = 1;
        angleSpin.Value = 0;
        angleSpin.CustomMinimumSize = new Vector2(80, 28);
        angleSpin.AddThemeFontSizeOverride("font_size", 11);
        addRow.AddChild(angleSpin);

        var addBtn = new Button();
        TranslateGroup.SetText(addBtn, "添加");
        addBtn.CustomMinimumSize = new Vector2(60, 28);
        addBtn.AddThemeFontSizeOverride("font_size", 11);
        addBtn.Pressed += () => {
            float newAngle = (float)angleSpin.Value;
            while (newAngle < 0) newAngle += 360;
            while (newAngle >= 360) newAngle -= 360;

            if (!dict.ContainsKey(newAngle))
            {
                dict[newAngle] = true;
                onChanged(dict);
                refreshAngles?.Invoke();
                if (currentEditingTarget is Laser laser)
                    laser.EnableAngle(newAngle);
            }
        };
        addRow.AddChild(addBtn);

        mainVbox.AddChild(addRow);

        var presetRow = new HBoxContainer();
        presetRow.AddThemeConstantOverride("separation", 4);
        var presetLabel = new Label();
        TranslateGroup.SetText(presetLabel, "快速添加:");
        presetLabel.AddThemeFontSizeOverride("font_size", 10);
        presetRow.AddChild(presetLabel);

        float[] presets = { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f };
        foreach (var preset in presets)
        {
            var presetBtn = new Button();
            presetBtn.Text = $"{preset:F0}°";
            presetBtn.CustomMinimumSize = new Vector2(40, 24);
            presetBtn.AddThemeFontSizeOverride("font_size", 9);
            float capturedPreset = preset;
            presetBtn.Pressed += () => {
                if (!dict.ContainsKey(capturedPreset))
                {
                    dict[capturedPreset] = true;
                    onChanged(dict);
                    refreshAngles?.Invoke();
                    if (currentEditingTarget is Laser laser)
                        laser.EnableAngle(capturedPreset);
                }
            };
            presetRow.AddChild(presetBtn);
        }
        mainVbox.AddChild(presetRow);

        if (currentEditingTarget is Laser)
        {
            var applyBtn = new Button();
            TranslateGroup.SetText(applyBtn, "🔄 应用角度配置到激光炮");
            applyBtn.CustomMinimumSize = new Vector2(200, 32);
            applyBtn.AddThemeFontSizeOverride("font_size", 12);
            applyBtn.AddThemeColorOverride("font_color", Colors.Yellow);
            var applyStyle = new StyleBoxFlat();
            applyStyle.BgColor = new Color(0.2f, 0.4f, 0.2f, 0.8f);
            applyStyle.SetCornerRadiusAll(6);
            applyBtn.AddThemeStyleboxOverride("normal", applyStyle);
            applyBtn.Pressed += () => {
                if (currentEditingTarget is Laser laser)
                {
                    laser.ApplyAngleConfigChanges();
                }
            };
            mainVbox.AddChild(applyBtn);
        }

        return mainVbox;
    }

    private Control CreateAttackMatrixEditor(
        System.Collections.Generic.IDictionary<string, int> dict,
        string title,
        Color titleColor,
        Action<object> onChanged)
    {
        var mainVbox = new VBoxContainer();
        mainVbox.AddThemeConstantOverride("separation", 4);

        var titleLabel = new Label();
        titleLabel.Text = $"⚔️ {title}";
        titleLabel.AddThemeFontSizeOverride("font_size", 13);
        titleLabel.AddThemeColorOverride("font_color", titleColor);
        mainVbox.AddChild(titleLabel);

        // 开创人2026-11-11 修复：null 字典不再走 null==null 误判主武器然后 InvalidCastException。
        if (dict == null)
        {
            var nullLabel = new Label { Text = "（字典为空/未初始化，保存过一次后生效）" };
            nullLabel.AddThemeColorOverride("font_color", Colors.Gray);
            mainVbox.AddChild(nullLabel);
            return mainVbox;
        }

        bool legacy = currentEditingTarget is Infantry;   // 只有单位的攻击矩阵用 39 防御者硬表

        if (legacy && title == TranslateGroup.T("主武器"))
        {
            var mulLabel = new Label();
            mulLabel.Text = $"{TranslateGroup.T("反击系数")}: {((Infantry)currentEditingTarget).counterMul:F1}";
            mulLabel.AddThemeFontSizeOverride("font_size", 10);
            mulLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
            mainVbox.AddChild(mulLabel);
        }

        if (legacy)
        {
            string attackerType = currentEditingTarget.GetType().Name;
            string[] defenderTypes = { "Infantry", "Mech", "Bike", "Oozium", "Recon", "APC", "LightTank", "MdTank", "Artillery", "Rocket", "AntiAir", "AntiTank", "PipeRunner", "Flare", "RepairBoat", "FlyBomb", "Fighter", "Bomber", "BCopter", "TCopter", "AntiAirMissile", "Stealth", "Dog", "Battleship", "Cruiser", "Submarine", "Lander", "Carrier", "Gunboat", "Neotank", "Megatank", "WarTank", "Rig", "Duster", "Seaplane", "ProducibleBoat", "KamikazeBoat" };

            foreach (var defenderType in defenderTypes)
            {
                var hbox = new HBoxContainer();
                hbox.AddThemeConstantOverride("separation", 8);

                var nameLabel = new Label();
                nameLabel.Text = $"→ {defenderType}:";
                nameLabel.CustomMinimumSize = new Vector2(100, 24);
                nameLabel.AddThemeFontSizeOverride("font_size", 11);
                hbox.AddChild(nameLabel);

                string key = $"{attackerType}_{defenderType}";
                int currentValue = dict.TryGetValue(key, out var v0) ? v0 : 0;

                var spinBox = new SpinBox();
                spinBox.MinValue = 0;
                spinBox.MaxValue = 999;
                spinBox.Value = currentValue;
                spinBox.CustomMinimumSize = new Vector2(80, 24);
                spinBox.AddThemeFontSizeOverride("font_size", 11);

                string capturedKey = key;
                spinBox.ValueChanged += (newVal) => {
                    dict[capturedKey] = (int)newVal;
                    onChanged(dict);
                };

                hbox.AddChild(spinBox);

                if (currentValue == 0)
                {
                    var hintLabel = new Label();
                    hintLabel.Text = "-";
                    hintLabel.AddThemeColorOverride("font_color", Colors.Red);
                    hbox.AddChild(hintLabel);
                }
                else if (title == TranslateGroup.T("主武器"))
                {
                    float mul = ((Infantry)currentEditingTarget).counterMul;
                    int counterVal = Mathf.RoundToInt(currentValue * mul);
                    var counterLabel = new Label();
                    counterLabel.Text = $"{TranslateGroup.T("反")}:{counterVal}";
                    counterLabel.AddThemeFontSizeOverride("font_size", 10);
                    counterLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.8f, 0.5f));
                    hbox.AddChild(counterLabel);
                }

                mainVbox.AddChild(hbox);
            }
        }
        else
        {
            // 通用字典编辑器（开创人2026-11-11）：设施伤害表/兵器防御表等任意 string→int 字典——列出真实键，可增删改。
            var keys = new List<string>(dict.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var key in keys)
            {
                var hbox = new HBoxContainer();
                hbox.AddThemeConstantOverride("separation", 8);

                var nameLabel = new Label { Text = $"→ {key}:" };
                nameLabel.CustomMinimumSize = new Vector2(140, 24);
                nameLabel.AddThemeFontSizeOverride("font_size", 11);
                hbox.AddChild(nameLabel);

                var spinBox = new SpinBox { MinValue = 0, MaxValue = 999999, Value = dict[key], CustomMinimumSize = new Vector2(80, 24) };
                string capturedKey = key;
                spinBox.ValueChanged += (newVal) => { dict[capturedKey] = (int)newVal; onChanged(dict); };
                hbox.AddChild(spinBox);

                var delBtn = new Button { Text = "×" };
                delBtn.Pressed += () => { dict.Remove(capturedKey); onChanged(dict); BuildPropertyEditor(currentEditingTarget); };
                hbox.AddChild(delBtn);

                mainVbox.AddChild(hbox);
            }

            var addRow = new HBoxContainer();
            var keyEdit = new LineEdit { PlaceholderText = "新键名(UnitTypeId/类名)", CustomMinimumSize = new Vector2(170, 24) };
            var addBtn = new Button { Text = "＋添加" };
            addBtn.Pressed += () =>
            {
                var k = keyEdit.Text.Trim();
                if (k.Length == 0 || dict.ContainsKey(k)) return;
                dict[k] = 0;
                onChanged(dict);
                BuildPropertyEditor(currentEditingTarget);
            };
            addRow.AddChild(keyEdit);
            addRow.AddChild(addBtn);
            mainVbox.AddChild(addRow);
        }

        return mainVbox;
    }

private void ApplyPreset(System.Collections.Generic.IDictionary<string, int> dict, string attackerType, string preset)
{
    string[] defenders = { "Infantry", "Mech", "Bike", "Oozium", "Recon", "APC", "LightTank", "MdTank", "Artillery", "Rocket", "AntiAir", "AntiTank", "PipeRunner", "Flare", "RepairBoat", "FlyBomb", "Fighter", "Bomber", "BCopter", "TCopter", "AntiAirMissile", "Stealth", "Dog", "Battleship", "Cruiser", "Submarine", "Lander", "Carrier", "Gunboat", "Neotank", "Megatank", "WarTank", "Rig", "Duster", "Seaplane", "ProducibleBoat", "KamikazeBoat" };

    foreach (var d in defenders)
    {
        string key = $"{attackerType}_{d}";
        int value = preset switch
        {
            "infantry" => d switch {
                "Infantry" => 55, "Mech" => 45, "LightTank" => 15,
                "Artillery" => 25, "Rocket" => 25, "APC" => 15, "Oozium" => 0, _ => 0
            },
            "armor" => d switch {
                "Infantry" => 75, "Mech" => 70, "LightTank" => 55,
                "Artillery" => 45, "Rocket" => 55, "APC" => 45, "Oozium" => 0, _ => 0
            },
            _ => 50
        };
        dict[key] = value;
    }
}
    private void AddUnitVisionFields(Infantry unit)
    {
        var visionLabel = new Label();
        TranslateGroup.SetText(visionLabel, "=== 战争迷雾视野 ===");
        visionLabel.AddThemeFontSizeOverride("font_size", 14);
        visionLabel.AddThemeColorOverride("font_color", new Color(0.4f, 1f, 0.4f));
        propertyContainer.AddChild(visionLabel);

        CreateMemberControl(unit, "visionRange", typeof(int),
            () => unit.visionRange, (v) => { unit.visionRange = (int)v; });
        CreateMemberControl(unit, "useConfigVision", typeof(bool),
            () => unit.useConfigVision, (v) => { unit.useConfigVision = (bool)v; });
        CreateMemberControl(unit, "overrideGlobalTerrainBonus", typeof(bool),
            () => unit.overrideGlobalTerrainBonus, (v) => { unit.overrideGlobalTerrainBonus = (bool)v; });

        // ✅ 地形视野加成矩阵编辑器
        if (unit.overrideGlobalTerrainBonus)
        {
            var matrixLabel = new Label();
            TranslateGroup.SetText(matrixLabel, "--- 单位专属地形视野加成 ---");
            matrixLabel.AddThemeFontSizeOverride("font_size", 12);
            matrixLabel.AddThemeColorOverride("font_color", Colors.Yellow);
            propertyContainer.AddChild(matrixLabel);

            var matrixEditor = CreateTerrainBonusMatrixEditor(
                unit.unitTerrainVisionBonus,
                (newDict) => { unit.unitTerrainVisionBonus = newDict; }
            );
            propertyContainer.AddChild(matrixEditor);
        }
    }

    // ✅ 新增：兵器视野字段
    private void AddWeaponVisionFields(Weapon weapon)
    {
        var visionLabel = new Label();
        TranslateGroup.SetText(visionLabel, "=== 战争迷雾视野 ===");
        visionLabel.AddThemeFontSizeOverride("font_size", 14);
        visionLabel.AddThemeColorOverride("font_color", new Color(0.4f, 1f, 0.4f));
        propertyContainer.AddChild(visionLabel);

        CreateMemberControl(weapon, "visionRange", typeof(int),
            () => weapon.visionRange, (v) => { weapon.visionRange = (int)v; });
        CreateMemberControl(weapon, "useConfigVision", typeof(bool),
            () => weapon.useConfigVision, (v) => { weapon.useConfigVision = (bool)v; });
        CreateMemberControl(weapon, "visionMode", typeof(VisionMode),
            () => weapon.visionMode, (v) => { weapon.visionMode = (VisionMode)v; });
        CreateMemberControl(weapon, "overrideGlobalTerrainBonus", typeof(bool),
            () => weapon.overrideGlobalTerrainBonus, (v) => { weapon.overrideGlobalTerrainBonus = (bool)v; });

        if (weapon.overrideGlobalTerrainBonus)
        {
            var matrixLabel = new Label();
            TranslateGroup.SetText(matrixLabel, "--- 兵器专属地形视野加成 ---");
            matrixLabel.AddThemeFontSizeOverride("font_size", 12);
            matrixLabel.AddThemeColorOverride("font_color", Colors.Yellow);
            propertyContainer.AddChild(matrixLabel);

            var matrixEditor = CreateTerrainBonusMatrixEditor(
                weapon.weaponTerrainVisionBonus,
                (newDict) => { weapon.weaponTerrainVisionBonus = newDict; }
            );
            propertyContainer.AddChild(matrixEditor);
        }
    }

    // ✅ 新增：大型黑炮专属字段（方向旋转按钮、旋转开关、胜利判定）
    private void AddLargeCannonFields(LargeCannon lc)
    {
        var sectionLabel = new Label();
        TranslateGroup.SetText(sectionLabel, "=== 大型黑炮专属 ===");
        sectionLabel.AddThemeFontSizeOverride("font_size", 14);
        sectionLabel.AddThemeColorOverride("font_color", new Color(1f, 0.6f, 0.2f));
        sectionLabel.CustomMinimumSize = new Vector2(0, 28);
        propertyContainer.AddChild(sectionLabel);

        // 方向旋转按钮
        var dirLabel = new Label();
        TranslateGroup.SetText(dirLabel, "🧭 方向:");
        dirLabel.AddThemeFontSizeOverride("font_size", 12);
        dirLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.9f));
        propertyContainer.AddChild(dirLabel);

        var dirHBox = new HBoxContainer();
        dirHBox.AddThemeConstantOverride("separation", 6);
        dirHBox.CustomMinimumSize = new Vector2(0, 36);

        string[] dirNames = { TranslateGroup.T("⬆️上"), TranslateGroup.T("➡️右"), TranslateGroup.T("⬇️下"), TranslateGroup.T("⬅️左") };
        var dirValues = new[] { BlackCannon.CannonDirection.Up, BlackCannon.CannonDirection.Right, BlackCannon.CannonDirection.Down, BlackCannon.CannonDirection.Left };

        for (int i = 0; i < 4; i++)
        {
            var dirBtn = new Button();
            dirBtn.Text = dirNames[i];
            dirBtn.CustomMinimumSize = new Vector2(50, 32);
            dirBtn.AddThemeFontSizeOverride("font_size", 11);
            var capturedDir = dirValues[i];
            dirBtn.Pressed += () => {
                lc.direction = capturedDir;
                lc.UpdateDirectionVisual();
                OnPropertyChanged(lc, "direction");
                // 刷新只读信息
                RefreshLargeCannonInfo(lc);
            };
            dirHBox.AddChild(dirBtn);
        }
        propertyContainer.AddChild(dirHBox);

        // 可旋转开关
        var rotateHBox = new HBoxContainer();
        rotateHBox.AddThemeConstantOverride("separation", 8);
        rotateHBox.CustomMinimumSize = new Vector2(0, 32);
        var rotateLabel = new Label();
        TranslateGroup.SetText(rotateLabel, "🔁 允许旋转:");
        rotateLabel.CustomMinimumSize = new Vector2(120, 28);
        rotateLabel.AddThemeFontSizeOverride("font_size", 12);
        rotateLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.9f));
        rotateHBox.AddChild(rotateLabel);
        var rotateCheck = new CheckBox();
        rotateCheck.ButtonPressed = lc.canRotate;
        rotateCheck.Toggled += (pressed) => {
            lc.canRotate = pressed;
        };
        rotateHBox.AddChild(rotateCheck);
        propertyContainer.AddChild(rotateHBox);

        // 介入胜利判定
        var victoryHBox = new HBoxContainer();
        victoryHBox.AddThemeConstantOverride("separation", 8);
        victoryHBox.CustomMinimumSize = new Vector2(0, 32);
        var victoryLabel = new Label();
        TranslateGroup.SetText(victoryLabel, "🏆 介入胜利判定:");
        victoryLabel.CustomMinimumSize = new Vector2(120, 28);
        victoryLabel.AddThemeFontSizeOverride("font_size", 12);
        victoryLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.9f));
        victoryHBox.AddChild(victoryLabel);
        var victoryCheck = new CheckBox();
        victoryCheck.ButtonPressed = lc.contributesToVictory;
        victoryCheck.Toggled += (pressed) => {
            lc.contributesToVictory = pressed;
        };
        victoryHBox.AddChild(victoryCheck);
        propertyContainer.AddChild(victoryHBox);

        // 需要摧毁数量
        var countHBox = new HBoxContainer();
        countHBox.AddThemeConstantOverride("separation", 8);
        countHBox.CustomMinimumSize = new Vector2(0, 32);
        var countLabel = new Label();
        TranslateGroup.SetText(countLabel, "📊 需摧毁数量:");
        countLabel.CustomMinimumSize = new Vector2(120, 28);
        countLabel.AddThemeFontSizeOverride("font_size", 12);
        countLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.9f));
        countHBox.AddChild(countLabel);
        var countSpin = new SpinBox();
        countSpin.MinValue = 1;
        countSpin.MaxValue = 99;
        countSpin.Value = lc.victoryCountRequired;
        countSpin.CustomMinimumSize = new Vector2(80, 28);
        countSpin.AddThemeFontSizeOverride("font_size", 12);
        countSpin.ValueChanged += (newVal) => {
            lc.victoryCountRequired = (int)newVal;
        };
        countHBox.AddChild(countSpin);
        propertyContainer.AddChild(countHBox);

        // 炮口和弱点信息（只读）
        var infoLabel = new Label();
        infoLabel.Name = "LargeCannonInfoLabel";
        infoLabel.Text = $"{TranslateGroup.T("炮口")}: {lc.GetFirePointOffsetForDirection(lc.direction)} | {TranslateGroup.T("弱点")}: {lc.GetWeakPointOffsetForDirection(lc.direction)}";
        infoLabel.AddThemeFontSizeOverride("font_size", 11);
        infoLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
        propertyContainer.AddChild(infoLabel);
    }

    private void RefreshLargeCannonInfo(LargeCannon lc)
    {
        foreach (var child in propertyContainer.GetChildren())
        {
            if (child is Label label && label.Name == "LargeCannonInfoLabel")
            {
                label.Text = $"{TranslateGroup.T("炮口")}: {lc.GetFirePointOffsetForDirection(lc.direction)} | {TranslateGroup.T("弱点")}: {lc.GetWeakPointOffsetForDirection(lc.direction)}";
                break;
            }
        }
    }
    private void CreateCustomDamageUI()
    {
        customDamageContainer = new VBoxContainer();
        customDamageContainer.Position = new Vector2(10, 290);
        customDamageContainer.Size = new Vector2(260, 160);
        customDamageContainer.AddThemeConstantOverride("separation", 5);

        var sectionTitle = new Label();
        TranslateGroup.SetText(sectionTitle, "=== 格子自定义伤害 ===");
        sectionTitle.AddThemeFontSizeOverride("font_size", 13);
        sectionTitle.AddThemeColorOverride("font_color", Colors.Yellow);
        customDamageContainer.AddChild(sectionTitle);

        var destroyHBox = new HBoxContainer();
        var destroyLabel = new Label { Text = TranslateGroup.T($"可摧毁{TranslateGroup.T("单位")}:"), CustomMinimumSize = new Vector2(100, 22) };
        destroyLabel.AddThemeFontSizeOverride("font_size", 11);
        canDestroyCheckBox = new CheckBox { ButtonPressed = true };
        destroyHBox.AddChild(destroyLabel);
        destroyHBox.AddChild(canDestroyCheckBox);
        customDamageContainer.AddChild(destroyHBox);

        var damageHBox = new HBoxContainer();
        var damageLabel = new Label { Text = TranslateGroup.T("固定伤害/回合:"), CustomMinimumSize = new Vector2(100, 22) };
        damageLabel.AddThemeFontSizeOverride("font_size", 11);
        fixedDamageSpinBox = new SpinBox { MinValue = -999, MaxValue = 999, Value = 0, CustomMinimumSize = new Vector2(80, 22) };
        fixedDamageSpinBox.AddThemeFontSizeOverride("font_size", 11);
        damageHBox.AddChild(damageLabel);
        damageHBox.AddChild(fixedDamageSpinBox);
        customDamageContainer.AddChild(damageHBox);

        var attackHBox = new HBoxContainer();
        var attackLabel = new Label { Text = TranslateGroup.T("固定攻击/回合:"), CustomMinimumSize = new Vector2(100, 22) };
        attackLabel.AddThemeFontSizeOverride("font_size", 11);
        fixedAttackSpinBox = new SpinBox { MinValue = -999, MaxValue = 999, Value = 0, CustomMinimumSize = new Vector2(80, 22) };
        fixedAttackSpinBox.AddThemeFontSizeOverride("font_size", 11);
        attackHBox.AddChild(attackLabel);
        attackHBox.AddChild(fixedAttackSpinBox);
        customDamageContainer.AddChild(attackHBox);

        var overMaxHBox = new HBoxContainer();
        var overMaxLabel = new Label { Text = TranslateGroup.T("可超最大血量:"), CustomMinimumSize = new Vector2(100, 22) };
        overMaxLabel.AddThemeFontSizeOverride("font_size", 11);
        canOverMaxCheckBox = new CheckBox { ButtonPressed = false };
        overMaxHBox.AddChild(overMaxLabel);
        overMaxHBox.AddChild(canOverMaxCheckBox);
        customDamageContainer.AddChild(overMaxHBox);

        var maxHealthHBox = new HBoxContainer();
        var maxHealthLabel = new Label { Text = TranslateGroup.T("最大血量增减/回合:"), CustomMinimumSize = new Vector2(100, 22) };
        maxHealthLabel.AddThemeFontSizeOverride("font_size", 11);
        maxHealthChangeSpinBox = new SpinBox { MinValue = -99, MaxValue = 99, Value = 0, CustomMinimumSize = new Vector2(80, 22) };
        maxHealthChangeSpinBox.AddThemeFontSizeOverride("font_size", 11);
        maxHealthHBox.AddChild(maxHealthLabel);
        maxHealthHBox.AddChild(maxHealthChangeSpinBox);
        customDamageContainer.AddChild(maxHealthHBox);

        terrainMenuPanel.AddChild(customDamageContainer);
    }

    private void CreateAmmoFuelUI()
    {
        ammoFuelContainer = new VBoxContainer();
        ammoFuelContainer.Position = new Vector2(10, 455);
        ammoFuelContainer.Size = new Vector2(260, 220);
        ammoFuelContainer.AddThemeConstantOverride("separation", 5);

        var sectionTitle = new Label();
        TranslateGroup.SetText(sectionTitle, "=== 弹药/燃料系统 ===");
        sectionTitle.AddThemeFontSizeOverride("font_size", 13);
        sectionTitle.AddThemeColorOverride("font_color", new Color(1f, 0.6f, 0.2f));
        ammoFuelContainer.AddChild(sectionTitle);

        var ammoTitle = new Label();
        TranslateGroup.SetText(ammoTitle, "【弹药】");
        ammoTitle.AddThemeFontSizeOverride("font_size", 12);
        ammoTitle.AddThemeColorOverride("font_color", new Color(0.9f, 0.7f, 0.2f));
        ammoFuelContainer.AddChild(ammoTitle);

        var ammoHBox = new HBoxContainer();
        var ammoLabel = new Label { Text = TranslateGroup.T("弹药变化/回合:"), CustomMinimumSize = new Vector2(100, 22) };
        ammoLabel.AddThemeFontSizeOverride("font_size", 11);
        fixedAmmoSpinBox = new SpinBox { MinValue = -999, MaxValue = 999, Value = 0, CustomMinimumSize = new Vector2(80, 22) };
        fixedAmmoSpinBox.AddThemeFontSizeOverride("font_size", 11);
        ammoHBox.AddChild(ammoLabel);
        ammoHBox.AddChild(fixedAmmoSpinBox);
        ammoFuelContainer.AddChild(ammoHBox);

        var ammoOverHBox = new HBoxContainer();
        var ammoOverLabel = new Label { Text = TranslateGroup.T("可超弹药上限:"), CustomMinimumSize = new Vector2(100, 22) };
        ammoOverLabel.AddThemeFontSizeOverride("font_size", 11);
        ammoCanOverMaxCheckBox = new CheckBox { ButtonPressed = false };
        ammoOverHBox.AddChild(ammoOverLabel);
        ammoOverHBox.AddChild(ammoCanOverMaxCheckBox);
        ammoFuelContainer.AddChild(ammoOverHBox);

        var ammoZeroHBox = new HBoxContainer();
        var ammoZeroLabel = new Label { Text = TranslateGroup.T("可归零:"), CustomMinimumSize = new Vector2(100, 22) };
        ammoZeroLabel.AddThemeFontSizeOverride("font_size", 11);
        ammoCanReachZeroCheckBox = new CheckBox { ButtonPressed = true };
        ammoZeroHBox.AddChild(ammoZeroLabel);
        ammoZeroHBox.AddChild(ammoCanReachZeroCheckBox);
        ammoFuelContainer.AddChild(ammoZeroHBox);

        // ✦优化：全种类弹药编辑（选择种类后设置该种类的增减/上限增减）
        var kindHBox = new HBoxContainer();
        var kindLabel = new Label { Text = TranslateGroup.T("弹药种类:"), CustomMinimumSize = new Vector2(100, 22) };
        kindLabel.AddThemeFontSizeOverride("font_size", 11);
        ammoKindOptionButton = new OptionButton { CustomMinimumSize = new Vector2(130, 22) };
        ammoKindOptionButton.AddThemeFontSizeOverride("font_size", 11);
        foreach (var kn in new[] { "主弹药", "自爆弹", "照明弹", "隐身弹", "显形弹", "迷彩弹", "生产弹", "建造弹" })
            ammoKindOptionButton.AddItem(TranslateGroup.T(kn));
        ammoKindOptionButton.ItemSelected += _ => LoadAmmoKindSpinValues();
        kindHBox.AddChild(kindLabel);
        kindHBox.AddChild(ammoKindOptionButton);
        ammoFuelContainer.AddChild(kindHBox);

        var kindChangeHBox = new HBoxContainer();
        var kindChangeLabel = new Label { Text = TranslateGroup.T("该种类增减/回合:"), CustomMinimumSize = new Vector2(100, 22) };
        kindChangeLabel.AddThemeFontSizeOverride("font_size", 11);
        ammoKindChangeSpinBox = new SpinBox { MinValue = -99, MaxValue = 99, Value = 0, CustomMinimumSize = new Vector2(80, 22) };
        ammoKindChangeSpinBox.AddThemeFontSizeOverride("font_size", 11);
        kindChangeHBox.AddChild(kindChangeLabel);
        kindChangeHBox.AddChild(ammoKindChangeSpinBox);
        ammoFuelContainer.AddChild(kindChangeHBox);

        var kindMaxHBox = new HBoxContainer();
        var kindMaxLabel = new Label { Text = TranslateGroup.T("该种类上限增减:"), CustomMinimumSize = new Vector2(100, 22) };
        kindMaxLabel.AddThemeFontSizeOverride("font_size", 11);
        ammoKindMaxSpinBox = new SpinBox { MinValue = -99, MaxValue = 99, Value = 0, CustomMinimumSize = new Vector2(80, 22) };
        ammoKindMaxSpinBox.AddThemeFontSizeOverride("font_size", 11);
        kindMaxHBox.AddChild(kindMaxLabel);
        kindMaxHBox.AddChild(ammoKindMaxSpinBox);
        ammoFuelContainer.AddChild(kindMaxHBox);

        var separator = new HSeparator();
        separator.CustomMinimumSize = new Vector2(0, 4);
        ammoFuelContainer.AddChild(separator);

        var fuelTitle = new Label();
        TranslateGroup.SetText(fuelTitle, "【燃料】");
        fuelTitle.AddThemeFontSizeOverride("font_size", 12);
        fuelTitle.AddThemeColorOverride("font_color", new Color(0.2f, 0.7f, 0.9f));
        ammoFuelContainer.AddChild(fuelTitle);

        var fuelHBox = new HBoxContainer();
        var fuelLabel = new Label { Text = TranslateGroup.T("燃料变化/回合:"), CustomMinimumSize = new Vector2(100, 22) };
        fuelLabel.AddThemeFontSizeOverride("font_size", 11);
        fixedFuelSpinBox = new SpinBox { MinValue = -999, MaxValue = 999, Value = 0, CustomMinimumSize = new Vector2(80, 22) };
        fixedFuelSpinBox.AddThemeFontSizeOverride("font_size", 11);
        fuelHBox.AddChild(fuelLabel);
        fuelHBox.AddChild(fixedFuelSpinBox);
        ammoFuelContainer.AddChild(fuelHBox);

        var maxFuelHBox = new HBoxContainer();
        var maxFuelLabel = new Label { Text = TranslateGroup.T("燃油上限增减/回合:"), CustomMinimumSize = new Vector2(100, 22) };
        maxFuelLabel.AddThemeFontSizeOverride("font_size", 11);
        maxFuelChangeSpinBox = new SpinBox { MinValue = -99, MaxValue = 99, Value = 0, CustomMinimumSize = new Vector2(80, 22) };
        maxFuelChangeSpinBox.AddThemeFontSizeOverride("font_size", 11);
        maxFuelHBox.AddChild(maxFuelLabel);
        maxFuelHBox.AddChild(maxFuelChangeSpinBox);
        ammoFuelContainer.AddChild(maxFuelHBox);

        var fuelOverHBox = new HBoxContainer();
        var fuelOverLabel = new Label { Text = TranslateGroup.T("可超燃料上限:"), CustomMinimumSize = new Vector2(100, 22) };
        fuelOverLabel.AddThemeFontSizeOverride("font_size", 11);
        fuelCanOverMaxCheckBox = new CheckBox { ButtonPressed = false };
        fuelOverHBox.AddChild(fuelOverLabel);
        fuelOverHBox.AddChild(fuelCanOverMaxCheckBox);
        ammoFuelContainer.AddChild(fuelOverHBox);

        var fuelZeroHBox = new HBoxContainer();
        var fuelZeroLabel = new Label { Text = TranslateGroup.T("可归零:"), CustomMinimumSize = new Vector2(100, 22) };
        fuelZeroLabel.AddThemeFontSizeOverride("font_size", 11);
        fuelCanReachZeroCheckBox = new CheckBox { ButtonPressed = true };
        fuelZeroHBox.AddChild(fuelZeroLabel);
        fuelZeroHBox.AddChild(fuelCanReachZeroCheckBox);
        ammoFuelContainer.AddChild(fuelZeroHBox);

        terrainMenuPanel.AddChild(ammoFuelContainer);
    }

    private void LoadCustomDamageValues(Grids grid)
    {
        if (grid == null) return;
        canDestroyCheckBox.ButtonPressed = grid.canDestroyUnit;
        fixedDamageSpinBox.Value = grid.fixedDamagePerTurn;
        fixedAttackSpinBox.Value = grid.fixedAttackPerTurn;
        canOverMaxCheckBox.ButtonPressed = grid.canOverMaxHealth;
        maxHealthChangeSpinBox.Value = grid.fixedMaxHealthChangePerTurn;
    }

    private void LoadAmmoFuelValues(Grids grid)
    {
        if (grid == null) return;
        fixedAmmoSpinBox.Value = grid.fixedAmmoChangePerTurn;
        ammoCanOverMaxCheckBox.ButtonPressed = grid.ammoCanOverMax;
        ammoCanReachZeroCheckBox.ButtonPressed = grid.ammoCanReachZero;
        fixedFuelSpinBox.Value = grid.fixedFuelChangePerTurn;
        maxFuelChangeSpinBox.Value = grid.fixedMaxFuelChangePerTurn;
        fuelCanOverMaxCheckBox.ButtonPressed = grid.fuelCanOverMax;
        fuelCanReachZeroCheckBox.ButtonPressed = grid.fuelCanReachZero;
        ammoKindEditGrid = grid;
        LoadAmmoKindSpinValues();
    }

    /// <summary>✦优化：按当前选中的弹药种类刷新增减/上限增减SpinBox</summary>
    private void LoadAmmoKindSpinValues()
    {
        if (ammoKindOptionButton == null || ammoKindEditGrid == null) return;
        var kind = (Infantry.AmmoKind)(int)ammoKindOptionButton.Selected;
        ammoKindChangeSpinBox.Value = ammoKindEditGrid.GetAmmoKindChange(kind);
        ammoKindMaxSpinBox.Value = ammoKindEditGrid.GetAmmoKindMaxChange(kind);
    }

    private void SaveCustomDamageValues(Grids grid)
    {
        if (grid == null) return;
        grid.canDestroyUnit = canDestroyCheckBox.ButtonPressed;
        grid.fixedDamagePerTurn = (int)fixedDamageSpinBox.Value;
        grid.fixedAttackPerTurn = (int)fixedAttackSpinBox.Value;
        grid.canOverMaxHealth = canOverMaxCheckBox.ButtonPressed;
        grid.fixedMaxHealthChangePerTurn = (int)maxHealthChangeSpinBox.Value;
    }

    private void SaveAmmoFuelValues(Grids grid)
    {
        if (grid == null) return;
        grid.fixedAmmoChangePerTurn = (int)fixedAmmoSpinBox.Value;
        grid.ammoCanOverMax = ammoCanOverMaxCheckBox.ButtonPressed;
        grid.ammoCanReachZero = ammoCanReachZeroCheckBox.ButtonPressed;
        grid.fixedFuelChangePerTurn = (int)fixedFuelSpinBox.Value;
        grid.fixedMaxFuelChangePerTurn = (int)maxFuelChangeSpinBox.Value;
        grid.fuelCanOverMax = fuelCanOverMaxCheckBox.ButtonPressed;
        grid.fuelCanReachZero = fuelCanReachZeroCheckBox.ButtonPressed;
        if (ammoKindOptionButton != null)
        {
            var kind = (Infantry.AmmoKind)(int)ammoKindOptionButton.Selected;
            grid.SetAmmoKindChange(kind, (int)ammoKindChangeSpinBox.Value);
            grid.SetAmmoKindMaxChange(kind, (int)ammoKindMaxSpinBox.Value);
        }
    }
}
