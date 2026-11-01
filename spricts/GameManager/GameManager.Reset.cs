// GameManager.cs - 添加完整的单位统计系统
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// GameManager · Reset（由 GameManager.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class GameManager
{

    // 资金 getter/setter 已内联到字段区域上方

    // ========== ✅ 重置游戏状态（导入地图后调用）==========
    public void ResetGameState()
    {
        gameEnded = false;
        defeatedTeams.Clear(); // ✅ 清理已失败势力记录，避免导入后延迟判定
        teamsWithPresence.Clear(); // ✅ 清理曾登场记录，重新累计
        TeamHelper.ClearAllAlliances(); // ✅ 清理同盟关系
        if (victoryPanel != null)
        {
            victoryPanel.Hide();
            victoryPanel.Modulate = new Color(1, 1, 1, 1);
        }
    }

    // ✅ 新增：供 TerrainEditor 调用的彻底重置方法
    public void ResetForMapImport()
    {
        gameEnded = false;
        defeatedTeams.Clear();
        teamsWithPresence.Clear();
        TeamHelper.ClearAllAlliances();
        selectedInfantry = null;
        selectedWeapon = null;
        isSelectingAttackTarget = false;
        turns = 1;
        turnPhase = 1;
        if (victoryPanel != null)
        {
            victoryPanel.Hide();
            victoryPanel.Modulate = new Color(1, 1, 1, 1);
        }
        // ✅ 导入期间屏蔽判定，3秒后自动解除
        blockVictoryCheck = true;
        var protectTimer = new Timer();
        protectTimer.WaitTime = 3.0f;
        protectTimer.OneShot = true;
        protectTimer.Timeout += () => {
            blockVictoryCheck = false;
            protectTimer.QueueFree();
        };
        AddChild(protectTimer);
        protectTimer.Start();
    }
}
