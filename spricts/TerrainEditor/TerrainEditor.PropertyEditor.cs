using Godot;
using System;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Reflection;

// TerrainEditor · PropertyEditor（由 TerrainEditor.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class TerrainEditor
{

// 新增：打开兵器编辑器
private void OpenWeaponEditor(Weapon weapon)
{
    currentEditingTarget = weapon;
    propertyEditorTitle.Text = $"{TranslateGroup.T("编辑兵器")}: {weapon.Name} ({weapon.GetType().Name})";

    BuildPropertyEditor(weapon);

    var viewportSize = GetViewport().GetVisibleRect().Size;
    propertyEditorPanel.Position = new Vector2(
        (viewportSize.X - propertyEditorPanel.Size.X) / 2,
        (viewportSize.Y - propertyEditorPanel.Size.Y) / 2
    );
    propertyEditorPanel.Visible = true;
    isMenuOpen = true;
}

// TerrainEditor.cs - 修改 CreateMemberControl，确保修改 team 时调用 RefreshTeamVisual
private void CreateMemberControl(object target, string name, Type memberType, Func<object> getter, Action<object> setter)
{
    var hbox = new HBoxContainer();
    hbox.CustomMinimumSize = new Vector2(0, 32);
    hbox.AddThemeConstantOverride("separation", 8);

    var nameLabel = new Label();
    nameLabel.Text = name;
    nameLabel.CustomMinimumSize = new Vector2(150, 28);
    nameLabel.AddThemeFontSizeOverride("font_size", 12);
    nameLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.9f));
    hbox.AddChild(nameLabel);

    var value = getter();

    Action<object> wrappedSetter = (newValue) => {
        setter(newValue);

        if (name == "team" && target is Node2D node)
        {
            RefreshTeamVisual(target);
        }
        else if (name == "facilityTeam" && target is Facility facility)
        {
            facility.UpdateCityVisual();
            var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
            gm?.UpdateUnitLists();
            // ✅ 开创人2026-11-11：编辑模式改完单位属性，立即结算非法地形策略——
            // 模式0(Warside)且脚下已非法 → 当场自毁（标准死亡管线），菜单体面退出、选中/范围/引用全部清零
            gm?.EnforceIllegalTerrainAfterEdit(target as Infantry);
        }
        else
        {
            OnPropertyChanged(target, name);
        }
    };

    Control editor = CreateEditorForType(memberType, value, getter, wrappedSetter);

    if (editor != null)
    {
        editor.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        hbox.AddChild(editor);
    }

    propertyContainer.AddChild(hbox);
}

private Control CreateEditorForType(Type propType, object value, Func<object> getter, Action<object> onChanged)
{
    if (propType == typeof(int))
    {
        var spinBox = new SpinBox();
        spinBox.MinValue = -999999;
        spinBox.MaxValue = 999999;
        spinBox.Value = (int)(value ?? 0);
        spinBox.CustomMinimumSize = new Vector2(100, 28);
        spinBox.AddThemeFontSizeOverride("font_size", 12);
        spinBox.ValueChanged += (newVal) => onChanged((int)newVal);
        return spinBox;
    }

    if (propType == typeof(float))
    {
        var spinBox = new SpinBox();
        spinBox.MinValue = -999999;
        spinBox.MaxValue = 999999;
        spinBox.Step = 0.01;
        spinBox.Value = (float)(value ?? 0f);
        spinBox.CustomMinimumSize = new Vector2(100, 28);
        spinBox.AddThemeFontSizeOverride("font_size", 12);
        spinBox.ValueChanged += (newVal) => onChanged((float)newVal);
        return spinBox;
    }

    if (propType == typeof(bool))
    {
        var checkBox = new CheckBox();
        checkBox.ButtonPressed = (bool)(value ?? false);
        checkBox.CustomMinimumSize = new Vector2(60, 28);
        checkBox.Toggled += (pressed) => onChanged(pressed);
        return checkBox;
    }

    if (propType == typeof(string))
    {
        var lineEdit = new LineEdit();
        lineEdit.Text = (string)(value ?? "");
        lineEdit.CustomMinimumSize = new Vector2(120, 28);
        lineEdit.AddThemeFontSizeOverride("font_size", 12);
        lineEdit.TextChanged += (newText) => onChanged(newText);
        return lineEdit;
    }

    if (propType.IsEnum)
    {
        var optionBtn = new OptionButton();
        var enumNames = Enum.GetNames(propType);

        for (int i = 0; i < enumNames.Length; i++)
        {
            optionBtn.AddItem(enumNames[i], i);
        }

        optionBtn.Selected = (int)(value ?? 0);
        optionBtn.CustomMinimumSize = new Vector2(120, 28);
        optionBtn.AddThemeFontSizeOverride("font_size", 12);
        optionBtn.ItemSelected += (index) => {
            var newValue = Enum.ToObject(propType, (int)index);
            onChanged(newValue);
        };
        return optionBtn;
    }

    if (propType == typeof(Color))
    {
        var colorPicker = new ColorPickerButton();
        colorPicker.Color = (Color)(value ?? Colors.White);
        colorPicker.CustomMinimumSize = new Vector2(80, 28);
        colorPicker.ColorChanged += (newColor) => onChanged(newColor);
        return colorPicker;
    }

    if (propType == typeof(Vector2))
    {
        var vec = (Vector2)(value ?? Vector2.Zero);
        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 4);

        var xSpin = new SpinBox { MinValue = -99999, MaxValue = 99999, Step = 0.1, Value = vec.X, CustomMinimumSize = new Vector2(70, 28) };
        var ySpin = new SpinBox { MinValue = -99999, MaxValue = 99999, Step = 0.1, Value = vec.Y, CustomMinimumSize = new Vector2(70, 28) };

        var capturedGetter = getter;
        xSpin.ValueChanged += (v) => {
            var current = (Vector2)(capturedGetter?.Invoke() ?? Vector2.Zero);
            var newVec = new Vector2((float)v, current.Y);
            onChanged(newVec);
        };
        ySpin.ValueChanged += (v) => {
            var current = (Vector2)(capturedGetter?.Invoke() ?? Vector2.Zero);
            var newVec = new Vector2(current.X, (float)v);
            onChanged(newVec);
        };

        hbox.AddChild(new Label { Text = "X:", CustomMinimumSize = new Vector2(15, 20) });
        hbox.AddChild(xSpin);
        hbox.AddChild(new Label { Text = "Y:", CustomMinimumSize = new Vector2(15, 20) });
        hbox.AddChild(ySpin);
        return hbox;
    }

    if (propType == typeof(Vector2I))
    {
        var vec = (Vector2I)(value ?? Vector2I.Zero);
        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 4);

        var xSpin = new SpinBox { MinValue = -99999, MaxValue = 99999, Value = vec.X, CustomMinimumSize = new Vector2(70, 28) };
        var ySpin = new SpinBox { MinValue = -99999, MaxValue = 99999, Value = vec.Y, CustomMinimumSize = new Vector2(70, 28) };

        var capturedGetter = getter;
        xSpin.ValueChanged += (v) => {
            var current = (Vector2I)(capturedGetter?.Invoke() ?? Vector2I.Zero);
            onChanged(new Vector2I((int)v, current.Y));
        };
        ySpin.ValueChanged += (v) => {
            var current = (Vector2I)(capturedGetter?.Invoke() ?? Vector2I.Zero);
            onChanged(new Vector2I(current.X, (int)v));
        };

        hbox.AddChild(new Label { Text = "X:", CustomMinimumSize = new Vector2(15, 20) });
        hbox.AddChild(xSpin);
        hbox.AddChild(new Label { Text = "Y:", CustomMinimumSize = new Vector2(15, 20) });
        hbox.AddChild(ySpin);
        return hbox;
    }

    if (propType.IsGenericType && propType.GetGenericTypeDefinition() == typeof(Godot.Collections.Dictionary<,>))
    {
        var keyType = propType.GetGenericArguments()[0];
        var valueType = propType.GetGenericArguments()[1];
        var dict = value as System.Collections.IDictionary;

        if (keyType == typeof(float) && valueType == typeof(bool))
        {
            return CreateAngleConfigEditor(
                value as Godot.Collections.Dictionary<float, bool>,
                (newDict) => onChanged(newDict)
            );
        }
        if (keyType == typeof(string) && valueType == typeof(int))
        {
            var dictin = value as Godot.Collections.Dictionary<string, int>;
            bool isPrimary = (currentEditingTarget as Infantry)?.attackMatrix == dict;
            string title = isPrimary ? TranslateGroup.T("主武器") : TranslateGroup.T("副武器");
            Color color = isPrimary ? Colors.Red : Colors.Blue;
            return CreateAttackMatrixEditor(dictin, title, color, (newDict) => onChanged(newDict));
        }

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 2);

        var label = new Label();
        label.Text = $"Dictionary ({(dict?.Count ?? 0)} items) - {TranslateGroup.T("只读")}";
        label.AddThemeFontSizeOverride("font_size", 11);
        label.AddThemeColorOverride("font_color", new Color(0.5f, 0.7f, 0.5f));
        vbox.AddChild(label);

        if (dict != null)
        {
            foreach (var key in dict.Keys)
            {
                var itemLabel = new Label();
                itemLabel.Text = $"  {key}: {dict[key]}";
                itemLabel.AddThemeFontSizeOverride("font_size", 10);
                itemLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
                vbox.AddChild(itemLabel);
            }
        }
        return vbox;
    }

    if (propType == typeof(Godot.Collections.Array<string>))
    {
        var array = (Godot.Collections.Array<string>)(value ?? new Godot.Collections.Array<string>());
        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 2);

        var textEdit = new TextEdit();
        textEdit.Text = string.Join(",", array);
        textEdit.CustomMinimumSize = new Vector2(120, 60);
        textEdit.AddThemeFontSizeOverride("font_size", 11);

        textEdit.TextChanged += () => {
            var newArray = new Godot.Collections.Array<string>(
                textEdit.Text.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .ToArray()
            );
            onChanged(newArray);
        };

        vbox.AddChild(textEdit);
        return vbox;
    }

    if (typeof(Node).IsAssignableFrom(propType))
    {
        var label = new Label();
        var node = value as Node;
        label.Text = node != null ? $"[{propType.Name}] {node.Name}" : "[null]";
        label.AddThemeFontSizeOverride("font_size", 11);
        label.AddThemeColorOverride("font_color", new Color(0.5f, 0.7f, 0.5f));
        return label;
    }

    var readOnly = new Label();
    readOnly.Text = $"[{propType.Name}] {value ?? "null"}";
    readOnly.AddThemeFontSizeOverride("font_size", 11);
    readOnly.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
    return readOnly;
}

    // ========== ✅ 创建通用属性编辑器（支持字段+属性） ==========
    private void CreatePropertyEditor()
    {
        propertyEditorPanel = new Panel();
        propertyEditorPanel.Name = "PropertyEditorPanel";
        propertyEditorPanel.CustomMinimumSize = new Vector2(380, 600);
        propertyEditorPanel.SetAnchorsPreset(LayoutPreset.Center);
        propertyEditorPanel.Visible = false;
        propertyEditorPanel.ZIndex = 1100;

        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = new Color(0.1f, 0.12f, 0.16f, 0.97f);
        panelStyle.SetCornerRadiusAll(12);
        panelStyle.SetBorderWidthAll(2);
        panelStyle.BorderColor = new Color(0.4f, 0.7f, 0.4f);
        propertyEditorPanel.AddThemeStyleboxOverride("panel", panelStyle);

        // ✅ 新增：拖拽手柄
        var dragHandle = new Panel();
        dragHandle.Name = "DragHandle";
        dragHandle.CustomMinimumSize = new Vector2(380, 30);
        dragHandle.Position = new Vector2(0, 0);
        var handleStyle = new StyleBoxFlat();
        handleStyle.BgColor = new Color(0.3f, 0.3f, 0.4f, 0.5f);
        handleStyle.SetCornerRadiusAll(8);
        dragHandle.AddThemeStyleboxOverride("panel", handleStyle);
        var dragHint = new Label();
        TranslateGroup.SetText(dragHint, "⋮⋮ 拖拽移动");
        dragHint.AddThemeFontSizeOverride("font_size", 10);
        dragHint.AddThemeColorOverride("font_color", new Color(0.6f, 0.7f, 0.8f));
        dragHint.Position = new Vector2(8, 2);
        dragHandle.AddChild(dragHint);
        propertyEditorPanel.AddChild(dragHandle);
        MakeMenuDraggable(propertyEditorPanel, dragHandle);

        var titleBar = new HBoxContainer();
        titleBar.Position = new Vector2(10, 35);
        titleBar.Size = new Vector2(360, 35);

        propertyEditorTitle = new Label();
        propertyEditorTitle.Name = "PropertyEditorTitle";
        TranslateGroup.SetText(propertyEditorTitle, "属性编辑器");
        propertyEditorTitle.AddThemeFontSizeOverride("font_size", 18);
        propertyEditorTitle.AddThemeColorOverride("font_color", new Color(0.4f, 1f, 0.4f));
        propertyEditorTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        titleBar.AddChild(propertyEditorTitle);

        propertyEditorCloseBtn = new Button();
        propertyEditorCloseBtn.Text = "✕";
        propertyEditorCloseBtn.CustomMinimumSize = new Vector2(35, 35);
        propertyEditorCloseBtn.Pressed += ClosePropertyEditor;
        titleBar.AddChild(propertyEditorCloseBtn);

        propertyEditorPanel.AddChild(titleBar);

        propertyScrollContainer = new ScrollContainer();
        propertyScrollContainer.Position = new Vector2(10, 75);
        propertyScrollContainer.Size = new Vector2(360, 475);
        propertyScrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

        propertyContainer = new VBoxContainer();
        propertyContainer.Name = "PropertyContainer";
        propertyContainer.AddThemeConstantOverride("separation", 6);
        propertyContainer.Size = new Vector2(360, 0);

        propertyScrollContainer.AddChild(propertyContainer);
        propertyEditorPanel.AddChild(propertyScrollContainer);

        propertyEditorSaveBtn = new Button();
        TranslateGroup.SetText(propertyEditorSaveBtn, "💾 保存修改");
        propertyEditorSaveBtn.CustomMinimumSize = new Vector2(200, 40);
        propertyEditorSaveBtn.Position = new Vector2(90, 580);
        propertyEditorSaveBtn.AddThemeFontSizeOverride("font_size", 16);
        propertyEditorSaveBtn.AddThemeColorOverride("font_color", Colors.White);

        var saveStyle = new StyleBoxFlat();
        saveStyle.BgColor = new Color(0.2f, 0.6f, 0.3f, 0.9f);
        saveStyle.SetCornerRadiusAll(8);
        propertyEditorSaveBtn.AddThemeStyleboxOverride("normal", saveStyle);

        propertyEditorSaveBtn.Pressed += OnSaveProperties;
        propertyEditorPanel.AddChild(propertyEditorSaveBtn);

        AddChild(propertyEditorPanel);
    }

    // ========== ✅ 打开单位编辑器 ==========
    private void OpenUnitEditor(Infantry unit)
    {
        CloseSelectionMenu();
        currentEditingTarget = unit;

        propertyEditorTitle.Text = $"{TranslateGroup.T("编辑单位")}: {unit.Name} ({unit.GetType().Name})";

        BuildPropertyEditor(unit);

        var viewportSize = GetViewport().GetVisibleRect().Size;
        propertyEditorPanel.Position = new Vector2(
            (viewportSize.X - propertyEditorPanel.Size.X) / 2,
            (viewportSize.Y - propertyEditorPanel.Size.Y) / 2
        );
        propertyEditorPanel.Visible = true;
        isMenuOpen = true;
    }

    private void BuildPropertyEditor(object target)
    {
        foreach (var child in propertyContainer.GetChildren())
            child.QueueFree();


        if (target is Grids grid)
        {
            BuildGridPropertyEditor(grid);
            return;
        }

        Type targetType = target.GetType();

        var fields = GetExportFields(targetType);
        var properties = GetExportProperties(targetType);

        var allMembers = new List<(string group, string name, Type type, Func<object> getter, Action<object> setter)>();

        foreach (var field in fields)
        {
            string group = GetExportGroupName(field);
            var f = field;
            allMembers.Add((group, f.Name, f.FieldType, 
                () => f.GetValue(target),
                (v) => f.SetValue(target, v)));
        }

        foreach (var prop in properties)
        {
            string group = GetExportGroupName(prop);
            var p = prop;
            allMembers.Add((group, p.Name, p.PropertyType,
                () => p.GetValue(target),
                (v) => p.SetValue(target, v)));
        }

        var grouped = allMembers
            .GroupBy(m => m.group)
            .OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            if (!string.IsNullOrEmpty(group.Key))
            {
                var groupLabel = new Label();
                groupLabel.Text = $"=== {group.Key} ===";
                groupLabel.AddThemeFontSizeOverride("font_size", 14);
                groupLabel.AddThemeColorOverride("font_color", new Color(1f, 0.8f, 0.2f));
                groupLabel.CustomMinimumSize = new Vector2(0, 25);
                propertyContainer.AddChild(groupLabel);
            }

            foreach (var member in group)
            {
                CreateMemberControl(target, member.name, member.type, member.getter, member.setter);
            }
        }


        if (target is Infantry infantry)
        {
            AddUnitVisionFields(infantry);
        }
        else if (target is Weapon weapon)
        {
            // 防御：兵器专属字段段异常时保留通用行已建好的面板内容，只记日志。
            try { AddWeaponVisionFields(weapon); }
            catch (System.Exception wEx) { GD.PushError($"[编辑模式] 兵器专属字段段失败: {wEx}"); }
            if (weapon is DeathRay dr)
        {
            dr.direction = pendingCannonDirection;
            dr.UpdateDirectionVisual();
            dr.UpdateAmmoVisual();
            dr.UpdateMultiTileVisual();
        }
        if (weapon is LargeCannon lc)
            {
                AddLargeCannonFields(lc);
            }
            // ✅ 超级兵工厂：图形化联动绑定按钮（点格增删接缝绑定）
            if (weapon is SuperFactory sf)
            {
                var bindBtn = new Button();
                TranslateGroup.SetText(bindBtn, "🔗 图形化选择联动接缝");
                bindBtn.CustomMinimumSize = new Vector2(0, 34);
                bindBtn.Pressed += () =>
                {
                    propertyEditorPanel?.Hide();
                    var initial = new List<Grids>();
                    var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
                    var map = gm?.gridManager?.map;
                    if (map != null && sf.grid != null)
                    {
                        var anchor = sf.grid.GridIndex;
                        foreach (var off in sf.linkedSeamOffsets)
                        {
                            int sx = anchor.X + off.X, sy = anchor.Y + off.Y;
                            if (sx >= 0 && sy >= 0 && sx < map.GetLength(0) && sy < map.GetLength(1))
                            {
                                var g = map[sx, sy];
                                if (g != null) initial.Add(g);
                            }
                        }
                    }
                    BeginBindingSelect("🔗 选择联动管道接缝", initial, (selected) =>
                    {
                        // 写回相对坐标
                        var rels = new Godot.Collections.Array<Vector2I>();
                        if (sf.grid != null)
                        {
                            var gm0 = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
                            foreach (var g in selected)
                            {
                                // 🌐 跨屏绑定开关：false 时点选异屏接缝直接拒收
                                if (!sf.allowCrossScreenBind && gm0?.gridManager != null && !gm0.gridManager.IsSameScreen(sf.grid.GridIndex, g.GridIndex)) continue;
                                rels.Add(g.GridIndex - sf.grid.GridIndex);
                            }
                        }
                        sf.linkedSeamOffsets = rels;
                        sf.CheckPowerLinkage();
                        TranslateGroup.SetText(spawnHintLabel, $"✅ 联动接缝已更新：{rels.Count} 处");
                    });
                };
                propertyContainer.AddChild(bindBtn);
            }
        }
    }

    private List<FieldInfo> GetExportFields(Type type)
    {
        if (exportFieldsCache.ContainsKey(type))
            return exportFieldsCache[type];

        var result = new List<FieldInfo>();

        var currentType = type;
        while (currentType != null && currentType != typeof(object))
        {
            var fields = currentType.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(f => f.GetCustomAttribute<ExportAttribute>() != null)
                .ToList();

            result.AddRange(fields);
            currentType = currentType.BaseType;
        }

        exportFieldsCache[type] = result;
        return result;
    }

    private List<PropertyInfo> GetExportProperties(Type type)
    {
        if (exportPropertiesCache.ContainsKey(type))
            return exportPropertiesCache[type];

        var result = new List<PropertyInfo>();

        var currentType = type;
        while (currentType != null && currentType != typeof(object))
        {
            var props = currentType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p => p.GetCustomAttribute<ExportAttribute>() != null)
                .Where(p => p.CanRead && p.CanWrite)
                .ToList();

            result.AddRange(props);
            currentType = currentType.BaseType;
        }

        exportPropertiesCache[type] = result;
        return result;
    }

private void OnPropertyChanged(object target, string propertyName)
{

    if (target is Grids grid)
    {

        if (propertyName == "gridType")
        {
            UpdateGridVisual(grid);

            if (terrainMenuPanel.Visible)
            {
                terrainTitleLabel.Text = $"{TranslateGroup.T("选择地形类型")} ({TranslateGroup.T("当前")}: {GetTerrainDisplayName(grid.gridType)})";
            }
        }
        else if (propertyName.Contains("Vision") || propertyName == "requiresAdjacentVision" || propertyName == "isWatchtower")
        {

            var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
            gm?.fogOfWarManager?.ForceRefresh();
        }
        return;
    }

    if (propertyName == "team" && target is Node2D node)
    {
        RefreshTeamVisual(target);
    }
    else if (propertyName == "facilityTeam" && target is Facility facility)
    {
        facility.UpdateCityVisual();
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.UpdateUnitLists();
    }
    else if (propertyName.Contains("health") || propertyName.Contains("Health"))
        {
            if (target is Infantry infantry) infantry.UpdateHpLabel();
            if (target is Weapon weapon) weapon.UpdateHpLabel();
        }
        else if (propertyName.Contains("Ammo") || propertyName.Contains("ammo"))
        {
            if (target is BlackCannon cannon) cannon.UpdateAmmoVisual();
        }
        else if (propertyName == "direction" && target is BlackCannon bc)
        {
            bc.UpdateDirectionVisual();
        }
    }

    private void RefreshTeamVisual(object target)
    {
        if (target is Infantry infantry)
        {
            infantry.UpdateHpLabel();

            // ✅ 通用：任何有 animSprite 的单位都走 GetIdleAnimName()
            if (infantry.animSprite != null && IsInstanceValid(infantry.animSprite))
            {
                string animName = infantry.GetIdleAnimName();
                if (!string.IsNullOrEmpty(animName) && infantry.animSprite.SpriteFrames?.HasAnimation(animName) == true)
                {
                    infantry.ShowAnimState(animName);
                }
            }
            else
            {
                infantry.UpdateTeamVisual();
            }

            var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
            gm?.UpdateUnitLists();
        }

        if (target is Weapon weapon && weapon.animSprite != null)
        {
            string teamPrefix = (weapon.team == "Player2") ? "cannon2" : "cannon1";

            if (weapon is BlackCannon cannon)
            {
                string dirSuffix = cannon.direction switch
                {
                    BlackCannon.CannonDirection.Up => "up",
                    BlackCannon.CannonDirection.Down => "down",
                    BlackCannon.CannonDirection.Left => "left",
                    BlackCannon.CannonDirection.Right => "right",
                    _ => "up"
                };
                string animName = $"{teamPrefix}_{dirSuffix}";
                if (weapon.animSprite.SpriteFrames.HasAnimation(animName))
                {
                    weapon.animSprite.Play(animName);
                    // ✅ 优先通过Shader tone设置势力色
                    if (weapon.animSprite.Material is ShaderMaterial shaderMat)
                        shaderMat.SetShaderParameter("tone", TeamHelper.GetTeamColor(weapon.team));
                    else
                    {
                        // ✅ 优先通过Shader tone设置势力色
                        if (weapon.animSprite.Material is ShaderMaterial shaderMat2)
                            shaderMat2.SetShaderParameter("tone", TeamHelper.GetTeamColor(weapon.team));
                        else
                            weapon.animSprite.Modulate = TeamHelper.GetTeamColor(weapon.team);
                    }
                }
            }
            else
            {
                if (weapon.animSprite.SpriteFrames.HasAnimation(teamPrefix))
                {
                    weapon.animSprite.Play(teamPrefix);
                }
                // ✅ 势力色由Shader统一处理（weapon._Ready中已应用Material）
            }

            weapon.UpdateHpLabel();
        }
    }



private void OnSaveProperties()
{
    if (currentEditingTarget == null) return;


    if (currentEditingTarget is Infantry unit)
    {
        unit.UpdateHpLabel();
        if (unit.sprite != null) unit.sprite.Modulate = unit.normal;

        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.UpdateUnitLists();
    }

    if (currentEditingTarget is Weapon weapon)
    {
        weapon.UpdateHpLabel();
        if (weapon is BlackCannon cannon)
        {
            cannon.UpdateAmmoVisual();
        }
    }
    
    // ✅ 新增：处理格子保存
    if (currentEditingTarget is Grids grid)
    {
        UpdateGridVisual(grid);
        // 刷新战争迷雾（因为视野属性可能变了）
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.fogOfWarManager?.ForceRefresh();
    }

    ClosePropertyEditor();
}

    private string GetExportGroupName(MemberInfo member)
    {
        var groupAttr = member.GetCustomAttribute<ExportGroupAttribute>();
        if (groupAttr != null) return groupAttr.Name;

        var declaringType = member.DeclaringType;
        if (declaringType != null)
        {
            var members = declaringType.GetMembers(BindingFlags.Public | BindingFlags.Instance);
            string currentGroup = "";
            foreach (var m in members)
            {
                var group = m.GetCustomAttribute<ExportGroupAttribute>();
                if (group != null) currentGroup = group.Name;
                if (m == member) return currentGroup;
            }
        }

        return "";
    }

    private void CloseSelectionMenu()
    {
        selectionMenuPanel.Visible = false;
    }

// 替换原有的 ClosePropertyEditor
private void ClosePropertyEditor()
{
    propertyEditorPanel.Visible = false;
    
    if (currentEditingTarget is Grids)
    {
        terrainMenuPanel.Visible = false;
        selectedGrid = null;
    }
    
    currentEditingTarget = null;
    isMenuOpen = false;
}

    private bool isDraggingMenu = false;
    private Vector2 dragOffset = Vector2.Zero;
    private Control currentDraggedMenu = null;
}
