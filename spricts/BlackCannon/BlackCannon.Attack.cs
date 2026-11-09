// BlackCannon.cs - 黑炮：三角形范围，固定百分比伤害，支持自定义目标选择和改良伤害模式
// ✅ 新增：完整弹药系统 + 多回合冷却系统（还原原版高级战争原型炮/完全体差异）
using Godot;
using System.Collections.Generic;
using System.Linq;

// BlackCannon · Attack（由 BlackCannon.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class BlackCannon
{

public override void TakeDamage(int damage, Infantry attacker = null)
{
    // ========== 兵器被击伤害表（在扣血前应用）==========
    if (useWeaponDamageTable && attacker != null)
    {
        int tableDamage = GetDamageFrom(attacker.unitTypeId);
        if (tableDamage >= 0)
            damage = tableDamage;
    }
    health -= damage;
    health = Mathf.Max(0, health);
    UpdateHpLabel();

    // 1. 受伤闪烁
    if (animSprite != null)
    {
        var tween = CreateTween();
        tween.TweenProperty(animSprite, "modulate", Colors.Red, 0.1f);
        tween.TweenProperty(animSprite, "modulate", Colors.White, 0.1f);
    }

    // 2. 标记攻击者已行动
    var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
    if (gm?.selectedInfantry != null)
    {
        var attacker = gm.selectedInfantry;
        attacker.isAttacked = true;
        attacker.state   = UnitState.Acted;
        attacker.originalGrid = null;
        attacker.SetWaitVisual(true);
        gm.gridManager.HideAttackRange();
        gm.ClearSelectedInfantry();
    }

    // 3. 血量归零 → 延迟自毁
    if (health <= 0)
    {
        // ✅ 修复：保存 grid 引用，不要在这里清空
        var destroyedGrid = grid;
        
        // 从格子移除引用（但不要清空自己的 grid 字段）
        if (grid != null)
        {
            grid.weapons.Remove(this);
            if (grid.weapon == this)
                grid.weapon = null;
            // ❌ 不要：grid = null;
        }

        if (gm != null && gm.selectedWeapon == this)
            gm.selectedWeapon = null;

        // ✅ 修复：直接在这里处理地形切换，不依赖 OnDestroyed
        if (destroyedGrid != null && (destroyedGrid.gridType == GridType.PIPESEAM || destroyedGrid.gridType == GridType.METEORITE))
        {
            string terrainName = destroyedGrid.gridType == GridType.PIPESEAM ? "PIPESEAM" : "METEORITE";

            // 切换地形
            destroyedGrid.gridType = GridType.BROKENPIPE;
            destroyedGrid.TriggerDestroyChain(gm?.gridManager?.map, 0); // ✅ 摧毁链: 相邻白名单内地形链式消失(多米诺)

            // 更新格子视觉
            var sprite = destroyedGrid.GetNodeOrNull<Sprite2D>("Sprite2D");
            if (sprite != null)
            {
                var tween = CreateTween();
                tween.TweenProperty(sprite, "modulate", new Color(0.3f, 0.3f, 0.3f), 0.5f)
                     .SetTrans(Tween.TransitionType.Sine)
                     .SetEase(Tween.EaseType.InOut);
            }

        }

        // 现在可以安全清空 grid
        grid = null;

        CallDeferred(nameof(OnDestroyed));
    }
}

    // 黑炮攻击兵器 - 完全重写，避免任何延迟调用
    private void PerformAttackOnWeapon(Weapon targetWeapon)
    {
        if (remainingAttacks <= 0) return;

        // 严格有效性检查
        if (targetWeapon == null || !IsInstanceValid(targetWeapon)) 
        {
            return;
        }

        // 保存关键数据（在修改前）
        Vector2 targetPos = targetWeapon.GlobalPosition;
        string targetName = targetWeapon.Name;
        int targetHealth = targetWeapon.health;


        // 计算伤害
        int damage;
        bool willDestroy = false;

        if (useModifiedDamage)
        {
            damage = Mathf.RoundToInt(targetWeapon.maxHealth * Mathf.Abs(modifiedHealthPercent));
            damage = Mathf.Max(1, damage);

            if (useModifiedDamage)
            {
                damage = Mathf.RoundToInt(targetWeapon.maxHealth * Mathf.Abs(modifiedHealthPercent));
                damage = Mathf.Max(1, damage);

                if (modifiedHealthPercent > 0) // 扣血
                {
                    var gm2 = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
                    Infantry attacker2 = gm2?.selectedInfantry;

                    if (!CanDestroy)
                    {
                        int oldHealth2 = targetWeapon.health;
                        targetWeapon.TakeDamage(damage, attacker2);
                        if (targetWeapon.health <= 0)
                        {
                            targetWeapon.health = Mathf.Max(1, oldHealth2 - damage);
                        }
                    }
                    else
                    {
                        targetWeapon.TakeDamage(damage, attacker2);
                        if (targetWeapon.health <= 0)
                        {
                            willDestroy = true;
                            targetWeapon.health = 0;
                        }
                    }
                }
                else // ← 回血（modifiedHealthPercent < 0）
                {
                    int oldHealth = targetWeapon.health;

                    if (canOverMaxHp)
                    {
                        targetWeapon.health += damage;
                    }
                    else
                    {
                        targetWeapon.health = Mathf.Min(targetWeapon.maxHealth, targetWeapon.health + damage);
                        int actualHeal = targetWeapon.health - oldHealth;
                    }

                    // 播放回血特效
                    ShowHealEffect(targetWeapon);
                }
            }
        }
        else // 传统模式
        {
            damage = Mathf.RoundToInt(targetWeapon.maxHealth * fixedDamagePercent);
            damage = Mathf.Max(1, damage);

            // 获取攻击者
            var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
            Infantry attacker = gm?.selectedInfantry;

            if (!CanDestroy)
            {
                // 使用TakeDamage走完整流程（包括伤害表）
                int oldHealth = targetWeapon.health;
                targetWeapon.TakeDamage(damage, attacker);
                if (targetWeapon.health <= 0)
                {
                    targetWeapon.health = Mathf.Max(1, oldHealth - damage);
                }
            }
            else
            {
                targetWeapon.TakeDamage(damage, attacker);
                if (targetWeapon.health <= 0)
                {
                    willDestroy = true;
                    targetWeapon.health = 0;
                }
            }
        }

        // 更新血量显示（必须在可能销毁前）
        if (IsInstanceValid(targetWeapon))
        {
            targetWeapon.UpdateHpLabel();
        }


        // 播放特效（使用保存的位置，不依赖目标对象）
        PlayHitEffectAt(targetPos);

        // 减少攻击次数
        remainingAttacks--;

        // 处理摧毁 - 关键：立即清理引用，但延迟销毁节点
        if (willDestroy)
        {

            // ✅ 关键：立即从所有管理结构中移除，防止其他代码访问
            RemoveWeaponFromAllSystems(targetWeapon);

            // ✅ 关键：使用 Godot 的延迟销毁，而不是自定义方法
            // 这样 Godot 会自己处理生命周期
            targetWeapon.SetDeferred("process_mode", (int)ProcessModeEnum.Disabled);

            // 创建一个定时器来延迟 QueueFree，避免当前帧问题
            var timer = new Godot.Timer();
            timer.WaitTime = 0.1f;
            timer.OneShot = true;
            timer.Autostart = true;
            AddChild(timer);

            // 使用 Callable，避免捕获 targetWeapon 引用
            // 改用目标名称查找（更安全）
            string destroyedWeaponName = targetName;
            timer.Timeout += () => {
                FindAndDestroyWeapon(destroyedWeaponName);
                timer.QueueFree();
            };
        }

        // 处理攻击后状态
        HandlePostAttack();
    }
    

    // ✅ 新增：从所有系统中移除兵器引用（立即执行）
    private void RemoveWeaponFromAllSystems(Weapon weapon)
    {
        if (weapon == null) return;


        // 1. 从格子移除
        if (weapon.grid != null)
        {
            weapon.grid.weapons.Remove(weapon);
            if (weapon.grid.weapon == weapon)
            {
                weapon.grid.weapon = null;
            }
            weapon.grid = null;
        }

        // 2. 从 GameManager 移除选中
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm != null)
        {
            if (gm.selectedWeapon == weapon)
            {
                gm.selectedWeapon = null;
            }

            // 3. 从 WeaponManager 列表移除（但不销毁）
            if (gm.weaponManager != null && gm.weaponManager.AllWeapons.Contains(weapon))
            {
                gm.weaponManager.AllWeapons.Remove(weapon);
            }
        }

        // 4. 禁用交互
        var area = weapon.GetNodeOrNull<Area2D>("Area2D");
        if (area != null)
        {
            area.InputPickable = false;
            area.Monitoring = false;
            area.Monitorable = false;
        }

        // 5. 隐藏视觉
        if (weapon.animSprite != null)
        {
            weapon.animSprite.Modulate = Colors.Transparent;
        }
        if (weapon.hpLabel != null)
        {
            weapon.hpLabel.Hide();
        }
    }

    // ✅ 新增：通过名称查找并销毁兵器（避免引用问题）
    private void FindAndDestroyWeapon(string weaponName)
    {

        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.weaponManager == null)
        {
            return;
        }

        // 在场景中查找（可能还在列表中，也可能不在）
        foreach (var weapon in gm.weaponManager.AllWeapons.ToList())
        {
            if (weapon != null && IsInstanceValid(weapon) && weapon.Name == weaponName)
            {
                // ✅ 多格兵器：走 OnDestroyed 路径（保留节点，保持不可通行）
                if (weapon.isMultiTile)
                {
                    if (gm.selectedWeapon == weapon)
                        gm.selectedWeapon = null;
                    gm.weaponManager.AllWeapons.Remove(weapon);
                    weapon.OnDestroyed();
                    return;
                }

                // ✅ 特殊地形处理
                if (weapon.grid != null && (weapon.grid.gridType == GridType.PIPESEAM || weapon.grid.gridType == GridType.METEORITE))
                {
                    var targetGrid = weapon.grid;
                    targetGrid.weapons.Remove(weapon);
                    if (targetGrid.weapon == weapon)
                        targetGrid.weapon = null;
                    targetGrid.gridType = GridType.BROKENPIPE;
                    targetGrid.TriggerDestroyChain(gm?.gridManager?.map, 0); // ✅ 摧毁链: 相邻白名单内地形链式消失(多米诺)

                    var sprite = targetGrid.GetNodeOrNull<Sprite2D>("Sprite2D");
                    if (sprite != null)
                    {
                        var tween = CreateTween();
                        tween.TweenProperty(sprite, "modulate", new Color(0.3f, 0.3f, 0.3f), 0.5f);
                    }
                }

                // 再次确保从列表移除
                gm.weaponManager.AllWeapons.Remove(weapon);

                // 安全销毁
                SafeQueueFree(weapon);
                return;
            }
        }

        // 如果没找到，可能在根节点下
        var root = GetTree().CurrentScene;
        if (root != null)
        {
            var node = root.FindChild(weaponName, true, false);
            if (node is Weapon w)
            {
                // 多格兵器也走 OnDestroyed 路径
                if (w.isMultiTile)
                {
                    w.OnDestroyed();
                }
                else
                {
                    SafeQueueFree(w);
                }
            }
        }

    }

    // ✅ 新增：安全销毁节点
    private void SafeQueueFree(Node node)
    {
        if (node == null) return;

        try
        {
            if (IsInstanceValid(node) && !node.IsQueuedForDeletion())
            {
                node.QueueFree();
            }
            else
            {
            }
        }
        catch (System.Exception)
        {
        }
    }

    // ✅ 新增：播放命中特效（不依赖目标对象）
    private void PlayHitEffectAt(Vector2 position)
    {
        ShakeCamera(amplitude: 6f, times: 3);

        var flash = new CannonFlash();
        flash.Position = GlobalPosition;
        GetTree().CurrentScene.AddChild(flash);

        var hit = new HitEffect();
        hit.Position = position;
        GetTree().CurrentScene.AddChild(hit);
    }
    // 在 BlackCannon.cs 中添加这个方法
    // BlackCannon.cs - 添加对步兵的攻击方法
    public override void PerformAttack(Infantry target)
    {
        if (remainingAttacks <= 0) return;

        // 严格有效性检查
        if (target == null || !IsInstanceValid(target)) 
        {
            return;
        }

        // 保存关键数据（在修改前）
        Vector2 targetPos = target.GlobalPosition;
        string targetName = target.Name;


        // 根据模式应用伤害
        if (useModifiedDamage)
        {
            ApplyModifiedEffect(target);
        }
        else
        {
            ApplyTraditionalDamage(target);
        }

        // 播放特效（使用保存的位置，不依赖目标对象）
        PlayHitEffectAt(targetPos);

        // 减少攻击次数
        remainingAttacks--;

        // 处理攻击后状态
        HandlePostAttack();
    }

    public override void HandlePostAttack()
    {
        if (remainingAttacks <= 0 || !HasAmmoAndCooldown())
        {
            hasActed = true;
            SetVisualDark();

            var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
            gm?.gridManager.ClearWeaponRange();

            // ✅ 关键：清除选中状态
            if (gm != null) gm.selectedWeapon = null;

            var menu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
            menu?.Hide();
        }
        else
        {
            // 延迟刷新攻击范围，等待销毁完成
            var refreshTimer = GetTree().CreateTimer(0.15f);
            refreshTimer.Timeout += () => {
                if (IsInstanceValid(this) && !hasActed)
                {
                    ShowAttackRange();
                }
            };
        }
    }

    private void DeferredRemoveWeapon(Weapon weapon)
    {
        // ✅ 关键修复：多重检查确保对象有效
        if (weapon == null) 
        {

            return;
        }

        if (!IsInstanceValid(weapon))
        {

            return;
        }

        // ✅ 特殊地形处理：兵器被消灭前检查所在格子
        if (weapon.grid != null && (weapon.grid.gridType == GridType.PIPESEAM || weapon.grid.gridType == GridType.METEORITE))
        {
            string terrainName = weapon.grid.gridType == GridType.PIPESEAM ? "PIPESEAM" : "METEORITE";
            var targetGrid = weapon.grid;

            // 清理兵器引用
            targetGrid.weapons.Remove(weapon);
            if (targetGrid.weapon == weapon)
                targetGrid.weapon = null;

            // 切换地形
            targetGrid.gridType = GridType.BROKENPIPE;

            // 更新视觉
            var sprite = targetGrid.GetNodeOrNull<Sprite2D>("Sprite2D");
            if (sprite != null)
            {
                var tween = CreateTween();
                tween.TweenProperty(sprite, "modulate", new Color(0.3f, 0.3f, 0.3f), 0.5f);
            }

        }

        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm != null)
        {
            // 如果当前选中的是被摧毁的兵器，清空选中
            if (gm.selectedWeapon == weapon)
            {
                gm.selectedWeapon = null;

            }

            // 通过 WeaponManager 安全移除
            if (gm.weaponManager != null)
            {
                // ✅ 关键修复：检查 weapon 是否还在列表中
                if (gm.weaponManager.AllWeapons.Contains(weapon))
                {
                    gm.weaponManager.RemoveWeapon(weapon);
                }
                else
                {

                    weapon.QueueFree();
                }
            }
            else
            {

                weapon.QueueFree();
            }
        }
        else
        {

            weapon.QueueFree();
        }


    }

    private void ApplyTraditionalDamage(Infantry target)
    {
        int damage = Mathf.RoundToInt(target.maxHealth * fixedDamagePercent);
        damage = Mathf.Max(1, damage);

        if (!CanDestroy)
        {
            target.health = Mathf.Max(1, target.health - damage);
        }
        else
        {
            target.health -= damage;
        }

        target.UpdateHpLabel();

        if (target.health <= 0 && !target.TryFlee())
        {
            var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
            gm?.RemovePiece(target);
            target.QueueFree();
        }
    }

    // ✅ 修改：应用改良效果，支持CanOverMaxHp选项
    private void ApplyModifiedEffect(Infantry target)
    {
        // ========== 1. 血量处理 ==========
        int healthChange = Mathf.RoundToInt(target.maxHealth * Mathf.Abs(modifiedHealthPercent));

        if (modifiedHealthPercent > 0 && healthChange > 0)
        {
            // 正数：扣血
            if (!CanDestroy)
            {
                target.health = Mathf.Max(1, target.health - healthChange);
            }
            else
            {
                target.health -= healthChange;
            }
        }
        else if (modifiedHealthPercent < 0)
        {
            // 负数：回血
            int oldHealth = target.health;

            // ✅ 关键修改：根据canOverMaxHp决定是否限制上限
            if (canOverMaxHp)
            {
                // 允许超出血量上限
                target.health += healthChange;
            }
            else
            {
                // 默认：不能超过maxHealth
                target.health = Mathf.Min(target.maxHealth, target.health + healthChange);
                int actualHeal = target.health - oldHealth;
            }

            ShowHealEffect(target);
        }

        target.UpdateHpLabel();

        // ========== 2. 弹药处理 ==========
        if (modifiedAmmoPercent != 0 && target.hasPrimaryWeapon && target.primaryHasLimitedAmmo)
        {
            int ammoChange = Mathf.RoundToInt(target.maxPrimaryAmmo * Mathf.Abs(modifiedAmmoPercent));

            if (modifiedAmmoPercent > 0)
            {
                target.currentPrimaryAmmo = Mathf.Max(0, target.currentPrimaryAmmo - ammoChange);
            }
            else
            {
                // ✅ 弹药也可以超上限（如果canOverMaxHp为true）
                if (canOverMaxHp)
                {
                    target.currentPrimaryAmmo += ammoChange;
                }
                else
                {
                    int oldAmmo = target.currentPrimaryAmmo;
                    target.currentPrimaryAmmo = Mathf.Min(target.maxPrimaryAmmo, target.currentPrimaryAmmo + ammoChange);
                    int actualReload = target.currentPrimaryAmmo - oldAmmo;
                }
            }
        }

        // ========== 3. 油量处理 ==========
        if (modifiedFuelPercent != 0 && target.consumeFuel)
        {
            int fuelChange = Mathf.RoundToInt(target.maxFuel * Mathf.Abs(modifiedFuelPercent));

            if (modifiedFuelPercent > 0)
            {
                target.fuel = Mathf.Max(0, target.fuel - fuelChange);
            }
            else
            {
                // ✅ 油量也可以超上限（如果canOverMaxHp为true）
                if (canOverMaxHp)
                {
                    target.fuel += fuelChange;
                }
                else
                {
                    int oldFuel = target.fuel;
                    target.fuel = Mathf.Min(target.maxFuel, target.fuel + fuelChange);
                    int actualRefuel = target.fuel - oldFuel;
                }
            }
        }

        // ========== 4. 死亡检查 ==========
        if (target.health <= 0 && !target.TryFlee())
        {
            var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
            gm?.RemovePiece(target);
            target.QueueFree();
        }
    }

    // ✅ 修改：对兵器应用改良效果，支持CanOverMaxHp，并修复死机
    private void ApplyModifiedEffectToWeapon(Weapon targetWeapon)
    {
        // ✅ 关键修复：提前检查
        if (targetWeapon == null || !IsInstanceValid(targetWeapon)) return;

        int healthChange = Mathf.RoundToInt(targetWeapon.maxHealth * Mathf.Abs(modifiedHealthPercent));
        bool willBeDestroyed = false;

        if (modifiedHealthPercent > 0)
        {
            // 正数：扣血
            if (!CanDestroy)
            {
                targetWeapon.health = Mathf.Max(1, targetWeapon.health - healthChange);
            }
            else
            {
                targetWeapon.health -= healthChange;
                if (targetWeapon.health <= 0)
                {
                    willBeDestroyed = true;
                    targetWeapon.health = 0;
                }
            }
        }
        else if (modifiedHealthPercent < 0)
        {
            // 负数：回血（略，不变）
            int oldHealth = targetWeapon.health;

            if (canOverMaxHp)
            {
                targetWeapon.health += healthChange;
            }
            else
            {
                targetWeapon.health = Mathf.Min(targetWeapon.maxHealth, targetWeapon.health + healthChange);
                int actualHeal = targetWeapon.health - oldHealth;
            }

            ShowHealEffect(targetWeapon);
        }

        targetWeapon.UpdateHpLabel();

        // ✅ 关键修复：如果需要销毁，先清理引用再延迟销毁
        if (willBeDestroyed)
        {

            // 立即从格子移除引用
            if (targetWeapon.grid != null)
            {
                targetWeapon.grid.weapons.Remove(targetWeapon);
                if (targetWeapon.grid.weapon == targetWeapon)
                    targetWeapon.grid.weapon = null;
                targetWeapon.grid = null;
            }

            CallDeferred(nameof(DeferredRemoveWeapon), targetWeapon);
        }
    }


    private void ShowHealEffect(Node2D target)
    {
        var healEffect = new HealEffect();
        healEffect.Position = target.GlobalPosition;
        GetTree().CurrentScene.AddChild(healEffect);
    }

    public void DoWait()
    {
        hasActed = true;
        SetVisualDark();

        // ✅ 关键：清除 GameManager 的选中状态
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm != null)
        {
            gm.selectedWeapon = null;
        }

        // ✅ 关键：清除攻击范围显示
        var gmGrid = GetTree().GetFirstNodeInGroup("grid_manager") as GridManager;
        gmGrid?.ClearWeaponRange();

        var menu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
        menu?.Hide();
    }

    private void PlayCannonEffect(Infantry target)
    {
        ShakeCamera(amplitude: 6f, times: 3);

        var flash = new CannonFlash();
        flash.Position = GlobalPosition;
        GetTree().CurrentScene.AddChild(flash);

        if (target != null && IsInstanceValid(target))
        {
            var hit = new HitEffect();
            hit.Position = target.GlobalPosition;
            GetTree().CurrentScene.AddChild(hit);
        }
    }

    private void ShakeCamera(float amplitude, int times)
    {
        var cam = GetViewport().GetCamera2D();
        if (cam == null) return;
        var tween = cam.CreateTween().SetParallel(false);
        for (int i = 0; i < times; i++)
        {
            var rnd = new Vector2(GD.Randf() * amplitude - amplitude * 0.5f,
                                  GD.Randf() * amplitude - amplitude * 0.5f);
            tween.TweenProperty(cam, "position", cam.Position + rnd, 0.05f);
        }
        tween.TweenProperty(cam, "position", cam.Position, 0.05f);
    }

    private void AttackWeapon(Weapon target)
    {
        if (target == null) return;
        int damage = Mathf.Max(1, Mathf.RoundToInt(target.maxHealth * fixedDamagePercent));
        target.TakeDamage(damage);
        hasActed = true;
        remainingAttacks = 0;
    }
}
