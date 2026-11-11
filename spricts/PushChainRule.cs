using Godot;
using System;

/// <summary>
/// Optional per-link overrides for a push chain. Array index 0 configures the
/// first displaced tile, index 1 the second, and so on. Missing entries inherit
/// the initiating unit's global push target rules.
/// </summary>
[GlobalClass]
public partial class PushChainRule : Resource
{
    [Export(PropertyHint.Enum, "继承全局,敌军,友军,盟军,全部,自定义")]
    public int teamPolicy = 0;

    [Export] public bool targetInfantry = true;
    [Export] public bool targetWeapons = false;
    [Export] public bool targetFacilities = false;

    [Export] public bool targetEnemies = true;
    [Export] public bool targetFriendlies = false;
    [Export] public bool targetAllies = false;

    [Export] public string[] whitelist = Array.Empty<string>();
    [Export] public string[] blacklist = Array.Empty<string>();
}
