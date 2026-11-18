using Godot;

// ✅ 方案甲（2026-11-15）：导出启动地图——把当前战场写成 user://campaigns/main_battlefield.txt，
// 供 BootMapLoader 在运行时逐行分帧生成（消灭 main.tscn 的 700+ 格子场景子节点）。
// 使用方法：编辑器跑游戏 → 进入战场 → 全局配置菜单点"📤 导出启动地图(方案甲)"。
// commit 2（翻 BOOT_ENABLED + 掏空 main.tscn）之前必须已成功导出并验收本文件。
public partial class TerrainEditor
{
    public void ExportBootMap()
    {
        string content = BuildCompactText();
        if (content == null) { ShowImportHint(TranslateGroup.T("❌ GridManager 未初始化")); return; }

        DirAccess.MakeDirRecursiveAbsolute("user://campaigns");   // ✅ 开创人2026-11-18: res://在安卓只读,保存必须落user://
        using var f = FileAccess.Open("user://campaigns/main_battlefield.txt", FileAccess.ModeFlags.Write);
        if (f == null)
        {
            ShowImportHint($"❌ 启动地图写出失败 err={FileAccess.GetOpenError()}");
            return;
        }
        f.StoreString(content);
        ShowImportHint(TranslateGroup.T("✅ 启动地图已导出 user://campaigns/main_battlefield.txt\n（方案甲 commit2：验收后翻 BOOT_ENABLED=true + 掏空 main.tscn）"));
        GD.Print("[BootExport] user://campaigns/main_battlefield.txt 写出完成");
    }
}
