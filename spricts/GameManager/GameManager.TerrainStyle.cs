using Godot;

// GameManager · 地形风格(CoW) [Export] 配置 —— 挂在 GameManager 上即可在编辑器/GlobalConfig 调整
public partial class GameManager
{
    [ExportGroup("CoW地形风格")]
    [Export] public bool enableCowTerrainStyle = true;   // 总开关: 关闭则地形渲染完全走旧贴图
    [Export] public TerrainStyleManager.Biome terrainBiome = TerrainStyleManager.Biome.Clear;  // 气候: 常规/雪/雨/废土
    [Export] public TerrainStyleManager.Style terrainStyle = TerrainStyleManager.Style.AW2;    // 风格: AW1/SFC/AW2/DS/DoR

    /// <summary>把 Export 配置写入静态管理器并广播(编辑器改配置/读档后调用)</summary>
    public void ApplyTerrainStyleConfig()
    {
        TerrainStyleManager.CurrentBiome = terrainBiome;
        TerrainStyleManager.CurrentStyle = terrainStyle;
    }

    /// <summary>✅ 游戏内全图换风格: 覆盖所有已放地块的烘焙风格(与编辑器画笔语义互不影响)</summary>
    public void ApplyGlobalTerrainStyle(int biome, int style)
    {
        terrainBiome = (TerrainStyleManager.Biome)biome;
        terrainStyle = (TerrainStyleManager.Style)style;
        ApplyTerrainStyleConfig();
        if (gridManager?.map == null) return;
        foreach (var g in gridManager.map)
            g?.RebakeCowStyle(biome, style);
    }
}
