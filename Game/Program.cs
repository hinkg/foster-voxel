using System.Numerics;
using Foster.Framework;

namespace Game;

public class Program
{
    private static void Main(string[] args)
    {
        Config.ParseCommandLineArgs(args);
        Config.ReadFromDisk();
        App.Run<Game>("Foster App", Config.WindowSizeX, Config.WindowSizeY, false, Renderers.OpenGL);
    }
}

class Game : Module
{
    private TimeSpan prevTime;
    private long prevMem;

    public Level level;
    public MenuManager menuManager;

    public int selectedHotbarSlot;

    private bool drawTerrain = true;

    private SpriteFont font;
    private Mesh quad;

    private Material vignetteMaterial;
    private Material backgroundMaterial;

    private float lastSidebarHeight;
    private int sidebarState = 0;

    public bool drawSkyTexture = false;
    public bool drawTerrainTexture = false;
    public bool drawCloudTexture = false;
    public bool drawShadowTexture = false;

    //

    public override void Startup()
    {
        Time.FramerateTarget = TimeSpan.FromSeconds(1.0f / Math.Clamp(Config.MaxFrameRate, 1, 1000));
        Time.FixedStepTarget = TimeSpan.FromSeconds(1.0f / 100.0f);

        App.VSync = Config.DoVerticalSync;

        font = new SpriteFont(Path.Join("Assets/Fonts/CozetteVector.ttf"), 12.0f);
        GUI.Init(ref font);

        vignetteMaterial = MakeMaterial(ShaderInfo[Renderers.OpenGL]["Vignette"]);
        backgroundMaterial = MakeMaterial(ShaderInfo[Renderers.OpenGL]["Background"]);

        menuManager = new();

        level = new();

        Audio.Load();

        {
            quad = new Mesh();

            quad.SetVertices<MeshVertex>([
                new(new(-1.0f, -1.0f, 0.0f), new(0.0f, 1.0f)),
                new(new(+1.0f, -1.0f, 0.0f), new(1.0f, 1.0f)),
                new(new(-1.0f, +1.0f, 0.0f), new(0.0f, 0.0f)),
                new(new(+1.0f, +1.0f, 0.0f), new(1.0f, 0.0f))
            ], MeshVertex.VertexFormat);

            quad.SetIndices([0, 1, 2, 3, 2, 1]);
        }

        if (Environment.GetCommandLineArgs().Contains("-enter"))
        {
            level.CreateSave($"World {Random.Shared.Next() % 10000:0000}");
            var saveList = DiskStorage.ScanForSaves();
            if (saveList.Length > 0) level.LoadSave(saveList[0].path);
        }
    }

    public override void Shutdown()
    {
        level.Exit();
        Config.WriteToDisk();
        Audio.Unload();
    }

    public void ReloadResources()
    {
        try
        {
            var newMaterial = MakeMaterial(ShaderInfo[Renderers.OpenGL]["Vignette"]);
            vignetteMaterial.Clear();
            vignetteMaterial = newMaterial;

            newMaterial = MakeMaterial(ShaderInfo[Renderers.OpenGL]["Background"]);
            backgroundMaterial.Clear();
            backgroundMaterial = newMaterial;

            level.ReloadResources();

            Log.Warn("Reloaded resources");
        }
        catch { }
    }

    public void DrawMenuSidebar()
    {
        if (sidebarState < 1) return;

        static Rect ButtonLine(ref Rect rect) => rect.CutTop(24).GetLeft(160).Inflate(-2);

        var r0 = GUI.Viewport.Inflate(-16);
        var startTop = r0.Top;

        GUI.Panel(r0.GetLeft(180).GetTop(lastSidebarHeight).Inflate(8));

        Entity? player = level.PlayerTarget;

        GUI.TextLine(ref r0, $"fps: {1.0f / (Time.Now.TotalSeconds - prevTime.TotalSeconds):0.00}");
        GUI.TextLine(ref r0, $"mem: {(GC.GetAllocatedBytesForCurrentThread() - prevMem) / 1e6:0.00}MB");
        prevMem = GC.GetAllocatedBytesForCurrentThread();

        GUI.TextLine(ref r0, $"pos: {player?.Position}");
        GUI.TextLine(ref r0, $"vel: {player?.Velocity.ToVector3() * 100:0.00} ({player?.Velocity.ToVector3().XY().Length() * 100:0.00})");
        GUI.TextLine(ref r0, $"rot: {player?.Rotation:0.00}");

        lastSidebarHeight = r0.Top - startTop;
        if (sidebarState < 2) return;

        GUI.TextLine(ref r0, $"draws: {level.ChunkDrawCount:0.00}");
        GUI.TextLine(ref r0, $"tasks: {ThreadPool.PendingWorkItemCount} / {ThreadPool.CompletedWorkItemCount} ({ThreadPool.ThreadCount} thr)");
        GUI.TextLine(ref r0, $"level: {level.UpdateTime.TotalMilliseconds:0.00}ms, {level.FixedTime.TotalMilliseconds:0.00}ms");

        var genTime = (level.GenerationTime / Math.Max(level.GenerationCount, 1)).TotalMilliseconds;
        var meshTime = (level.MeshingTime / Math.Max(level.MeshingCount, 1)).TotalMilliseconds;
        var lightTime = (level.LightingTime / Math.Max(level.LightingCount, 1)).TotalMilliseconds;
        GUI.TextLine(ref r0, $"gen: {genTime / Level.StackChunkCount,6:0.00}ms / {genTime,6:0.00}ms ({level.GenerationCount})");
        GUI.TextLine(ref r0, $"tri: {meshTime / Level.StackChunkCount,6:0.00}ms / {meshTime,6:0.00}ms ({level.MeshingCount})");
        GUI.TextLine(ref r0, $"lgt: {lightTime / Level.StackChunkCount,6:0.00}ms / {lightTime,6:0.00}ms ({level.LightingCount})");

        if (GUI.Button(ButtonLine(ref r0), "Reload resources")) ReloadResources();

        r0.CutTop(18);

        lastSidebarHeight = r0.Top - startTop;
    }

    public void DrawBlock(in Vector2 position, in float size, in Block block)
    {
        if (block == 0) return;

        Vector2[] HexVertices = [
            new Vector2(0.0f, 0.0f),

            // Outer vertices of a hexagon, starting at top and going clockwise
            new Vector2(0.0f, MathF.Sqrt(3.0f)),
            new Vector2(3.0f / 2.0f, MathF.Sqrt(3.0f) / 2.0f),
            new Vector2(3.0f / 2.0f, -MathF.Sqrt(3.0f) / 2.0f),
            new Vector2(0.0f, -MathF.Sqrt(3.0f)),
            new Vector2(-3.0f / 2.0f, -MathF.Sqrt(3.0f) / 2.0f),
            new Vector2(-3.0f / 2.0f, MathF.Sqrt(3.0f) / 2.0f),
        ];

        var radius = new Vector2(size, -size);
        var quadTop = new Quad(HexVertices[0], radius * HexVertices[6], radius * HexVertices[1], radius * HexVertices[2]);
        var quadRgt = new Quad(HexVertices[0], radius * HexVertices[2], radius * HexVertices[3], radius * HexVertices[4]);
        var quadLft = new Quad(HexVertices[0], radius * HexVertices[6], radius * HexVertices[5], radius * HexVertices[4]);

        static Quad MakeUV(Block block, int face)
        {
            var uv_i = Data.GetBlockData(block).Textures[face];
            var uv = (new Vector2(uv_i.X, uv_i.Y) / 16) + new Vector2(1.0f / 64);
            return new Quad(
                uv,
                uv + (Vector2.UnitX / 32),
                uv + (Vector2.One / 32),
                uv + (Vector2.UnitY / 32)
            );
        }

        GUI.QuadTexture(quadTop.Translate(position), MakeUV(block, 5), level.TerrainTexture, new Color((int)(0xFFFFFF * 1.0), 0xFF));
        GUI.QuadTexture(quadRgt.Translate(position), MakeUV(block, 0), level.TerrainTexture, new Color((int)(0xFFFFFF * 0.4), 0xFF));
        GUI.QuadTexture(quadLft.Translate(position), MakeUV(block, 2), level.TerrainTexture, new Color((int)(0xFFFFFF * 0.8), 0xFF), true);
    }

    void DrawMenuHotbar()
    {
        if (level.PlayerTarget == null) return;
        var hotbar = ((Player)level.PlayerTarget).Hotbar;

        var r0 = GUI.Viewport.Inflate(-40);
        var r1 = r0.CutBottom(40).AlignCenter(new Rect(0, 0, hotbar.Length * 40, 40));
        GUI.RectColored(r1, new Color(000, 0x88));

        // Two loops to avoid excessive state switching during rendering

        for (int i = 0; i < hotbar.Length; i++)
        {
            var item = hotbar[i].Item;

            var r2 = r1.CutLeft(40).Inflate(-4);
            GUI.RectColored(r2, new Color(000, 0x88));
            GUI.RectLine(r2, i == selectedHotbarSlot ? new Color(0xAA_AA_AA, 0x88) : Color.Black);

            if (r2.Pressed(MouseButtons.Left)) selectedHotbarSlot = i;
            if (r2.Pressed(MouseButtons.Right))
            {
                selectedHotbarSlot = i;
                hotbar[selectedHotbarSlot] = InventorySlot.Clear;
            }

            if (item == 0) continue;
            var data = Data.GetItemData(item);
            if (data.GetType() != typeof(ItemBlock)) continue;

            DrawBlock(r2.Center, 8.0f, ((ItemBlock)data).PlaceTarget);

            var rt = r2.GetRight(16).GetBottom(12);
            GUI.TextRect(rt, $"{hotbar[i].Count}", shadow: true);
        }
    }

    void DrawLoadingScreen()
    {
        var r0 = GUI.Viewport.AlignCenter(200, 16);

        var r1 = r0;
        r1.Width *= level.LoadingScreenProgress;
        r1 = r1.Inflate(-1);

        GUI.RectColored(r0, Color.Black);
        GUI.RectColored(r1, Color.Green);
    }

    void DrawMainMenu()
    {
        static Rect ButtonLine(ref Rect rect) => rect.CutTop(36).GetLeft(rect.Width).Inflate(-4);

        var r0 = GUI.Viewport.AlignCenter(256, 36 * 3);

        if (GUI.Button(ButtonLine(ref r0), "Play"))
        {
            menuManager.Push<MenuLevelSelect>();
        }

        if (GUI.Button(ButtonLine(ref r0), "Settings"))
        {
            menuManager.Push<MenuSettings>();
        }

        if (GUI.Button(ButtonLine(ref r0), "Exit"))
        {
            App.Exit();
        }
    }

    public override void Update()
    {
        Time.FramerateTarget = TimeSpan.FromSeconds(1.0f / Math.Clamp(Config.MaxFrameRate, 1, 1000));
        Time.FixedStepTarget = TimeSpan.FromSeconds(1.0f / 100.0f);

        if (App.VSync != Config.DoVerticalSync) App.VSync = Config.DoVerticalSync;

        // Mouse locking

        bool shouldLockMouse = (level.IsLoaded && menuManager.IsTopLevel && !Input.Keyboard.Down(Keys.LeftAlt));
        if (Input.MouseLocked && (!App.Focused || !shouldLockMouse)) {
            Input.MouseLocked = false;
            Input.SetMousePosition(App.WidthInPixels / 2, App.HeightInPixels / 2);
        }
        if (!Input.MouseLocked && shouldLockMouse && (Input.Mouse.Down(MouseButtons.Left) || level.IsFirstTick)) Input.MouseLocked = true;

        Line.NewFrame();
        GUI.NewFrame();

        //

        if (Input.Keyboard.Pressed(Keys.F1)) sidebarState = (sidebarState + 1) % 3;
        if (Input.Keyboard.Pressed(Keys.F2)) drawTerrain = !drawTerrain;
        if (Input.Keyboard.Pressed(Keys.F3)) drawSkyTexture = !drawSkyTexture;
        if (Input.Keyboard.Pressed(Keys.F4)) drawTerrainTexture = !drawTerrainTexture;
        if (Input.Keyboard.Pressed(Keys.F5)) ReloadResources();
        if (Input.Keyboard.Pressed(Keys.F6)) drawCloudTexture = !drawCloudTexture;
        if (Input.Keyboard.Pressed(Keys.F8)) drawShadowTexture = !drawShadowTexture;

        if (Input.Keyboard.Pressed(Keys.F7) && !level.IsLoaded)
        {
            level.CreateSave($"World {Random.Shared.Next() % 10000:0000}");
            var saveList = DiskStorage.ScanForSaves();
            if (saveList.Length > 0) level.LoadSave(saveList[0].path);
        };

        if (Input.Mouse.RightDown && level.IsLoaded && !Input.MouseLocked)
        {
            level.CurrentDayCycleTick += (int)Input.Mouse.Delta.Y * 100;
        }

        //

        UpdatePlayerGUIInput();

        if (menuManager.IsTopLevel)
        {
            UpdatePlayerInput();
        }

        level.Update();
    }

    public override void FixedUpdate()
    {
        Line.NewFixedFrame();

        level.FixedUpdate();

        Line.EndFixedFrame();
    }

    public override void LateUpdate()
    {
        level.LateUpdate();

        // Menues and GUI

        if (Input.Keyboard.Pressed(Keys.Escape))
        {
            if (menuManager.IsTopLevel)
            {
                if (level.IsLoaded)
                {
                    menuManager.Push<MenuPause>();
                }
            }
            else
            {
                menuManager.Exit();
            }
        }

        menuManager.Update(this);

        // Main menu
        if (!level.IsLoaded && menuManager.IsTopLevel)
        {
            DrawMainMenu();
        }

        if (level.IsLoaded && !level.IsSimulationReady)
        {
            DrawLoadingScreen();
        }

        if (level.IsSimulationReady && level.PlayerTarget != null)
        {
            DrawMenuHotbar();

            if (menuManager.IsTopLevel)
            {
                if (Input.Keyboard.Pressed(Keys.B))
                {
                    menuManager.Push<MenuBlocks>();
                }

                if (Input.Keyboard.Pressed(Keys.C))
                {
                    menuManager.Push<MenuDataEditor>();
                }

                // Reticle
                GUI.RectColored(GUI.Viewport.AlignCenter(new Rect(0, 0, 8, 2)), new Color(0x77_77_77, 1));
                GUI.RectColored(GUI.Viewport.AlignCenter(new Rect(0, 0, 2, 8)), new Color(0x77_77_77, 1));
            }
        }

        if (drawSkyTexture)
        {
            var skyTex = level.SkyState.GetAtmosphereTexture();
            var rect = new Rect(Vector2.Zero, skyTex.Size);
            GUI.RectTexture(rect, rect, skyTex);
        }

        if (drawTerrainTexture)
        {
            var terrainTex = level.TerrainTexture;
            var rect = new Rect(Vector2.Zero, terrainTex.Size);
            GUI.RectTexture(rect, rect, terrainTex);
        }

        if (drawCloudTexture)
        {
            var cloudTex = level.SkyState.GetCloudTexture();
            var rect = new Rect(Vector2.Zero, cloudTex.Size);
            GUI.RectTexture(rect, rect, cloudTex);
        }

        if (drawShadowTexture)
        {
            var shadowTex0 = level.ShadowTarget0.Attachments[0];
            var shadowTex1 = level.ShadowTarget1.Attachments[0];

            var texRect = new Rect(Vector2.Zero, shadowTex0.Size);
            var posRect0 = new Rect(Vector2.Zero, shadowTex0.Size);
            var posRect1 = new Rect(Vector2.UnitX * shadowTex0.Size.X, (Vector2.UnitX * shadowTex0.Size.X) + shadowTex0.Size);

            GUI.RectTexture(posRect0.Scale(0.25f), texRect, shadowTex0);
            GUI.RectTexture(posRect1.Scale(0.25f), texRect, shadowTex1);
        }

        DrawMenuSidebar();

        Audio.Update(level.PlayerTarget ?? level.NullEntity);
    }

    private void UpdatePlayerGUIInput()
    {
        if (level.PlayerTarget == null || (level.PlayerTarget.GetType() != typeof(Player))) return;
        var player = (Player)level.PlayerTarget;
        var hotbar = player.Hotbar;

        selectedHotbarSlot -= (int)Input.Mouse.Wheel.Y;
        selectedHotbarSlot = (Math.Abs(selectedHotbarSlot * hotbar.Length) + selectedHotbarSlot) % hotbar.Length;

        if (Input.Keyboard.Pressed(Keys.D1)) selectedHotbarSlot = 0;
        if (Input.Keyboard.Pressed(Keys.D2)) selectedHotbarSlot = 1;
        if (Input.Keyboard.Pressed(Keys.D3)) selectedHotbarSlot = 3;
        if (Input.Keyboard.Pressed(Keys.D4)) selectedHotbarSlot = 4;
        if (Input.Keyboard.Pressed(Keys.D5)) selectedHotbarSlot = 5;
        if (Input.Keyboard.Pressed(Keys.D6)) selectedHotbarSlot = 6;
        if (Input.Keyboard.Pressed(Keys.D7)) selectedHotbarSlot = 7;
        if (Input.Keyboard.Pressed(Keys.D8)) selectedHotbarSlot = 8;
        if (Input.Keyboard.Pressed(Keys.D9)) selectedHotbarSlot = 9;
        if (Input.Keyboard.Pressed(Keys.D0)) selectedHotbarSlot = 10;
    }

    private void UpdatePlayerInput()
    {
        if (!level.IsSimulationReady || level.PlayerTarget == null || (level.PlayerTarget.GetType() != typeof(Player)) || !Input.MouseLocked) return;
        var player = (Player)level.PlayerTarget;
        var hotbar = player.Hotbar;

        (Block rayBlock, EntityVector hitPos, Vector3i hitNormal) = level.RaySolid(player.GetInterpolatedEyePosition(Time.FixedAlpha), EntityVector.FromBlock(player.Forward));

        if (rayBlock != 0)
        {
            Line.PushCubeNormal(hitPos, Vector3.One, hitNormal, Color.Black);

            var audioPos = player.GetInterpolatedEyePosition(Time.FixedAlpha) + EntityVector.FromBlock(player.Forward * (hitPos - player.GetInterpolatedEyePosition(Time.FixedAlpha)).ToVector3().Length());

            if (Input.Mouse.MiddlePressed || Input.Keyboard.Pressed(Keys.T))
            {
                if (rayBlock != 0)
                {
                    hotbar[selectedHotbarSlot] = new InventorySlot(Data.GetBlockData(rayBlock).ItemDrop, 1);
                }
            }

            if (Input.Mouse.LeftPressed)
            {
                level.SetBlock(hitPos, 0, audioPos);
            }

            if (Input.Mouse.RightPressed)
            {
                if (hotbar[selectedHotbarSlot].Item != 0)
                {
                    var data = Data.GetItemData(hotbar[selectedHotbarSlot].Item);

                    if (data.GetType() == typeof(ItemBlock))
                    {
                        var placeTarget = ((ItemBlock)data).PlaceTarget;
                        bool didSet = level.TrySetBlock(player, hitPos + hitNormal, placeTarget, audioPos);

                        if (didSet)
                        {
                            hotbar[selectedHotbarSlot].Count -= 1;
                            if (hotbar[selectedHotbarSlot].Count == 0) hotbar[selectedHotbarSlot] = new();
                        }
                    }
                }

            }
        }

        if (Input.Keyboard.Pressed(Keys.V))
        {
            player.DoGravity = !player.DoGravity;
        }

        //
        // Player movement input
        //

        Vector3 movement = default;

        if (Input.MouseLocked)
        {
            player.Rotation.Y -= Input.Mouse.Delta.X / 5000.0f * Config.MouseSensitivity;
            player.Rotation.X -= Input.Mouse.Delta.Y / 5000.0f * Config.MouseSensitivity;
            player.Rotation.X = Math.Clamp(player.Rotation.X, 0.1f, 3.0f);
        }

        if (Input.Keyboard.Down(Keys.W)) movement.Y += 1.0f;
        if (Input.Keyboard.Down(Keys.S)) movement.Y -= 1.0f;
        if (Input.Keyboard.Down(Keys.A)) movement.X -= 1.0f;
        if (Input.Keyboard.Down(Keys.D)) movement.X += 1.0f;
        if (Input.Keyboard.Down(Keys.Space)) movement.Z += 1.0f;
        if (Input.Keyboard.Down(Keys.LeftControl)) movement.Z -= 1.0f;
        if (Input.Keyboard.Pressed(Keys.Space)) player.DoJump();

        if (movement != Vector3.Zero)
        {
            if (movement.X != 0.0f || movement.Y != 0.0f)
            {
                var strafe = MathF.Atan2(movement.Y, movement.X);
                movement.X = MathF.Cos(player.Rotation.Y + strafe);
                movement.Y = MathF.Sin(player.Rotation.Y + strafe);
            }
        }

        player.MovementDirection = EntityVector.FromBlock(movement);
    }

    public Matrix4x4 GetPerspectiveMatrix()
    {
        const float CameraNearPlane = 0.1f;
        const float CameraFarPlane = 1000.0f;

        return Matrix4x4.CreatePerspectiveFieldOfView(
            float.DegreesToRadians(Config.CameraFieldOfView),
            (float)App.Width / App.Height,
            CameraNearPlane,
            CameraFarPlane
        );
    }

    public override void Render()
    {
        prevTime = Time.Now;

        Config.WindowSizeX = App.Width;
        Config.WindowSizeY = App.Height;

        Graphics.Clear(Color.Transparent, 1.0f, 0, ClearMask.Depth);

        // Draw level

        if (drawTerrain && level.IsLoaded)
        {
            level.Render(null, GetPerspectiveMatrix());
        }

        // Draw background
        if (!drawTerrain || !level.IsSimulationReady)
        {
            backgroundMaterial.Set("u_transform", Matrix4x4.Identity);
            backgroundMaterial.Set("u_texture", level.TerrainTexture);
            backgroundMaterial.Set("u_uv", new Vector4(
                (2.0f / 16) + (1.0f / 64),
                (0.0f / 16) + (1.0f / 64),
                32.0f,
                64.0f
            ));

            new DrawCommand(null, quad, backgroundMaterial)
            {
                BlendMode = BlendMode.Premultiply,
                DepthMask = false,
                MeshIndexStart = 0,
                MeshIndexCount = 6,
            }.Submit();
        }

        // Draw vignette

        vignetteMaterial.Set("u_transform", Matrix4x4.Identity);
        vignetteMaterial.Set("u_strength", Config.VignetteStrength);

        new DrawCommand(null, quad, vignetteMaterial)
        {
            BlendMode = BlendMode.Multiply,
            DepthMask = false,
            MeshIndexStart = 0,
            MeshIndexCount = 6,
        }.Submit();

        // Draw GUI

        GUI.Render();

        //
    }

    //
    //
    //

    public static Material MakeMaterial((string, string) path, string vertexEntry = "", string fragmentEntry = "", (string, object)[]? options = null) => MakeMaterial(path.Item1, path.Item2, vertexEntry, fragmentEntry, options);

    public static Material MakeMaterial(string vertexPath, string fragmentPath, string vertexEntry = "", string fragmentEntry = "", (string, object)[]? options = null)
    {
        string vertexData, fragmentData;

        try
        {
            vertexData = File.ReadAllText(vertexPath);
        }
        catch
        {
            throw new Log.Fatal($"Failed to read shader file at \"{vertexPath}\"");
        }

        try
        {
            fragmentData = File.ReadAllText(fragmentPath);
        }
        catch
        {
            throw new Log.Fatal($"Failed to read shader file at \"{fragmentPath}\"");
        }

        if (vertexEntry != "") vertexData = vertexEntry.Replace(vertexEntry, "main");
        if (fragmentEntry != "") fragmentData = fragmentData.Replace(fragmentEntry, "main");

        // Hacky way of doing shader settings by replacing #define values, or undefining for booleans.
        if (options != null)
        {
            string[] separators = ["\r\n", "\r", "\n"];

            var vertexLines = vertexData.Split(separators, StringSplitOptions.None);
            var fragLines = fragmentData.Split(separators, StringSplitOptions.None);

            foreach ((string name, object value) in options)
            {
                for (int i = 0; i < vertexLines.Length; i++)
                {
                    if (vertexLines[i].Contains($"#define {name}"))
                    {
                        vertexLines[i] = value switch
                        {
                            bool => (bool)value ? $"#define {name}" : $"//#define {name}",
                            int => $"#define {name} {(int)value}",
                            float => $"#define {name} {(float)value}",
                            _ => throw new Exception("Unhandled shader option value type"),
                        };
                    }
                }

                for (int i = 0; i < fragLines.Length; i++)
                {
                    if (fragLines[i].StartsWith($"#define {name}"))
                    {
                        fragLines[i] = value switch
                        {
                            bool => (bool)value ? $"#define {name}" : $"//#define {name}",
                            int => $"#define {name} {(int)value}",
                            float => $"#define {name} {(float)value}",
                            _ => throw new Exception("Unhandled shader option value type"),
                        };
                    }
                }
            }

            vertexData = string.Join('\n', vertexLines);
            fragmentData = string.Join('\n', fragLines);
        }

        return new Material(new Shader(new ShaderCreateInfo(vertexData, fragmentData)));
    }

    public readonly static Dictionary<Renderers, Dictionary<string, (string Vertex, string Fragment)>> ShaderInfo = new()
    {
        [Renderers.OpenGL] = new()
        {
            ["SkyAtmosphere"] = ("Assets/Shaders/sky_atmosphere.vert.glsl", "Assets/Shaders/sky_atmosphere.frag.glsl"),
            ["SkyBox"] = ("Assets/Shaders/sky_box.vert.glsl", "Assets/Shaders/sky_box.frag.glsl"),
            ["SkyObject"] = ("Assets/Shaders/sky_object.vert.glsl", "Assets/Shaders/sky_object.frag.glsl"),
            ["SkyClouds"] = ("Assets/Shaders/mesh.vert.glsl", "Assets/Shaders/sky_clouds.frag.glsl"),
            ["SkyStars"] = ("Assets/Shaders/mesh.vert.glsl", "Assets/Shaders/sky_stars.frag.glsl"),
            ["LevelTerrain"] = ("Assets/Shaders/level_terrain.vert.glsl", "Assets/Shaders/level_basic.frag.glsl"),
            ["LevelEntity"] = ("Assets/Shaders/level_entity.vert.glsl", "Assets/Shaders/level_basic.frag.glsl"),
            ["LevelLine"] = ("Assets/Shaders/level_line.vert.glsl", "Assets/Shaders/level_line.frag.glsl"),
            ["Vignette"] = ("Assets/Shaders/quad.vert.glsl", "Assets/Shaders/vignette.frag.glsl"),
            ["Background"] = ("Assets/Shaders/quad.vert.glsl", "Assets/Shaders/fullscreen_blocks.frag.glsl"),
            ["Null"] = ("", "Assets/Shaders/null.frag.glsl"),
        },
    };
}