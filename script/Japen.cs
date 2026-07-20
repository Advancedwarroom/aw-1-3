using Godot;
using System;
public partial class Japen : Node {
    public override void _Ready() {
        base._Ready();
    }
public int GetRandomInt() {
return new Random().Next();}
    public override void _Process(double delta) {
        base._Process(delta);
    }
}