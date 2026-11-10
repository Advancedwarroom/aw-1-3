using Godot;

// ✅ 临时港口（DoR 原作 Temporary Port）：由工程车 Rig 搭建在海滩；可维修/补给舰船、可占领；不能生产、无收入。
//    外观沿用港口贴图。收入在 .tscn 检查器里设为 0（检查器优先）。
public partial class TempPort : Port
{
    // 临时港口底图是青蓝色源图，不是标准红色设施模板。
    // 色相旋转必须从它自己的源色相计算，否则 P-1 会被旋到黄绿色而不是紫色。
    protected override float GetBaseArtHue() => 0.542f;

    protected override string[] DefaultProducibleUnits() => System.Array.Empty<string>();
}
