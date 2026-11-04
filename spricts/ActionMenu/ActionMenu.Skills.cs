// ActionMenu.cs - 完整修复版，移除所有unloadButton直接引用
using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Net;

// ActionMenu · Skills（由 ActionMenu.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class ActionMenu
{
    public void OnScentPressed()
    {
        GetViewport().SetInputAsHandled();
        if (currentUnit == null || currentWeapon != null) return;
        currentUnit.ToggleScent();
        Hide();
    }

    public void OnStealthPressed()
    {
        GetViewport().SetInputAsHandled();
        if (currentUnit == null || currentWeapon != null) return;
        // ✅ 遥控搭载模块：舱内隐身（条款5可同步到运输车）
        if (currentUnit.CanPerformLoadedStealth())
        {
            currentUnit.PerformLoadedStealth();
            Hide();
            return;
        }
        currentUnit.ToggleStealth();   // 无弹/锁定时内部静默拒绝
        TranslateGroup.SetText(stealthButton, currentUnit.isStealthed ? currentUnit.GetStealthShowButtonText() : currentUnit.GetStealthHideButtonText());
        if (currentUnit.stealthToggleCostsAction || currentUnit.state == UnitState.Acted)
            Hide();
    }
}
