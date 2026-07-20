// WeaponManager.cs - 专用兵器管理器
using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class WeaponManager : Node
{
    [Export] public GridManager gridManager;
    [Export] public Node weaponsNode;  // 存放兵器的父节点

    public List<Weapon> AllWeapons { get; private set; } = new List<Weapon>();

    public override void _Ready()
    {
        if (gridManager == null)
            gridManager = GetTree().GetFirstNodeInGroup("grid_manager") as GridManager;

        // 获取 Weapons 节点
        weaponsNode = GetNodeOrNull("Weapons");
        if (weaponsNode == null)
        {
            return;
        }

        // 初始化已有兵器
        RefreshWeaponList();
    }

    // 刷新兵器列表（从场景中收集）
    public void RefreshWeaponList()
    {
        AllWeapons.Clear();

        foreach (var child in weaponsNode.GetChildren())
        {
            // ✅ 跳过已销毁的对象
            if (!IsInstanceValid(child)) continue;
            
            if (child is Weapon weapon)
            {
                if (!IsInstanceValid(weapon)) continue;
                
                AllWeapons.Add(weapon);
                BindWeaponToGrid(weapon);

                // 设置点击回调
                weapon.OnClickWeapon = OnSelectWeapon;
            }
        }

    }

    // 绑定兵器到格子（支持多格）
    public bool BindWeaponToGrid(Weapon weapon, bool forceRebind = false)
    {
        if (weapon == null || !IsInstanceValid(weapon)) return false;

        // 如果已有格子且不是强制重新绑定，跳过
        if (weapon.grid != null && !forceRebind && !weapon.isMultiTile) return true;

        // 计算锚点格子坐标
        Vector2I gridPos = WorldToGrid(weapon.Position);

        // 检查是否在地图范围内
        if (!IsValidGrid(gridPos))
        {
            return false;
        }

        var targetGrid = gridManager.map[gridPos.X, gridPos.Y];
        if (targetGrid == null)
        {
            return false;
        }

        // 多格兵器：检查所有被占据格子是否在地图内且未被其他兵器占据
        if (weapon.isMultiTile)
        {
            var occupiedIndices = weapon.GetOccupiedIndices(gridPos);
            foreach (var idx in occupiedIndices)
            {
                if (!IsValidGrid(idx)) return false;
                var g = gridManager.map[idx.X, idx.Y];
                if (g == null) return false;
                // 检查是否已有其他兵器占据（排除自己已在的格子）
                if (g.weapon != null && g.weapon != weapon)
                {
                    // ✅ 清理无效引用，防止已销毁兵器残留导致无法放置
                    if (!IsInstanceValid(g.weapon))
                    {
                        g.weapon = null;
                        g.weapons.RemoveAll(w => w != null && !IsInstanceValid(w));
                    }
                    else if (g.weapon.isMultiTile && g.weapon != weapon)
                    {
                        return false;
                    }
                    else if (!g.weapon.isMultiTile)
                    {
                        return false;
                    }
                }
                // 检查是否被其他多格兵器占据（通过occupiedGrids检查）
                // ✅ 先清理 AllWeapons 中的无效引用
                AllWeapons.RemoveAll(w => w == null || !IsInstanceValid(w));
                foreach (var otherW in AllWeapons)
                {
                    if (otherW != weapon && IsInstanceValid(otherW) && otherW.isMultiTile && otherW.occupiedGrids.Contains(g))
                        return false;
                }
            }
        }
        else
        {
            // 单格兵器原有检查
            if (targetGrid.weapon != null && targetGrid.weapon != weapon)
            {
                return false;
            }
        }

        // 从旧格子移除（清除所有occupiedGrids）
        if (weapon.isMultiTile)
        {
            foreach (var og in weapon.occupiedGrids.ToList())
            {
                og.weapons.Remove(weapon);
                if (og.weapon == weapon) og.weapon = null;
            }
            weapon.occupiedGrids.Clear();
        }
        else
        {
            if (weapon.grid != null)
            {
                weapon.grid.weapons.Remove(weapon);
                if (weapon.grid.weapon == weapon)
                    weapon.grid.weapon = null;
            }
        }

        // 绑定到新格子
        weapon.grid = targetGrid;
        if (!targetGrid.weapons.Contains(weapon))
        {
            targetGrid.weapons.Add(weapon);
        }
        targetGrid.weapon = weapon;

        // 多格兵器：绑定所有占据格子
        if (weapon.isMultiTile)
        {
            var occupiedIndices = weapon.GetOccupiedIndices(gridPos);
            foreach (var idx in occupiedIndices)
            {
                var g = gridManager.map[idx.X, idx.Y];
                if (g != null && g != targetGrid)
                {
                    if (!g.weapons.Contains(weapon)) g.weapons.Add(weapon);
                    // 被占据的格子如果没有自己的weapon，就设为这个多格兵器
                    if (g.weapon == null) g.weapon = weapon;
                    weapon.occupiedGrids.Add(g);
                }
            }
            // 锚点也要加入occupiedGrids
            if (!weapon.occupiedGrids.Contains(targetGrid))
                weapon.occupiedGrids.Add(targetGrid);
        }

        // 对齐位置到格子中心（锚点）
        weapon.Position = targetGrid.Position;

        return true;
    }

    // 动态创建兵器
    public Weapon SpawnWeapon(PackedScene weaponScene, Vector2I gridPos, string team = "Player1")
    {
        if (!IsValidGrid(gridPos))
        {
            return null;
        }

        var grid = gridManager.map[gridPos.X, gridPos.Y];
        if (grid == null)
        {
            return null;
        }

        // 检查格子是否已有兵器
        if (grid.weapon != null)
        {
            return null;
        }

        var weapon = weaponScene.Instantiate<Weapon>();
        if (weapon == null)
        {
            return null;
        }

        weapon.Position = grid.Position;
        weapon.team = team;
        weapon.Name = $"{team}_Weapon_{AllWeapons.Count}";

        weaponsNode.AddChild(weapon);

        if (BindWeaponToGrid(weapon))
        {
            AllWeapons.Add(weapon);
            weapon.OnClickWeapon = OnSelectWeapon;

            return weapon;
        }
        else
        {
            weapon.QueueFree();
            return null;
        }
    }

    // 移除兵器（支持多格）
    public void RemoveWeapon(Weapon weapon)
    {
        if (weapon == null) return;

        // 多格兵器：从所有占据格子清除
        if (weapon.isMultiTile)
        {
            foreach (var og in weapon.occupiedGrids.ToList())
            {
                if (og != null && IsInstanceValid(og))
                {
                    og.weapons.Remove(weapon);
                    if (og.weapon == weapon)
                        og.weapon = og.weapons.Count > 0 ? og.weapons[0] : null;
                }
            }
            weapon.occupiedGrids.Clear();
        }
        else
        {
            // ✅ 特殊地形自动切换
            if (weapon.grid != null && (weapon.grid.gridType == GridType.PIPESEAM || weapon.grid.gridType == GridType.METEORITE))
            {
                string terrainName = weapon.grid.gridType == GridType.PIPESEAM ? "PIPESEAM" : "METEORITE";
                var targetGrid = weapon.grid;

                targetGrid.weapons.Remove(weapon);
                if (targetGrid.weapon == weapon)
                    targetGrid.weapon = null;

                targetGrid.gridType = GridType.BROKENPIPE;

                var sprite = targetGrid.GetNodeOrNull<Sprite2D>("Sprite2D");
                if (sprite != null)
                {
                    var tween = CreateTween();
                    tween.TweenProperty(sprite, "modulate", new Color(0.3f, 0.3f, 0.3f), 0.5f)
                         .SetTrans(Tween.TransitionType.Sine)
                         .SetEase(Tween.EaseType.InOut);
                }
            }
            else
            {
                // 普通地形：只清理引用
                if (weapon.grid != null)
                {
                    weapon.grid.weapons.Remove(weapon);
                    if (weapon.grid.weapon == weapon)
                    {
                        weapon.grid.weapon = weapon.grid.weapons.Count > 0 ? weapon.grid.weapons[0] : null;
                    }
                }
            }
        }

        AllWeapons.Remove(weapon);

        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm != null)
        {
            if (gm.selectedWeapon == weapon)
                gm.selectedWeapon = null;
            gm.weapons.Remove(weapon); 
        }

        // 节点销毁
        weapon.QueueFree();
    }
    public Weapon GetWeaponAt(Vector2I gridPos)
    {
        if (!IsValidGrid(gridPos)) return null;
        var grid = gridManager.map[gridPos.X, gridPos.Y];
        return grid?.weapon;
    }

    // 获取某队伍的所有兵器
    public List<Weapon> GetWeaponsByTeam(string team)
    {
        return AllWeapons.Where(w => w.team == team).ToList();
    }

    // 获取某格子的所有兵器（包括主兵器和列表中的）
    public List<Weapon> GetWeaponsAtGrid(Vector2I gridPos)
    {
        if (!IsValidGrid(gridPos)) return new List<Weapon>();
        var grid = gridManager.map[gridPos.X, gridPos.Y];
        return grid?.weapons.ToList() ?? new List<Weapon>();
    }

    // 世界坐标转格子坐标
    public Vector2I WorldToGrid(Vector2 worldPos)
    {
        int x = (int)(worldPos.X - gridManager.startPos.X) / gridManager.gridSize.X;
        int y = (int)(worldPos.Y - gridManager.startPos.Y) / gridManager.gridSize.Y;
        return new Vector2I(x, y);
    }

    // 格子坐标转世界坐标
    public Vector2 GridToWorld(Vector2I gridPos)
    {
        return new Vector2(
            gridPos.X * gridManager.gridSize.X + gridManager.startPos.X,
            gridPos.Y * gridManager.gridSize.Y + gridManager.startPos.Y
        );
    }

    // 检查格子是否有效
    public bool IsValidGrid(Vector2I gridPos)
    {
        return gridPos.X >= 0 && gridPos.X < gridManager.searchRange.X &&
               gridPos.Y >= 0 && gridPos.Y < gridManager.searchRange.Y;
    }

    // 选择兵器回调
    private void OnSelectWeapon(Weapon weapon)
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm != null)
        {
            gm.OnSelectWeapon(weapon);
        }
    }

public void OnTurnStartForNewTurn()
{
    foreach (var w in AllWeapons)
    {
        if (!IsInstanceValid(w)) continue;
        w.OnTurnStart(); // 只有新回合才调用
    }
}

// 保留原有方法用于阶段切换（不触发冷却）
public void OnPhaseStart()
{
    foreach (var w in AllWeapons)
    {
        if (!IsInstanceValid(w)) continue;
        // 阶段切换时只重置 hasActed，不处理冷却
        w.hasActed = false;
        w.remainingAttacks = w.maxAttacksPerTurn;
    }
}


    // 回合结束处理
    public void OnTurnEnd()
    {
        foreach (var weapon in AllWeapons)
        {
            if (IsInstanceValid(weapon))
            {
                // ✅ 关键：调用每个兵器的OnTurnEnd（BlackCannon会覆盖此方法处理存次数）
                weapon.OnTurnEnd();
            }
        }
    }
}
