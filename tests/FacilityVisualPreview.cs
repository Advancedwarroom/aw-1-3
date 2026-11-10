using Godot;
using System;
using System.Collections.Generic;

public partial class FacilityVisualPreview : Node2D
{
    private static readonly (string Name, string Path)[] Facilities =
    {
        ("City", "res://Prefabs/City.tscn"),
        ("Base", "res://Prefabs/Base.tscn"),
        ("AirPort", "res://Prefabs/AirPort.tscn"),
        ("Port", "res://Prefabs/Port.tscn"),
        ("Lab", "res://Prefabs/Lab.tscn"),
        ("WatchTower", "res://Prefabs/Watchtower.tscn"),
        ("ComTower", "res://Prefabs/ComTower.tscn"),
        ("HQ", "res://Prefabs/HQ.tscn"),
        ("TempAirport", "res://Prefabs/TempAirPort.tscn"),
        ("TempPort", "res://Prefabs/TempPort.tscn"),
        ("RelayJunction", "res://Prefabs/RelayJunction.tscn")
    };

    public override async void _Ready()
    {
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        TeamHelper.InitializeDefaults();

        var background = new ColorRect
        {
            Position = Vector2.Zero,
            Size = new Vector2(1280, 720),
            Color = new Color(0.12f, 0.13f, 0.16f),
            ZIndex = -100
        };
        AddChild(background);

        var camera = new Camera2D
        {
            Position = new Vector2(640, 360),
            Enabled = true,
            Zoom = Vector2.One
        };
        AddChild(camera);
        camera.MakeCurrent();

        AddRowLabel("P-1  |  Purple", new Vector2(32, 34));
        AddRowLabel("P2  |  Blue", new Vector2(32, 274));

        var gridScene = GD.Load<PackedScene>("res://Prefabs/grid.tscn");
        if (gridScene == null)
        {
            GD.PushError("Facility preview: cannot load Prefabs/grid.tscn");
            GetTree().Quit(2);
            return;
        }

        string[] teams = { TeamHelper.Player, TeamHelper.Player2 };
        float[] rowY = { 100f, 340f };

        for (int row = 0; row < teams.Length; row++)
        {
            for (int col = 0; col < Facilities.Length; col++)
            {
                float x = 32f + col * 112f;
                float y = rowY[row];

                var cell = new ColorRect
                {
                    Position = new Vector2(x, y),
                    Size = new Vector2(32, 32),
                    Color = new Color(0.24f, 0.25f, 0.29f),
                    ZIndex = -10
                };
                AddChild(cell);

                var grid = gridScene.Instantiate<Grids>();
                grid.Name = $"PreviewGrid_{row}_{col}";
                grid.Position = new Vector2(x, y);
                AddChild(grid);

                var packed = GD.Load<PackedScene>(Facilities[col].Path);
                if (packed == null)
                {
                    GD.PushError($"Facility preview: missing scene {Facilities[col].Path}");
                    GetTree().Quit(3);
                    return;
                }

                var facility = packed.Instantiate<Facility>();
                if (facility == null)
                {
                    GD.PushError($"Facility preview: {Facilities[col].Path} root is not Facility");
                    GetTree().Quit(4);
                    return;
                }

                facility.facilityTeam = teams[row];
                grid.AddChild(facility);
                grid.city = facility;

                var label = new Label
                {
                    Text = Facilities[col].Name,
                    Position = new Vector2(x - 28, y + 38),
                    Size = new Vector2(88, 24),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Modulate = Colors.White
                };
                AddChild(label);
            }
        }

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        string outputDirectory = ProjectSettings.GlobalizePath("res://build");
        DirAccess.MakeDirRecursiveAbsolute(outputDirectory);
        var image = GetViewport().GetTexture().GetImage();
        Error saveResult = image.SavePng(outputDirectory + "/facility-preview.png");
        if (saveResult != Error.Ok)
        {
            GD.PushError($"Facility preview: failed to save image: {saveResult}");
            GetTree().Quit(5);
            return;
        }

        GD.Print("[FACILITY_PREVIEW] Saved build/facility-preview.png");
        GetTree().Quit();
    }

    private void AddRowLabel(string text, Vector2 position)
    {
        var label = new Label
        {
            Text = text,
            Position = position,
            Size = new Vector2(300, 28),
            Modulate = Colors.White
        };
        AddChild(label);
    }
}
