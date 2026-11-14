// GameManager.cs - 添加完整的单位统计系统
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// GameManager · AndroidPermission（由 GameManager.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class GameManager
{

// 新增方法
    // ========== ✅ Android 存储权限（导入外部地图文件的前提） ==========

    // 启动时请求存储权限（权限在 export_presets.cfg 中声明）
    private void RequestStoragePermissionsIfNeeded()
    {
        if (OS.GetName() != "Android") return;

        // 批量请求清单中声明的危险权限（READ/WRITE_EXTERNAL_STORAGE，旧版安卓有效）
        OS.RequestPermissions();

        // ✅ 开创人2026-11-14: 启动不再请求MANAGE_EXTERNAL_STORAGE——它跳转系统设置页,返回时Activity重建,
        // 导致"授权后必须再次进入"。特殊权限改为导入时才按需请求(EnsureStoragePermission),开局零打断。
    }

    // 是否已有存储访问权限（非安卓平台视为已有）
    public static bool HasStoragePermission()
    {
        if (OS.GetName() != "Android") return true;
        return OS.GetGrantedPermissions().Contains("android.permission.MANAGE_EXTERNAL_STORAGE");
    }

    // 导入前兜底检查：无权限则重新发起请求并返回 false（调用方应提示用户授权后重试）
    public static bool EnsureStoragePermission()
    {
        if (HasStoragePermission()) return true;
        OS.RequestPermissions();
        OS.RequestPermission("android.permission.MANAGE_EXTERNAL_STORAGE");
        return false;
    }
}
