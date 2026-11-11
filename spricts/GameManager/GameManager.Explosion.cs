// GameManager.cs - 添加完整的单位统计系统
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// GameManager · Explosion（由 GameManager.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class GameManager
{

    // ========== ✅ 爆炸执行 ==========
    public void ExecuteExplosion(Infantry unit)
    {
        if (unit == null || !unit.canExplode || gridManager == null || unitManager == null) return;

        // ✅ 无限自爆模式消耗自爆弹
        if (!unit.explosionDestroysSelf)
        {
            if (!unit.TryConsumeAmmo(Infantry.AmmoKind.Explode))   // ✅ 统一弹药系统
            {
                return;
            }
        }

        var gridPos = unit.grid?.GridIndex ?? unitManager.WorldToGrid(unit.Position);
        var sourceGrid = unit.grid ?? gridManager.GetGridAt(gridPos);
        if (sourceGrid == null) return;
        int minR = unit.explosionMinRange;
        int maxR = unit.explosionMaxRange;

        // 获取受影响单位
        var affected = new List<Infantry>();
        foreach (var u in unitManager.AllUnits)
        {
            if (u == null || !IsInstanceValid(u) || u == unit) continue; // 自身伤害只由 explosionSelfDamageEnabled 专门控制
            var uGridPos = u.grid?.GridIndex ?? unitManager.WorldToGrid(u.Position);
            int dist = Mathf.Abs(uGridPos.X - gridPos.X) + Mathf.Abs(uGridPos.Y - gridPos.Y);
            if (dist >= minR && dist <= maxR && gridManager.IsRangeAllowed(sourceGrid, u.grid ?? gridManager.GetGridAt(uGridPos), unit.explosionCanCrossScreens))
            {
                // 目标筛选
                bool isEnemy = u.team != unit.team;
                bool include = unit.explosionTargetMode switch
                {
                    1 => isEnemy,   // 仅敌
                    2 => !isEnemy,  // 仅友
                    _ => true        // 所有
                };
                if (include) affected.Add(u);
            }
        }

        // 计算并应用伤害（对单位）
        foreach (var target in affected)
        {
            int damage = CalculateExplosionDamage(unit, target);
            ApplyExplosionDamage(unit, target, damage, false);
        }

        // ✅ 影响兵器（如果开启）
        if (unit.explosionAffectsWeapons && weaponManager != null)
        {
            foreach (var w in weaponManager.AllWeapons.ToList())
            {
                if (w == null || !IsInstanceValid(w)) continue;
                if (w.grid == null) continue;
                int dist = Mathf.Abs(w.grid.GridIndex.X - gridPos.X) + Mathf.Abs(w.grid.GridIndex.Y - gridPos.Y);
                if (dist < minR || dist > maxR) continue;
                if (!gridManager.IsRangeAllowed(sourceGrid, w.grid, unit.explosionCanCrossScreens)) continue;

                // 目标筛选（兵器也有 team）
                bool isEnemy = w.team != unit.team;
                bool include = unit.explosionTargetMode switch
                {
                    1 => isEnemy,   // 仅敌
                    2 => !isEnemy,  // 仅友
                    _ => true        // 所有
                };
                if (!include) continue;

                int damage = CalculateExplosionDamage(unit, w);
                ApplyExplosionDamage(unit, w, damage, true);
            }
        }

        // ✅ 自爆内伤（开启、不摧毁自己且最小范围为0时对自身造成伤害）
        if (unit.explosionSelfDamageEnabled && !unit.explosionDestroysSelf && unit.explosionMinRange == 0)
        {
            int selfDamage = CalculateExplosionDamage(unit, unit);
            ApplyExplosionDamage(unit, unit, selfDamage, false);
        }

        // 触发单位自身的 OnExplode
        unit.OnExplode();

        // 播放爆炸特效
        PlayExplosionEffect(unit.GlobalPosition);

        // 摧毁自身
        if (unit.explosionDestroysSelf)
        {
            unitManager.RemoveUnit(unit);
        }
        else
        {
            // 无限爆炸模式：重置为已行动状态
            unit.state = UnitState.Acted;
            unit.isMoved = true;
            unit.SetWaitVisual(true);
        }
    }

    // ========== ✅ 爆炸特效 ==========
    private void PlayExplosionEffect(Vector2 position)
    {
        var particles = new GpuParticles2D
        {
            Position = position,
            ZIndex = 300,
            Emitting = true,
            OneShot = true,
            Explosiveness = 1.0f,
            Amount = 80,
            Lifetime = 1.2f,
        };

        var material = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 20.0f,
            InitialVelocityMin = 60.0f,
            InitialVelocityMax = 200.0f,
            ScaleMin = 4.0f,
            ScaleMax = 10.0f,
            Color = new Color(1.0f, 0.35f, 0.0f, 1.0f),
            Gravity = new Vector3(0, 0, 0),
            DampingMin = 50.0f,
            DampingMax = 100.0f,
        };

        particles.ProcessMaterial = material;
        GetTree().CurrentScene.AddChild(particles);

        // 自动清理
        var timer = new Timer
        {
            WaitTime = 2.0f,
            OneShot = true,
            Autostart = true
        };
        timer.Timeout += () =>
        {
            particles.QueueFree();
            timer.QueueFree();
        };
        particles.AddChild(timer);

        // 屏幕震动（可选）
        var camera = GetTree().CurrentScene.GetNodeOrNull<Camera2D>("Camera2D");
        if (camera != null)
        {
            camera.Offset = new Vector2(GD.Randf() * 6 - 3, GD.Randf() * 6 - 3);
            var shakeTimer = new Timer { WaitTime = 0.15f, OneShot = true, Autostart = true };
            shakeTimer.Timeout += () => { camera.Offset = Vector2.Zero; shakeTimer.QueueFree(); };
            camera.AddChild(shakeTimer);
        }
    }

    // ========== ✅ 爆炸伤害计算 ==========
    public int CalculateExplosionDamage(Infantry source, object target)
    {
        int maxHp = 100;
        if (target is Infantry inf) maxHp = inf.maxHealth;
        else if (target is Weapon w) maxHp = w.maxHealth;

        switch (source.explosionDamageMode)
        {
            case 0: // 固定值（可为负=加血）
                return source.explosionFixedValue;
            case 1: // 百分比（最大HP，可为负=加血）
                return Mathf.RoundToInt(maxHp * source.explosionPercentValue);
            case 2: // 攻击公式（查攻防表）
                if (target is Infantry tInf)
                {
                    int dmg = source.GetPrimaryDamageFromMatrix(tInf);
                    if (dmg == 0) dmg = Mathf.RoundToInt(maxHp * source.explosionPercentValue);
                    return dmg;
                }
                // 兵器没有攻防表，回退百分比
                return Mathf.RoundToInt(maxHp * source.explosionPercentValue);
            default:
                return 0;
        }
    }

    // ========== ✅ 爆炸伤害应用 ==========
    private void ApplyExplosionDamage(Infantry source, object target, int damage, bool isWeapon)
    {
        if (damage == 0) return; // 0=无变化

        if (isWeapon && target is Weapon w)
        {
            // 兵器
            if (damage > 0)
            {
                // 扣血
                w.TakeDamage(damage, null);
                if (!source.explosionCanKill && w.health <= 0)
                    w.health = 1;
                if (w.health <= 0)
                    weaponManager.RemoveWeapon(w);
            }
            else
            {
                // 回血（负值）
                int heal = -damage;
                int newHealth = w.health + heal;
                if (!source.explosionCanExceedMaxHealth && newHealth > w.maxHealth)
                    newHealth = w.maxHealth;
                w.health = newHealth;
                w.UpdateHpLabel();
            }
        }
        else if (target is Infantry inf)
        {
            // 单位
            if (damage > 0)
            {
                // 扣血
                int newHealth = inf.health - damage;
                if (!source.explosionCanKill && newHealth <= 0)
                    newHealth = 1;
                inf.health = Mathf.Max(0, newHealth);
                inf.UpdateHpLabel();
                if (inf.health <= 0)
                    unitManager.RemoveUnit(inf);
            }
            else
            {
                // 回血（负值）
                int heal = -damage;
                int newHealth = inf.health + heal;
                if (!source.explosionCanExceedMaxHealth && newHealth > inf.maxHealth)
                    newHealth = inf.maxHealth;
                inf.health = newHealth;
                inf.UpdateHpLabel();
            }
        }
    }
}
