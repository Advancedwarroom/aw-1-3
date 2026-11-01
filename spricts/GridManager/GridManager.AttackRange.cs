// GridManager.cs - 修改 HandleGridClick 和 SetupMoveRangeCallbacks 方法
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

// GridManager · AttackRange（由 GridManager.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class GridManager
{

    public void ShowWeaponAttackRange(Weapon weapon)
    {
        if (weapon == null || weapon.hasActed) return;
        
        CloseRange();
        HideAttackRange();
        ClearWeaponRange();
        
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm != null) gm.isSelectingAttackTarget = true;
        
        weapon.ShowAttackRange();
    }

    public void ClearWeaponRange()
    {
        Grids.IsForceActionMode = false;
        foreach (var grid in grids)
        {
            if (grid == null || !IsInstanceValid(grid)) continue;

            // ✅ 修复：兵器回调多为闭包（捕获 g/gm），Target 不是 Weapon，下方判定清不掉 → 取消后残留，
            //    之后点到这些格子会意外触发兵器。凡兵器标记过的格子一律清回调并恢复单位可点击
            if (grid.IsInWeaponRange)
            {
                grid.OnClickGrid = null;
                OverrideUnitInput(grid, false);
            }
            grid.IsInWeaponRange = false;
            
            if (!moveRange.Contains(grid))
            {
                grid.pathIcon?.Hide();
            }
            
            if (!attackRange.Contains(grid))
            {
                grid.attackRangeIcon?.Hide();
            }
            
            if (grid.OnClickGrid?.Target is Weapon) 
            {
                grid.OnClickGrid = null;
            }
        }
    }
    public bool IsShowingAttackRange => attackRange.Count > 0;
    
    public void ShowMinMaxAttackRange(Infantry unit)
    {
        if (unit.attackType == AttackType.NoAttack) return;
        if (unit.hasPrimaryWeapon && !unit.CanUsePrimaryWeapon()) return;
        if (!unit.canAttackAfterMoving && unit.state == UnitState.Moved)
        {
            return;
        }
        
        CloseRange();
        HideAttackRange();
        
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm != null) gm.isSelectingAttackTarget = true;
        var fog = gm?.fogOfWarManager;
        bool isFogEnabled = fog != null && fog.isFogOfWarEnabled;
        
        attackRange.Clear();
        
        // ✅ CO 模块系统·模块3（攻击范围加成）：射程/最小射程增减同步到范围显示
        int minRange = Mathf.Max(1, unit.minAttackRange + CoModuleSystem.GetMinFireRangeBonus(unit));
        int maxRange = unit.maxAttackRange + CoModuleSystem.GetFireRangeBonus(unit);
        
        var allRangeGrids = FindRange(unit.grid, maxRange, false);
        var blindZoneGrids = new HashSet<Grids>();
        
        blindZoneGrids.Add(unit.grid);
        if (minRange > 1)
        {
            var nearGrids = FindRange(unit.grid, minRange - 1, false);
            foreach (var g in nearGrids) 
                blindZoneGrids.Add(g);
        }
        
        // ✅ 修复：盲区必须先算好再扫描（原顺序会让盲区内目标被高亮可打）
            foreach (var grid in allRangeGrids)
    {
        if (blindZoneGrids.Contains(grid)) continue;
        
        // 多格兵器：只有弱点格子可被攻击
        if (grid.weapon != null && grid.weapon.isMultiTile && !grid.weapon.IsWeakPointGrid(grid)) continue;
        
        bool hasEnemyTarget = grid.HasAttackableEnemyInfantry(unit, grids) || grid.HasEnemyWeapon(unit.team);
        if (hasEnemyTarget)
        {
            if (isFogEnabled && !fog.IsGridVisible(grid)) continue;
            attackRange.Add(grid);
            grid.attackRangeIcon?.Show();
            grid.IsInAttackRangeMode = true;
            
            // ✅ 强制攻击回调
            grid.OnClickGrid = to => OnAttackGridClicked(unit, to);
            OverrideUnitInput(grid, true); // 禁用单位选择
            
            grid.OnMouseEnteredAttackGrid = () => ShowDamagePreview(unit, grid);
            grid.OnMouseExitedAttackGrid = () => HideDamagePreview();
        }
    }
        foreach (var grid in allRangeGrids)
        {
            if (blindZoneGrids.Contains(grid)) continue;
            if (grid.weapon != null && TeamHelper.IsEnemyForAttacker(unit.team, grid.weapon.team))
            {
                // 多格兵器：只有弱点格子显示为可攻击
                if (grid.weapon.isMultiTile && !grid.weapon.IsWeakPointGrid(grid))
                    continue;
                if (isFogEnabled && !fog.IsGridVisible(grid)) continue;
                attackRange.Add(grid);
                grid.attackRangeIcon?.Show();
                grid.IsInAttackRangeMode = true;
                grid.OnClickGrid = to => OnAttackGridClicked(unit, to);
                grid.OnMouseEnteredAttackGrid = () => ShowDamagePreview(unit, grid);
                grid.OnMouseExitedAttackGrid = () => HideDamagePreview();
                continue;
            }
            if (grid.weapon != null && TeamHelper.IsEnemyForAttacker(unit.team, grid.weapon.team) && !grid.weapon.hasActed)
            {
                // 多格兵器：只有弱点格子显示为可攻击
                if (grid.weapon.isMultiTile && !grid.weapon.IsWeakPointGrid(grid))
                    continue;
                if (isFogEnabled && !fog.IsGridVisible(grid)) continue;
                attackRange.Add(grid);
                grid.attackRangeIcon?.Show();
                grid.IsInAttackRangeMode = true;
                var area2D = grid.GetNodeOrNull<Area2D>("Area2D");
                if (area2D != null) { area2D.ZIndex = 10; area2D.InputPickable = true; }

                grid.OnClickGrid = to => OnAttackGridClicked(unit, to);
                grid.OnMouseEnteredAttackGrid = () => ShowDamagePreview(unit, grid);
                grid.OnMouseExitedAttackGrid = () => HideDamagePreview();
            }
            
            bool hasEnemyTarget = grid.HasAttackableEnemyInfantry(unit, grids) || grid.HasEnemyWeapon(unit.team);
            if (hasEnemyTarget)
            {
                // 多格兵器：只有弱点格子显示为可攻击
                if (grid.weapon != null && grid.weapon.isMultiTile && !grid.weapon.IsWeakPointGrid(grid))
                    continue;
                if (isFogEnabled && !fog.IsGridVisible(grid)) continue;
                attackRange.Add(grid);
                grid.attackRangeIcon?.Show();
                grid.IsInAttackRangeMode = true;
                var area2D = grid.GetNodeOrNull<Area2D>("Area2D");
                if (area2D != null)
                {
                    area2D.ZIndex = 10;
                    area2D.InputPickable = true;
                }
                
                grid.OnClickGrid = to => OnAttackGridClicked(unit, to);
                grid.OnMouseEnteredAttackGrid = () => ShowDamagePreview(unit, grid);
                grid.OnMouseExitedAttackGrid = () => HideDamagePreview();
            }
        }
        
        if (attackRange.Count == 0)
        {
            unit.actionMenu?.Hide();
            if (unit.state == UnitState.Moved && unit.originalGrid != null)
                gm?.RollbackMove();
        }
    }

    public void ShowAttackRange(Infantry unit)
    {   
        Grids.IsForceActionMode = true;
        if (unit.attackType == AttackType.NoAttack) return;
        var terrainEditor = GetTree().GetFirstNodeInGroup("terrain_editor") as TerrainEditor;
    if (terrainEditor != null && terrainEditor.ShouldBlockUnitOperations())
    {
        return;
    }
        CloseRange();
        HideAttackRange();
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm != null) gm.isSelectingAttackTarget = true;
        var fog = gm?.fogOfWarManager;
        bool isFogEnabled = fog != null && fog.isFogOfWarEnabled;

        attackRange.Clear();
        
        if (unit.useMinMaxAttackRange)
        {
            ShowMinMaxAttackRange(unit);
            return;
        }
        
        if (unit.attackType == AttackType.NoAttack) return;
        var neighbors = GetAttackNeighbours(unit.grid);
        
        // ✅ 禁用所有单位选择，防止误选己方未行动单位
        foreach (var grid in grids)
        {
            OverrideUnitInput(grid, true);
        }
        
        foreach (var neighbor in neighbors)
        {
            if (neighbor.weapon != null && TeamHelper.IsEnemyForAttacker(unit.team, neighbor.weapon.team) && !neighbor.weapon.hasActed
                && (!neighbor.weapon.isMultiTile || neighbor.weapon.IsWeakPointGrid(neighbor)))
            {
                if (isFogEnabled && fog != null && !fog.IsGridVisible(neighbor)) continue;
                attackRange.Add(neighbor);
                neighbor.attackRangeIcon?.Show();
                neighbor.IsInAttackRangeMode = true;
                var area2D = neighbor.GetNodeOrNull<Area2D>("Area2D");
                if (area2D != null) { area2D.ZIndex = 10; area2D.InputPickable = true; }

                neighbor.OnClickGrid = to => OnAttackGridClicked(unit, to);
                neighbor.OnMouseEnteredAttackGrid = () => ShowDamagePreview(unit, neighbor);
                neighbor.OnMouseExitedAttackGrid = () => HideDamagePreview();
            }
            
            if (neighbor.HasAttackableEnemyInfantry(unit, grids))
            {
                if (isFogEnabled && fog != null && !fog.IsGridVisible(neighbor)) continue;
                attackRange.Add(neighbor);
                neighbor.attackRangeIcon?.Show();
                neighbor.IsInAttackRangeMode = true;
                var area2D = neighbor.GetNodeOrNull<Area2D>("Area2D");
                if (area2D != null)
                {
                    area2D.ZIndex = 10;
                    area2D.InputPickable = true;
                }
                
                neighbor.OnClickGrid = to => OnAttackGridClicked(unit, to);
                neighbor.OnMouseEnteredAttackGrid = () => ShowDamagePreview(unit, neighbor);
                neighbor.OnMouseExitedAttackGrid = () => HideDamagePreview();
            }
        }

        if (unit.overlapType == UnitOverlapType.Overlapping && unit.grid.HasAttackableEnemyInfantry(unit, grids))
        {
            if (isFogEnabled && fog != null && !fog.IsGridVisible(unit.grid)) { /* 不添加 */ }
            else
            {
                attackRange.Add(unit.grid);
                unit.grid.attackRangeIcon?.Show();
                unit.grid.OnClickGrid = to => OnAttackGridClicked(unit, to);
                unit.grid.OnMouseEnteredAttackGrid = () => ShowDamagePreview(unit, unit.grid);
                unit.grid.OnMouseExitedAttackGrid = () => HideDamagePreview();
            }
        }

        // ✅ 攻击范围外点击回退（包括所有格子）
        foreach (var grid in grids)
        {
            var capturedUnit = unit;
            
            if (!attackRange.Contains(grid))
            {
                // ✅ 范围外格子点击触发回退
                grid.OnClickEmpty = () => HandleAttackRangeOutsideClick(unit);
            }
        }

        if (attackRange.Count == 0)
        {
            unit.actionMenu?.Hide();
            gm?.RollbackMove();
        }
    }
    
    // ✅ 新增：攻击范围外点击处理
    private void HandleAttackRangeOutsideClick(Infantry unit)
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        
        bool shouldRollback = gm?.selectedInfantry != null && 
                              gm.selectedInfantry.state == UnitState.Moved && 
                              gm.selectedInfantry.originalGrid != null;

        if (shouldRollback)
        {
            gm.RollbackMove();
            HideAttackRange();
        }
        else
        {
            HideDamagePreview();
            HideAttackRange();
            CloseRange();
            unit.actionMenu?.Hide();
            gm?.ClearSelectedInfantry();
        }
    }
   public void OverrideUnitInput(Grids grid, bool overrideInput)
{
    if (grid == null) return;
    
    foreach (var u in grid.infantries)
    {
        if (u == null || !IsInstanceValid(u)) continue;
        
        var area2D = u.GetNodeOrNull<Area2D>("Area2D");
        if (area2D != null)
        {
            area2D.InputPickable = !overrideInput; // 覆盖时禁用，恢复时启用
        }
    }
    
    // 同样处理兵器
    if (grid.weapon != null && IsInstanceValid(grid.weapon))
    {
        var area2D = grid.weapon.GetNodeOrNull<Area2D>("Area2D");
        if (area2D != null)
        {
            area2D.InputPickable = !overrideInput;
        }
    }
}
 

    public void OnAttackGridClicked(Infantry attacker, Grids to)
    {
        if (to == attacker.grid && to.infantry == attacker)
        {
            var enemies = to.GetEnemyInfantries(attacker);
            if (enemies.Count == 0) return;
        }
        
        Weapon targetWeapon = null;
        // 多格兵器兜底：非弱点部位不可作为攻击目标
        if (to.weapon != null && TeamHelper.IsEnemyForAttacker(attacker.team, to.weapon.team)
            && (!to.weapon.isMultiTile || to.weapon.IsWeakPointGrid(to)))
            targetWeapon = to.weapon;

        if (targetWeapon != null)
        {
            attacker.AttackTarget(targetWeapon); 
        }
        else
        {
            Infantry target = null;
            if (to == attacker.grid)
            {
                var enemies = to.GetEnemyInfantries(attacker);
                if (enemies.Count > 0) target = enemies[0];
            }
            else
            {
                var enemies = to.GetEnemyInfantries(attacker);
                if (enemies.Count > 0) target = enemies[0];
            }

            if (target == null)
            {
                HideAttackRange();
                attacker.actionMenu.Hide();
                var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
                gm?.RollbackMove();
                return;
            }

            attacker.originalGrid = null;
            
            var gameManager = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
            gameManager?.OnAttack(attacker, target);
        }
    }

    
    // ✅ 新增：支持 attackRange 的扩展射程（非minMax模式）
    public void ShowExtendedAttackRange(Infantry unit)
    {
        if (unit.attackType == AttackType.NoAttack) return;
        if (unit.hasPrimaryWeapon && !unit.CanUsePrimaryWeapon()) return;
        if (!unit.canAttackAfterMoving && unit.state == UnitState.Moved)
        {
            return;
        }

        CloseRange();
        HideAttackRange();

        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm != null) gm.isSelectingAttackTarget = true;
        var fog = gm?.fogOfWarManager;
        bool isFogEnabled = fog != null && fog.isFogOfWarEnabled;

        attackRange.Clear();

        int range = unit.attackRange;
        var allRangeGrids = FindRange(unit.grid, range, false);

        foreach (var grid in allRangeGrids)
        {
            if (grid == null || grid == unit.grid) continue;

            // 检查敌方兵器
            if (grid.weapon != null && TeamHelper.IsEnemyForAttacker(unit.team, grid.weapon.team) && !grid.weapon.hasActed)
            {
                // 多格兵器：只有弱点格子显示为可攻击
                if (grid.weapon.isMultiTile && !grid.weapon.IsWeakPointGrid(grid))
                    continue;
                if (isFogEnabled && fog != null && !fog.IsGridVisible(grid)) continue;
                attackRange.Add(grid);
                grid.attackRangeIcon?.Show();
                grid.IsInAttackRangeMode = true;
                var area2D = grid.GetNodeOrNull<Area2D>("Area2D");
                if (area2D != null) { area2D.ZIndex = 10; area2D.InputPickable = true; }
                grid.OnClickGrid = to => OnAttackGridClicked(unit, to);
                grid.OnMouseEnteredAttackGrid = () => ShowDamagePreview(unit, grid);
                grid.OnMouseExitedAttackGrid = () => HideDamagePreview();
                continue;
            }

            // 检查敌方单位
            if (grid.HasEnemyInfantry(unit))
            {
                if (isFogEnabled && fog != null && !fog.IsGridVisible(grid)) continue;
                attackRange.Add(grid);
                grid.attackRangeIcon?.Show();
                grid.IsInAttackRangeMode = true;
                var area2D = grid.GetNodeOrNull<Area2D>("Area2D");
                if (area2D != null)
                {
                    area2D.ZIndex = 10;
                    area2D.InputPickable = true;
                }
                grid.OnClickGrid = to => OnAttackGridClicked(unit, to);
                grid.OnMouseEnteredAttackGrid = () => ShowDamagePreview(unit, grid);
                grid.OnMouseExitedAttackGrid = () => HideDamagePreview();
            }
        }

        // Overlapping 单位检查同格敌人
        if (unit.overlapType == UnitOverlapType.Overlapping && unit.grid.HasEnemyInfantry(unit))
        {
            if (isFogEnabled && fog != null && !fog.IsGridVisible(unit.grid)) { /* 不添加 */ }
            else
            {
                attackRange.Add(unit.grid);
                unit.grid.attackRangeIcon?.Show();
                unit.grid.OnClickGrid = to => OnAttackGridClicked(unit, to);
                unit.grid.OnMouseEnteredAttackGrid = () => ShowDamagePreview(unit, unit.grid);
                unit.grid.OnMouseExitedAttackGrid = () => HideDamagePreview();
            }
        }

        // 空地点击处理
        foreach (var grid in grids)
        {
            var capturedUnit = unit;
            grid.OnClickEmpty = () => 
            {
                var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
                bool shouldRollback = gm?.selectedInfantry != null && 
                                      gm.selectedInfantry.state == UnitState.Moved && 
                                      gm.selectedInfantry.originalGrid != null;
                if (shouldRollback)
                {
                    gm.RollbackMove();
                    HideAttackRange();
                }
                else
                {
                    HideDamagePreview();
                    HideAttackRange();
                    CloseRange();
                    capturedUnit.actionMenu?.Hide();
                    gm?.ClearSelectedInfantry();
                }
            };
        }

        if (attackRange.Count == 0)
        {
            unit.actionMenu?.Hide();
            gm?.RollbackMove();
        }
    }

public void HideAttackRange()
{
    Grids.IsForceActionMode = false;
    HideDamagePreview();

    foreach (var g in attackRange)
    {
        if (g != null && IsInstanceValid(g))
        {
            g.IsInAttackRangeMode = false;
            
            // ✅ 恢复单位输入
            OverrideUnitInput(g, false);
            
            if (g.attackRangeIcon != null && IsInstanceValid(g.attackRangeIcon))
                g.attackRangeIcon.Hide();
            g.OnClickGrid = null;
            g.OnClickEmpty = null;
            g.OnMouseEnteredAttackGrid = null;
            g.OnMouseExitedAttackGrid = null;
        }
    }
    attackRange.Clear();
    HideExplosionRange(); // ✅ 关闭攻击范围时也清除爆炸预览

        
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm != null) gm.isSelectingAttackTarget = false;
        
        foreach (var grid in grids)
            grid.OnClickEmpty = null;
    }

    private void ShowDamagePreview(Infantry attacker, Grids targetGrid)
    {
        if (damagePreviewLabel == null) return;
        
        var target = targetGrid.GetEnemyInfantries(attacker)
            .OrderBy(u => u.health)
            .FirstOrDefault();
        
        if (target == null) return;
        
        var (damage, attackInfo) = attacker.CalculateDamagePreview(target);
        var (counterDamage, counterInfo) = target.CalculateCounterPreview(attacker);
        
        bool targetWillDie = target.health - damage <= 0;
        bool attackerWillDie = !targetWillDie && attacker.health - counterDamage <= 0;
        
        string text = "";
        
        if (targetWillDie)
            text += $"💀 {damage} (击杀!)";
        else
            text += $"⚔️ {damage}";
        
        if (!targetWillDie && counterDamage > 0)
        {
            if (attackerWillDie)
                text += $"\n💀 反击{counterDamage} (同归于尽!)";
            else
                text += $"\n🛡️ 反击{counterDamage}";
        }
        else if (!targetWillDie && counterDamage == 0)
            text += TranslateGroup.T("\n🛡️ 无反击");
        
        damagePreviewLabel.Text = text;
        
        damagePreviewLabel.TopLevel = true;
        damagePreviewLabel.GlobalPosition = targetGrid.GlobalPosition + new Godot.Vector2(-30, -60);
        
        damagePreviewLabel.Show();
        damagePreviewLabel.QueueRedraw();
    }

    private void HideDamagePreview()
    {
        damagePreviewLabel?.Hide();
    }
}
