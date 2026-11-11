using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

// Infantry · Flare（由 Infantry.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class Infantry
{

    // ========== ✅ 通用照明模块（任何单位勾选即可发射照明弹）==========
    [ExportGroup("照明模块")]
    [Export] public bool canIlluminate = false;                     // 是否可以发射照明弹
    [Export] public int maxFlareAmmo = 0;                            // 最大照明弹数
    [Export] public int currentFlareAmmo = 0;                        // 当前照明弹数
    [Export] public int minLaunchRange = 0;                          // 投射最小射程（绿色预览）
    [Export] public int maxLaunchRange = 5;                          // 投射最大射程（绿色预览）
    [Export] public int minIlluminationRange = 0;                    // 照明覆盖最小射程（黄色预览）
    [Export] public int maxIlluminationRange = 2;                    // 照明覆盖最大射程（黄色预览）
    [Export] public int flareDurationTurns = 1;                      // 照明效果持续大回合数
    [Export] public bool canIlluminateAfterMove = false;             // 移动后是否可以照明
    [Export] public bool flareLaunchCanCrossScreens = false;       // 照明弹投射射程是否允许跨屏
    [Export] public bool flareEffectCanCrossScreens = false;       // 照明效果覆盖范围是否允许跨屏
}
