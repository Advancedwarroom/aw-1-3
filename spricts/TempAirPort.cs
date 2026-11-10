using Godot;

// ✅ 临时机场（DoR 原作 Temporary Airport）：由工程车 Rig 搭建；可维修/补给空军、可占领；不能生产、无收入。
//    外观沿用机场贴图。收入在 .tscn 检查器里设为 0（检查器优先）。
public partial class TempAirPort : AirPort
{
    // Temporary Airport uses its own red base frames, slightly different from the standard airport.
    protected override float GetBaseArtHue() => 358.0f / 360.0f;

    protected override string[] DefaultProducibleUnits() => System.Array.Empty<string>();
}
