using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

// Infantry · FogWar（由 Infantry.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class Infantry
{

    [ExportGroup("雾战模块")]
        // ✅ 雾战加成（可为负=减成；不改实际数值，仅生效上限/判定）
    [Export] public int fogMaxHpBonus = 0;
    [Export] public int fogMaxFuelBonus = 0;
    [Export] public int fogFlatDefenseBonus = 0;
    [Export] public int fogPrimaryAmmoMaxBonus = 0;
    [Export] public int fogExplodeAmmoMaxBonus = 0;
    [Export] public int fogFlareAmmoMaxBonus = 0;
    [Export] public int fogStealthBulletMaxBonus = 0;
    [Export] public int fogRevealBulletMaxBonus = 0;
    [Export] public int fogCamoBulletMaxBonus = 0;
    [Export] public Godot.Collections.Dictionary<string, int> fogMatrixOverride = new();
    public bool FogActive()   // 雾战生效=全局迷雾开关
    {
        // ✅ 开创人2026-11-11: 孤儿单位(AI原型/预览实例)不在场景树——GetTree()会刷"data.tree is null"引擎错误, 直接按雾战关闭处理
        if (!IsInsideTree()) return false;
        if (_fogGmCache == null || !GodotObject.IsInstanceValid(_fogGmCache))
            _fogGmCache = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        return _fogGmCache?.fogOfWarManager?.isFogOfWarEnabled == true;
    }
    public int GetEffectiveMaxHealth() => FogActive() ? maxHealth + fogMaxHpBonus : maxHealth;
    public int GetEffectiveMaxFuel() => FogActive() ? maxFuel + fogMaxFuelBonus : maxFuel;
    public int GetEffectiveMaxPrimaryAmmo() => FogActive() ? maxPrimaryAmmo + fogPrimaryAmmoMaxBonus : maxPrimaryAmmo;
    public int GetEffectiveMaxExplodeAmmo() => FogActive() ? maxExplodeAmmo + fogExplodeAmmoMaxBonus : maxExplodeAmmo;
    public int GetEffectiveMaxFlareAmmo() => FogActive() ? maxFlareAmmo + fogFlareAmmoMaxBonus : maxFlareAmmo;
    public int GetEffectiveMaxStealthBullets() => FogActive() ? maxStealthBullets + fogStealthBulletMaxBonus : maxStealthBullets;
    public int GetEffectiveMaxRevealBullets() => FogActive() ? maxRevealBullets + fogRevealBulletMaxBonus : maxRevealBullets;
    public int GetEffectiveMaxCamoBullets() => FogActive() ? maxCamoBullets + fogCamoBulletMaxBonus : maxCamoBullets;
}
