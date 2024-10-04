using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Foster.Framework;

namespace Game;

[InlineArray(Level.ChunkVolume)]
struct Chunk
{
    private Block _blocks;

    public Span<Block> Span => MemoryMarshal.CreateSpan(ref this[0], Level.ChunkVolume);
}

class ChunkStack
{
    public Vector2i Position;
    public Chunk[] Chunks;
    public bool[] IsDirty;
    public bool[] IsLoadedFromDisk;

    public bool IsWaitingForTask;
    public bool IsGenerated;
    public bool IsMeshed;

    public Mesh Mesh;
    public int MeshLowZ, MeshHighZ;
    public int OpaqueIndexStart, OpaqueIndexCount;
    public int LiquidIndexStart, LiquidIndexCount;

    public ReaderWriterLockSlim RWLock;
    public bool Visited;

    public (byte temperature, byte humidity)[,] ClimateMap;

    public ChunkStack(int height, Vector2i position)
    {
        this.Position = position;
        this.Chunks = new Chunk[height];
        this.IsDirty = new bool[height];
        this.IsLoadedFromDisk = new bool[height];
        this.RWLock = new();
        this.Mesh = new();
        this.ClimateMap = new (byte, byte)[Level.ChunkSize, Level.ChunkSize];
    }
}

partial class Level
{
    public const int ChunkSize = 32;
    public const int ChunkArea = ChunkSize * ChunkSize;
    public const int ChunkVolume = ChunkSize * ChunkSize * ChunkSize;
    public const int StackChunkCount = 8;
    public const int StackSizeZ = StackChunkCount * ChunkSize;

    public const int MaxLightRadius = 15;
    public const int SunlightRadius = 15;

    //

    public const int MinimumViewDistance = 4;

    public bool IsLoaded { get; private set; }
    public bool IsSimulationReady { get; private set; }
    public bool IsFirstTick { get; private set; }

    public float LoadingScreenProgress { get; private set; }

    public int CurrentTick = 0;
    public int CurrentDayCycleTick = 0;
    public int Seed;

    public ConcurrentDictionary<Vector2i, ChunkStack> StackMap = [];

    private int CurrentTaskCount;
    private PriorityQueue<(WaitCallback, object), float> TaskQueue;

    private float CalcTaskPriority(Vector2i center, float taskType, Vector2i position)
    {
        return taskType + (1.0f - (1.0f / ((center - position).ToVector2().Length() + 1.0f)));
    }

    private LevelGen.OutputState genOutputState;
    private LevelMesh.OutputState meshingOutputState;

    public Sky SkyState;

    public DiskStorage diskStorage;

    public List<Entity> Entities;
    public readonly Entity NullEntity;

    public Entity? PlayerTarget;

    public T CreateEntity<T>(T entity) where T : Entity
    {
        Entities.Add(entity);
        return entity;
    }

    //
    // Rendering
    //

    public static (string, object)[] LevelMaterialOptions => [
        ("DO_TEXTURE_AA", Config.DoTextureAA),
        ("DO_CLOUD_SHADOWS", Config.DoCloudShadows),
        ("DO_SHADOWS", Config.DoShadowMap),
    ];

    public List<EntityRenderBlock> EntityRenderList;

    public Material OpaqueMaterial;
    public Material LiquidMaterial;
    public Material EntityMaterial;

    public Image TerrainImage;
    public Texture TerrainTexture;
    public TextureSampler TerrainTextureSamplerNearest;
    public TextureSampler TerrainTextureSamplerLinear;
    public TextureSampler ActiveTerrainTextureSampler => Config.DoTextureAA ? TerrainTextureSamplerLinear : TerrainTextureSamplerNearest;

    // shadow mapping stuff

    public Target ShadowTarget0;
    public Target ShadowTarget1;
    public Target ShadowTarget2;
    public TextureSampler ShadowTextureSampler;
    public Material ShadowTerrainMaterial;
    public Material ShadowEntityMaterial;

    // grass + leaves climate colorization stuff

    public Image ClimateColorImage;
    public Texture ClimateColorTexture;
    public TextureSampler ClimateColorTextureSampler;

    public static Color Climate_P01 = new(0x88a13a);
    public static Color Climate_P11 = new(0x44891a);
    public static Color Climate_P00 = new(0x9cd19d);
    public static Color Climate_P10 = new(0xfdf8c4);

    //
    // Debug
    //

    public int ChunkDrawCount;
    public TimeSpan MeshingTime;
    public int MeshingCount;
    public TimeSpan LightingTime;
    public int LightingCount;
    public TimeSpan GenerationTime;
    public int GenerationCount;
    public TimeSpan UpdateTime;
    public TimeSpan FixedTime;

    //
    //
    //

    public Level()
    {
        StackMap = [];

        TaskQueue = new();

        genOutputState = new();
        meshingOutputState = new();

        OpaqueMaterial = Game.MakeMaterial(Game.ShaderInfo[Renderers.OpenGL]["LevelTerrain"], fragmentEntry: "mainOpaque", options: LevelMaterialOptions);
        LiquidMaterial = Game.MakeMaterial(Game.ShaderInfo[Renderers.OpenGL]["LevelTerrain"], fragmentEntry: "mainLiquid", options: LevelMaterialOptions);
        EntityMaterial = Game.MakeMaterial(Game.ShaderInfo[Renderers.OpenGL]["LevelEntity"], fragmentEntry: "mainEntity", options: LevelMaterialOptions);

        ShadowTarget0 = new Target(2048, 2048, [TextureFormat.Depth24Stencil8]);
        ShadowTarget1 = new Target(2048, 2048, [TextureFormat.Depth24Stencil8]);
        ShadowTarget2 = new Target(2048, 2048, [TextureFormat.Depth24Stencil8]);
        ShadowTextureSampler = new TextureSampler(TextureFilter.Linear, TextureWrap.ClampToBorder, TextureWrap.ClampToBorder);
        ShadowTerrainMaterial = Game.MakeMaterial(Game.ShaderInfo[Renderers.OpenGL]["LevelTerrain"].Vertex, Game.ShaderInfo[Renderers.OpenGL]["Null"].Fragment);
        ShadowEntityMaterial = Game.MakeMaterial(Game.ShaderInfo[Renderers.OpenGL]["LevelEntity"].Vertex, Game.ShaderInfo[Renderers.OpenGL]["Null"].Fragment);

        TerrainImage = new Image("Assets/Textures/terrain.png").GeneratePaddedAtlas(16, 16);
        TerrainTexture = new Texture(TerrainImage);

        TerrainTextureSamplerNearest = new TextureSampler(TextureFilter.Nearest, TextureWrap.ClampToEdge, TextureWrap.ClampToEdge);
        TerrainTextureSamplerLinear = new TextureSampler(TextureFilter.Linear, TextureWrap.ClampToEdge, TextureWrap.ClampToEdge);

        EntityRenderList = [];

        Entities = [];

        SkyState = new();

        diskStorage = new();

        ClimateColorImage = new Image("Assets/Textures/grasscolor.png");//new Image(64, 64);
        ClimateColorTexture = new Texture(ClimateColorImage);
        ClimateColorTextureSampler = new TextureSampler(TextureFilter.Linear, TextureWrap.ClampToEdge, TextureWrap.ClampToEdge);

        NullEntity = new Player(EntityVector.Zero, Vector2.Zero);
    }

    public bool CreateSave(string name)
    {
        return DiskStorage.CreateSave(name);
    }

    public bool LoadSave(string path)
    {
        IsLoaded = diskStorage.LoadSave(path);

        if (IsLoaded)
        {
            this.Seed = diskStorage.MetaData.Seed;
            this.CurrentTick = diskStorage.MetaData.ElapsedTicks;
            this.CurrentDayCycleTick = diskStorage.MetaData.ElapsedDayCycleTicks;

            IsSimulationReady = false;
            IsFirstTick = true;

            SkyState.LoadClouds(this.Seed);
            SkyState.LoadStars(this.Seed);
        }

        return IsLoaded;
    }

    public void Exit()
    {
        if (!IsLoaded) return;

        diskStorage.WriteSave(this);

        var deletionQueue = new Stack<Vector2i>();

        foreach (var (position, stack) in StackMap)
        {
            if (!stack.Visited && stack.IsGenerated && stack.IsMeshed)
            {
                deletionQueue.Push(position);
            }

            stack.Visited = false;
        }

        while (deletionQueue.Count > 0)
        {
            DestroyStack(deletionQueue.Pop());
        }

        Entities.Clear();
        PlayerTarget = null;

        IsLoaded = false;
        IsSimulationReady = false;
        IsFirstTick = true;

    }

    public void ReloadResources()
    {
        try
        {
            var newMaterial = Game.MakeMaterial(Game.ShaderInfo[Renderers.OpenGL]["LevelTerrain"], fragmentEntry: "mainOpaque", options: LevelMaterialOptions);
            OpaqueMaterial.Clear();
            OpaqueMaterial = newMaterial;

            newMaterial = Game.MakeMaterial(Game.ShaderInfo[Renderers.OpenGL]["LevelTerrain"], fragmentEntry: "mainLiquid", options: LevelMaterialOptions);
            LiquidMaterial.Clear();
            LiquidMaterial = newMaterial;

            newMaterial = Game.MakeMaterial(Game.ShaderInfo[Renderers.OpenGL]["LevelEntity"], fragmentEntry: "mainEntity", options: LevelMaterialOptions);
            EntityMaterial.Clear();
            EntityMaterial = newMaterial;

            newMaterial = Game.MakeMaterial(Game.ShaderInfo[Renderers.OpenGL]["LevelTerrain"].Vertex, Game.ShaderInfo[Renderers.OpenGL]["Null"].Fragment);
            ShadowTerrainMaterial.Clear();
            ShadowTerrainMaterial = newMaterial;

            newMaterial = Game.MakeMaterial(Game.ShaderInfo[Renderers.OpenGL]["LevelEntity"].Vertex, Game.ShaderInfo[Renderers.OpenGL]["Null"].Fragment);
            ShadowEntityMaterial.Clear();
            ShadowEntityMaterial = newMaterial;

            var newImage = new Image("Assets/Textures/terrain.png").GeneratePaddedAtlas(16, 16);
            TerrainImage.Dispose();
            TerrainImage = newImage;

            TerrainTexture.Dispose();
            TerrainTexture = new Texture(TerrainImage);

            SkyState.ReloadResources();
        }
        catch { }
    }

    public void Update()
    {
        if (!IsLoaded) return;

        var stopwatch = Stopwatch.StartNew();

        if (PlayerTarget == null)
        {
            PlayerTarget = CreateEntity(new Player(EntityVector.FromBlock(0, 0, 160), new Vector2(MathF.PI / 2, 0)));
        }

        //
        // Scan for new chunks within view distance and create tasks
        //

        var VD = (IsSimulationReady ? (Config.ViewDistance) : MinimumViewDistance) + 1;

        Vector2i loadPosition = default;

        if (PlayerTarget != null)
        {
            loadPosition = PlayerTarget.Position.GetChunk().XY;
        }

        if (!IsSimulationReady)
        {
            LoadingScreenProgress = 0.0f;

            int loadingTarget = MinimumViewDistance * MinimumViewDistance * 4 * 2;
            int readyChunks = 0;

            for (int y = -VD; y <= VD; y++)
            {
                for (int x = -VD; x <= VD; x++)
                {
                    var stackPosition = new Vector2i(x + loadPosition.X, y + loadPosition.Y);
                    ChunkStack? stack = GetStackSafe(stackPosition);
                    if (stack == null) continue;
                    if (stack.IsGenerated) readyChunks += 1;
                    if (stack.IsMeshed) readyChunks += 1;
                }
            }

            LoadingScreenProgress = Math.Clamp(((float)readyChunks) / loadingTarget, 0, 1);

            if (readyChunks >= loadingTarget) IsSimulationReady = true;
        }

        for (int y = -VD; y <= VD; y++)
        {
            for (int x = -VD; x <= VD; x++)
            {
                var stackPosition = new Vector2i(x + loadPosition.X, y + loadPosition.Y);

                ChunkStack? stack = GetStackSafe(stackPosition);

                if (stack == null)
                {
                    stack = CreateStack(stackPosition);
                }

                stack.Visited = true;

                if (!stack.IsGenerated && !stack.IsWaitingForTask)
                {
                    diskStorage.LoadStack(stackPosition, ref stack, out int loadedChunkCount);

                    if (loadedChunkCount < stack.Chunks.Length)
                    {
                        float taskPrio = CalcTaskPriority(loadPosition, 1, stackPosition);
                        TaskQueue.Enqueue((LevelGen.Generate, (new LevelGen.TaskData(stack, this.Seed), genOutputState)), taskPrio);
                        stack.IsWaitingForTask = true;
                    }
                    else
                    {
                        stack.IsGenerated = true;
                    }
                }

                if (stack.IsGenerated && !stack.IsMeshed && !stack.IsWaitingForTask && StackHasGeneratedNeighbours(stackPosition))
                {
                    float taskPrio = CalcTaskPriority(loadPosition, 0, stackPosition);
                    TaskQueue.Enqueue((LevelMesh.Generate, (new LevelMesh.TaskData(stackPosition, GetStackNeighbours(stackPosition)), meshingOutputState)), taskPrio);
                    stack.IsWaitingForTask = true;
                    continue;
                }
            }
        }

        //
        // Process task queue
        //

        while (TaskQueue.Count > 0)
        {
            if (CurrentTaskCount > Config.TaskCountLimit) break;

            var entry = TaskQueue.Dequeue();
            ThreadPool.QueueUserWorkItem(entry.Item1, entry.Item2);
            CurrentTaskCount += 1;
        }

        //
        // Cull chunks out of range
        //

        var deletionQueue = new Stack<Vector2i>();

        foreach (var (position, stack) in StackMap)
        {
            if (!stack.Visited && stack.IsGenerated && stack.IsMeshed)
            {
                deletionQueue.Push(position);
            }

            stack.Visited = false;
        }

        while (deletionQueue.Count > 0)
        {
            DestroyStack(deletionQueue.Pop());
        }

        //
        // Process completed generation tasks
        //

        if (genOutputState.Entries.Count > 0 && Monitor.TryEnter(genOutputState.Lock))
        {
            while (genOutputState.Entries.Count > 0)
            {
                var entry = genOutputState.Entries.Pop();
                GenerationTime += entry.GenTime;
                GenerationCount += 1;
                CurrentTaskCount -= 1;

                var stack = GetStack(entry.Position);
                stack.IsGenerated = true;

                if (!stack.IsWaitingForTask) throw new Exception();
                stack.IsWaitingForTask = false;
            }

            Monitor.Exit(genOutputState.Lock);
        }

        //
        // Process completed meshing tasks
        //

        if (meshingOutputState.Entries.Count > 0 && Monitor.TryEnter(meshingOutputState.Lock))
        {
            var vertices = CollectionsMarshal.AsSpan(meshingOutputState.Vertices);
            var indices = CollectionsMarshal.AsSpan(meshingOutputState.Indices);

            while (meshingOutputState.Entries.Count > 0)
            {
                var entry = meshingOutputState.Entries.Pop();

                if (entry.VertexStart + entry.VertexCount > vertices.Length) throw new IndexOutOfRangeException();
                if (entry.IndexStart + entry.IndexCount > indices.Length) throw new IndexOutOfRangeException();

                var stack = StackMap[entry.StackPosition];

                if (stack.Mesh == null || (stack.Mesh != null && stack.Mesh.IsDisposed))
                {
                    stack.Mesh = new();
                }

                if (entry.VertexCount > 0 && entry.IndexCount > 0)
                {
                    var vertexSlice = vertices.Slice(entry.VertexStart, entry.VertexCount);
                    var indexSlice = indices.Slice(entry.IndexStart, entry.IndexCount);

                    // TODO: Avoid SetVertices and use SetSubVertices when the log2 of the meshes 
                    // VertexCount is equal

                    stack.Mesh!.SetVertices<ChunkVertex>(vertexSlice);
                    stack.Mesh!.SetIndices<int>(indexSlice);
                }

                stack.OpaqueIndexStart = entry.OpaqueIndexStart;
                stack.OpaqueIndexCount = entry.OpaqueIndexCount;

                stack.LiquidIndexStart = entry.LiquidIndexStart;
                stack.LiquidIndexCount = entry.LiquidIndexCount;

                stack.MeshLowZ = entry.LowZ;
                stack.MeshHighZ = entry.HighZ;

                stack.IsMeshed = true;

                MeshingTime += entry.MeshingTime;
                MeshingCount += 1;

                LightingTime += entry.LightingTime;
                LightingCount += 1;

                if (!stack.IsWaitingForTask) throw new Exception();
                stack.IsWaitingForTask = false;
                CurrentTaskCount -= 1;
            }

            meshingOutputState.Vertices.Clear();
            meshingOutputState.Indices.Clear();

            Monitor.Exit(meshingOutputState.Lock);
        }

        UpdateTime = stopwatch.Elapsed;
    }

    public void FixedUpdate()
    {
        if (!IsSimulationReady) return;

        var stopwatch = Stopwatch.StartNew();

        if (IsFirstTick)
        {
            if (PlayerTarget!.DoGravity) SettleEntityTop(ref PlayerTarget!);
        }

        foreach (var entity in Entities)
        {
            entity.PositionPrev = entity.Position;
            entity.RotationPrev = entity.Rotation;
            entity.VelocityPrev = entity.Velocity;
        }

        foreach (var entity in Entities)
        {
            entity.Update(this, CollectionsMarshal.AsSpan(Entities));
            entity.Simulate(this);
        }

        for (int i = Entities.Count - 1; i >= 0; i--)
        {
            if (Entities[i].ShouldKill)
            {
                Audio.Play(Entities[i].AudioOnKill, 1.0f, Entities[i].Position);
                Entities.RemoveAt(i);
                continue;
            }
        }

        CurrentTick += 1;
        CurrentDayCycleTick += 1;

        IsFirstTick = false;

        FixedTime = stopwatch.Elapsed;
    }

    public void LateUpdate()
    {
        if (!IsSimulationReady) return;

        if (App.DidFixedUpdate)
        {
            foreach (var entity in Entities.OfType<Player>())
            {
                entity.MovementDirection = default;
            }
        }
    }

    //
    // Rendering
    //

    struct ChunkDrawCall
    {
        public Vector3 Offset;
        public Mesh Mesh;
        public int IndexStart, IndexCount;

        public ChunkDrawCall(Vector3 offset, Mesh mesh, int indexStart, int indexCount)
        {
            this.Offset = offset;
            this.Mesh = mesh;
            this.IndexStart = indexStart;
            this.IndexCount = indexCount;
        }
    }

    public void Render(Target? target, Matrix4x4 projectionMatrix)
    {
        if (!IsLoaded) return;

        EntityVector eyePosition = default;
        Matrix4x4 localViewMatrix = Matrix4x4.Identity;
        Matrix4x4 shadowMatrix0 = Matrix4x4.Identity;
        Matrix4x4 shadowMatrix1 = Matrix4x4.Identity;
        Matrix4x4 shadowMatrix2 = Matrix4x4.Identity;

        if (PlayerTarget != null)
        {
            eyePosition = PlayerTarget.GetInterpolatedEyePosition(Time.FixedAlpha);

            var rotationMatrix = Matrix4x4.Identity;
            rotationMatrix *= Matrix4x4.CreateRotationZ(-PlayerTarget.Rotation.Y);
            rotationMatrix *= Matrix4x4.CreateRotationX(-PlayerTarget.Rotation.X);
            localViewMatrix = rotationMatrix * localViewMatrix * projectionMatrix;

            if (Config.DoShadowMap)
            {
                var shadowDir = Sky.GetSunDirection(CurrentDayCycleTick, Time.FixedAlpha);
                var moonDir = Sky.GetMoonDirection(CurrentDayCycleTick, Time.FixedAlpha);
                if (shadowDir.Z < 0 && moonDir.Z > 0) shadowDir = moonDir;
                var shadowMat = Matrix4x4.CreateLookAt(Vector3.Zero, shadowDir, Vector3.UnitZ);

                shadowMatrix0 = shadowMat * Matrix4x4.CreateOrthographicLeftHanded(48, 48, -1024, 1024);
                shadowMatrix1 = shadowMat * Matrix4x4.CreateOrthographicLeftHanded(256, 256, -1024, 1024);
                shadowMatrix2 = shadowMat * Matrix4x4.CreateOrthographicLeftHanded(1024, 1024, -1024, 1024);
            }
        }

        //
        // Draw sky
        //

        SkyState.Render(target, localViewMatrix, eyePosition, CurrentDayCycleTick);

        //
        // Generate terrain draw calls
        //

        List<ChunkDrawCall> callsOpaque = [];
        List<ChunkDrawCall> callsLiquid = [];
        List<ChunkDrawCall> callsShadow0 = [];
        List<ChunkDrawCall> callsShadow1 = [];
        List<ChunkDrawCall> callsShadow2 = [];

        var viewFrustum = localViewMatrix.GetFrustum();
        var shadow0Frustum = shadowMatrix0.GetFrustum();
        var shadow1Frustum = shadowMatrix1.GetFrustum();
        var shadow2Frustum = shadowMatrix2.GetFrustum();

        foreach ((Vector2i position, ChunkStack stack) in StackMap)
        {
            if (stack.Mesh == null || stack.Mesh.VertexCount == 0) continue;

            var aabb = EntityAABB.FromChunk(position);
            aabb.Position.Z = EntityVector.FromBlock(stack.MeshLowZ - 1);
            aabb.Size.Z = EntityVector.FromBlock(stack.MeshHighZ - stack.MeshLowZ + 2);
            var offset = (eyePosition - EntityVector.FromBlock(position.X * ChunkSize, position.Y * ChunkSize, 0)).ToVector3();

            if (EntityAABB.Intersect(aabb, eyePosition, viewFrustum))
            {
                if (stack.OpaqueIndexCount > 0) callsOpaque.Add(new(offset, stack.Mesh, stack.OpaqueIndexStart, stack.OpaqueIndexCount));
                if (stack.LiquidIndexCount > 0) callsLiquid.Add(new(offset, stack.Mesh, stack.LiquidIndexStart, stack.LiquidIndexCount));
            }

            if (Config.DoShadowMap)
            {
                if (stack.OpaqueIndexCount > 0 && EntityAABB.Intersect(aabb, eyePosition, shadow0Frustum))
                {
                    callsShadow0.Add(new(offset, stack.Mesh, stack.OpaqueIndexStart, stack.OpaqueIndexCount));
                }

                if (stack.OpaqueIndexCount > 0 && EntityAABB.Intersect(aabb, eyePosition, shadow1Frustum))
                {
                    callsShadow1.Add(new(offset, stack.Mesh, stack.OpaqueIndexStart, stack.OpaqueIndexCount));
                }

                if (stack.OpaqueIndexCount > 0 && EntityAABB.Intersect(aabb, eyePosition, shadow2Frustum))
                {
                    callsShadow2.Add(new(offset, stack.Mesh, stack.OpaqueIndexStart, stack.OpaqueIndexCount));
                }
            }
        }

        ChunkDrawCount = callsOpaque.Count + callsLiquid.Count;

        //
        // Shadow pass
        //

        if (Config.DoShadowMap)
        {
            ShadowTarget0.Clear(Color.Black, 1.0f, 0, ClearMask.Depth);
            ShadowTarget1.Clear(Color.Black, 1.0f, 0, ClearMask.Depth);
            ShadowTarget2.Clear(Color.Black, 1.0f, 0, ClearMask.Depth);

            if (callsShadow0.Count > 0)
            {
                ShadowTerrainMaterial.Set("u_localViewMatrix", shadowMatrix0);

                var dc = new DrawCommand()
                {
                    Target = ShadowTarget0,
                    Material = ShadowTerrainMaterial,
                    DepthCompare = DepthCompare.Less,
                    DepthMask = true,
                    CullMode = CullMode.Back,
                };

                foreach (var call in callsShadow0)
                {
                    ShadowTerrainMaterial.Set("u_offset", call.Offset);
                    dc.Mesh = call.Mesh;
                    dc.MeshIndexStart = call.IndexStart;
                    dc.MeshIndexCount = call.IndexCount;
                    dc.Submit();
                }
            }

            if (callsShadow1.Count > 0)
            {
                ShadowTerrainMaterial.Set("u_localViewMatrix", shadowMatrix1);

                var dc = new DrawCommand()
                {
                    Target = ShadowTarget1,
                    Material = ShadowTerrainMaterial,
                    DepthCompare = DepthCompare.Less,
                    DepthMask = true,
                    CullMode = CullMode.Back,
                };

                foreach (var call in callsShadow1)
                {
                    ShadowTerrainMaterial.Set("u_offset", call.Offset);
                    dc.Mesh = call.Mesh;
                    dc.MeshIndexStart = call.IndexStart;
                    dc.MeshIndexCount = call.IndexCount;
                    dc.Submit();
                }
            }

            if (callsShadow2.Count > 0)
            {
                ShadowTerrainMaterial.Set("u_localViewMatrix", shadowMatrix2);

                var dc = new DrawCommand()
                {
                    Target = ShadowTarget2,
                    Material = ShadowTerrainMaterial,
                    DepthCompare = DepthCompare.Less,
                    DepthMask = true,
                    CullMode = CullMode.Back,
                };

                foreach (var call in callsShadow2)
                {
                    ShadowTerrainMaterial.Set("u_offset", call.Offset);
                    dc.Mesh = call.Mesh;
                    dc.MeshIndexStart = call.IndexStart;
                    dc.MeshIndexCount = call.IndexCount;
                    dc.Submit();
                }
            }
        }

        //
        // Generate entity draw calls
        //

        EntityRenderList.Clear();

        foreach (var entity in Entities)
        {
            entity.Render(ref EntityRenderList, CurrentTick);
        }

        //
        // Draw opaque terrain meshes
        //

        void SetLevelMaterialUniforms(Material material)
        {
            material.Set("u_localViewMatrix", localViewMatrix);
            material.Set("u_terrainTexture", TerrainTexture);
            material.Set("u_terrainTexture_sampler", ActiveTerrainTextureSampler);
            material.Set("u_skyTexture", SkyState.GetAtmosphereTexture());
            material.Set("u_sunDirection", Sky.GetSunDirection(CurrentDayCycleTick, Time.FixedAlpha));
            material.Set("u_moonDirection", Sky.GetMoonDirection(CurrentDayCycleTick, Time.FixedAlpha));
            material.Set("u_viewDistance", (float)(Config.ViewDistance * ChunkSize));

            if (Config.DoShadowMap)
            {
                material.Set("u_shadowMatrix0", shadowMatrix0);
                material.Set("u_shadowMatrix1", shadowMatrix1);
                material.Set("u_shadowMatrix2", shadowMatrix2);
                material.Set("u_shadowTexture0", ShadowTarget0.Attachments[0]);
                material.Set("u_shadowTexture1", ShadowTarget1.Attachments[0]);
                material.Set("u_shadowTexture2", ShadowTarget2.Attachments[0]);
                material.Set("u_shadowTexture0_sampler", ShadowTextureSampler);
                material.Set("u_shadowTexture1_sampler", ShadowTextureSampler);
                material.Set("u_shadowTexture2_sampler", ShadowTextureSampler);
            }

            if (Config.DoCloudShadows)
            {
                material.Set("u_cloudTexture", SkyState.CloudTexture);
                material.Set("u_cloudTexture_sampler", SkyState.CloudTextureWrapSampler);
                material.Set("u_cloudOffset", SkyState.GetCloudOffset(eyePosition, CurrentDayCycleTick, Time.FixedAlpha));
            }
        }

        if (callsOpaque.Count > 0)
        {
            callsOpaque.Sort((a, b) => a.Offset.LengthSquared().CompareTo(b.Offset.LengthSquared()));

            SetLevelMaterialUniforms(OpaqueMaterial);
            OpaqueMaterial.Set("u_climateColorTexture", ClimateColorTexture);
            OpaqueMaterial.Set("u_climateColorTexture_sampler", ClimateColorTextureSampler);

            var dc = new DrawCommand()
            {
                Target = target,
                Material = OpaqueMaterial,
                DepthCompare = DepthCompare.Less,
                DepthMask = true,
                CullMode = CullMode.Back,
            };

            foreach (var call in callsOpaque)
            {
                OpaqueMaterial.Set("u_offset", call.Offset);
                dc.Mesh = call.Mesh;
                dc.MeshIndexStart = call.IndexStart;
                dc.MeshIndexCount = call.IndexCount;
                dc.Submit();
            }
        }

        //
        // Draw entities
        //

        if (EntityRenderList.Count > 0)
        {
            SetLevelMaterialUniforms(EntityMaterial);

            var dc = new DrawCommand()
            {
                Target = target,
                Material = EntityMaterial,
                DepthCompare = DepthCompare.Less,
                DepthMask = true,
                CullMode = CullMode.None,
                Mesh = Data.GetEntityMesh(),
            };

            foreach (var entry in EntityRenderList)
            {
                var offset = Matrix4x4.CreateTranslation((entry.position - eyePosition).ToVector3());
                EntityMaterial.Set("u_modelMatrix", entry.localTransform * offset);
                EntityMaterial.Set("u_localModelMatrix", entry.localTransform);
                EntityMaterial.Set("u_zPosition", EntityVector.ToBlockFloat(entry.position.Z));

                dc.MeshIndexStart = entry.MeshIndexStart;
                dc.MeshIndexCount = entry.MeshIndexCount;
                dc.Submit();
            }
        }

        //
        // Draw lines
        //

        if (PlayerTarget != null)
        {
            Line.Draw(target, localViewMatrix, eyePosition);
        }

        //
        // Draw liquid terrain meshes
        //

        if (callsLiquid.Count > 0)
        {
            callsLiquid.Sort((a, b) => a.Offset.LengthSquared().CompareTo(b.Offset.LengthSquared()));

            SetLevelMaterialUniforms(LiquidMaterial);

            var dc = new DrawCommand()
            {
                Target = target,
                Material = LiquidMaterial,
                DepthCompare = DepthCompare.Less,
                DepthMask = true,
                CullMode = CullMode.None,
                BlendMode = BlendMode.Premultiply,
            };

            foreach (var call in callsLiquid)
            {
                LiquidMaterial.Set("u_offset", call.Offset);
                dc.Mesh = call.Mesh;
                dc.MeshIndexStart = call.IndexStart;
                dc.MeshIndexCount = call.IndexCount;
                dc.Submit();
            }
        }
    }

    //
    // Chunk management
    //

    private ChunkStack CreateStack(Vector2i position)
    {
        return StackMap[position] = new ChunkStack(StackChunkCount, position);
    }

    private void DestroyStack(Vector2i position)
    {
        bool didRemove = StackMap.Remove(position, out var stack);
        if (!didRemove || stack == null) throw new ArgumentNullException(nameof(position));
        stack.Mesh?.Dispose();
    }

    public ChunkStack GetStack(Vector2i position)
    {
        return StackMap[position];
    }

    public ChunkStack? GetStackSafe(Vector2i position)
    {
        StackMap.TryGetValue(position, out var stack);
        return stack;
    }

    private bool StackHasGeneratedNeighbours(Vector2i position)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                ChunkStack? stack = GetStackSafe(position + (dx, dy));
                if (stack == null) return false;
                if (!stack.IsGenerated) return false;
            }
        }

        return true;
    }

    private ChunkStack[] GetStackNeighbours(Vector2i position)
    {
        var arr = new ChunkStack[9];

        for (int dy = -1, i = 0; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++, i++)
            {
                arr[i] = StackMap[position + (dx, dy)];
            }
        }

        return arr;
    }

    //
    // Utility
    //

    public void SettleEntityTop(ref Entity entity)
    {
        var bp = entity.Position.GetGlobalBlock();

        for (bp.Z = StackSizeZ - 1; bp.Z > 0; bp.Z--)
        {
            var nb = GetBlock(bp);

            if (nb != 0)
            {
                bp.Z += 1;
                entity.Position = EntityVector.FromBlock(bp) + EntityVector.FromBlock(0.5f, 0.5f, 0.0f);
                entity.PositionPrev = entity.Position;
                return;
            }
        }

        entity.Position = EntityVector.FromBlock(bp);
        entity.PositionPrev = entity.Position;
    }

    public void SettleEntity(ref Entity entity)
    {
        var bp = entity.Position.GetGlobalBlock();

        int oz = bp.Z;

        for (bp.Z = oz; bp.Z > 0; bp.Z--)
        {
            var nb = GetBlock(bp);

            if (nb == 0 || Data.GetBlockData(nb).IsLiquid)
            {
                entity.Position = EntityVector.FromBlock(bp);
                entity.PositionPrev = entity.Position;
                return;
            }
        }

        for (bp.Z = oz; bp.Z < StackSizeZ; bp.Z++)
        {
            var nb = GetBlock(bp);

            if (nb == 0 || Data.GetBlockData(nb).IsLiquid)
            {
                entity.Position = EntityVector.FromBlock(bp);
                entity.PositionPrev = entity.Position;
                return;
            }
        }

        entity.Position = EntityVector.FromBlock(bp);
        entity.PositionPrev = entity.Position;
    }

    public Block GetBlock(Vector3i position)
    {
        var c = position.GetChunk();
        var b = position.GetLocalBlock();
        if (c.Z < 0 || c.Z >= StackChunkCount) return Data.DefaultNonSolid;
        return GetStackSafe(c.XY)?.Chunks[c.Z][b.GetLocalBlockIndex()] ?? Data.DefaultNonSolid;
    }

    public Block GetBlock(EntityVector position)
    {
        return GetBlock(position.GetGlobalBlock());
    }

    public void SetBlock(EntityVector position, Block set, EntityVector audioPosition)
    {
        var c = position.GetChunk();
        var b = position.GetLocalBlock();
        if (c.Z < 0 || c.Z >= StackSizeZ) return;
        ChunkStack? stack = GetStackSafe(c.XY);
        if (stack == null) return;

        while (!stack.RWLock.TryEnterWriteLock(-1)) { }
        var prev = stack.Chunks[c.Z][b.GetLocalBlockIndex()];
        stack.Chunks[c.Z][b.GetLocalBlockIndex()] = set;
        stack.RWLock.ExitWriteLock();
        stack.IsDirty[c.Z] = true;

        for (int dy = (b.Y <= MaxLightRadius ? -1 : 0); dy <= (b.Y >= ChunkSize - MaxLightRadius ? 1 : 0); dy++)
        {
            for (int dx = (b.X <= MaxLightRadius ? -1 : 0); dx <= (b.X >= ChunkSize - MaxLightRadius ? 1 : 0); dx++)
            {
                ChunkStack? neighbour = GetStackSafe(c.XY + (dx, dy));
                if (neighbour != null) neighbour.IsMeshed = false;
            }
        }

        // On destroy (air placed)
        if (prev != set && set == 0)
        {
            var bd = Data.GetBlockData(prev);
            Audio.Play(bd.AudioOnDestroy, 1.0f, audioPosition, AudioGroup.Block);
            CreateEntity(new ItemDrop(bd.ItemDrop, position.GetGlobalBlock(), CurrentTick));
        }

        // On place (non-air placed)
        if (prev != set && set != 0)
        {
            var bd = Data.GetBlockData(set);
            Audio.Play(bd.AudioOnPlace, 1.0f, audioPosition, AudioGroup.Block);
        }
    }

    /// <summary>
    /// Sets block at position unless entity collides with it 
    /// </summary>
    public bool TrySetBlock(Entity from, EntityVector position, Block set, EntityVector audioPosition)
    {
        if (EntityAABB.Intersect(from.GetBoundingBox(), EntityAABB.FromBlock(position.GetGlobalBlock()))) return false;
        SetBlock(position, set, audioPosition);
        return true;
    }

    // ugly method to avoid some stackMap lookups, needs refactoring
    public void GetCollisionBlocksRange(Vector3i low, Vector3i high, ref List<EntityAABB> solids, ref List<EntityAABB> liquids, ref List<(EntityAABB, Block)> all)
    {
        Vector3i p = default;
        Vector2i prevStackPos = low.XY - 100;
        ChunkStack? prevStack = default;
        Vector3i prevChunkPos = low - 100;
        Span<Block> prevChunk = default;

        low.Z = Math.Max(low.Z, 0);
        high.Z = Math.Min(high.Z, StackSizeZ - 1);

        for (int z = low.Z; z <= high.Z; z++)
        {
            for (int y = low.Y; y <= high.Y; y++)
            {
                for (int x = low.X; x <= high.X; x++)
                {
                    p.X = x;
                    p.Y = y;
                    p.Z = z;

                    Vector3i chunkPos = p.GetChunk();

                    if (chunkPos.XY != prevStackPos)
                    {
                        ChunkStack? stack = GetStackSafe(chunkPos.XY);
                        prevStack = stack;
                        prevStackPos = chunkPos.XY;
                    }

                    if (prevStack == null) continue;

                    if (prevChunkPos != chunkPos)
                    {
                        prevChunk = prevStack.Chunks[chunkPos.Z].Span;
                        prevChunkPos = chunkPos;
                    }

                    var b = prevChunk[p.GetLocalBlockIndex()];
                    var bd = Data.GetBlockData(b);

                    if (b != 0)
                    {
                        all.Add((EntityAABB.FromBlock(x, y, z), b));

                        if (!bd.IsLiquid)
                            solids.Add(EntityAABB.FromBlock(x, y, z));
                        else
                            liquids.Add(EntityAABB.FromBlock(x, y, z));
                    }
                }
            }
        }
    }

    public (Block b, EntityVector hitPos, Vector3i hitNormal) RaySolid(EntityVector position, EntityVector direction)
    {
        static EntityVector IntBounds(EntityVector s, EntityVector ds)
        {
            EntityVector v = default;

            for (int i = 0; i < 3; i++)
            {
                double divs = 1.0 / EntityVector.ToBlockDouble(Math.Max(Math.Abs(ds[i]), 1));
                v[i] = EntityVector.Mul(ds[i] > 0 ? (EntityVector.Ceiling(s[i]) - s[i]) : (s[i] - EntityVector.Floor(s[i])), EntityVector.FromBlock(divs));
            }

            return v;
        }

        var p = EntityVector.Floor(position);
        p += EntityVector.FromBlock(new Vector3(0.001f));

        var step = EntityVector.Sign(direction);

        var rayRadius = EntityVector.FromBlock(8);

        var tmax = IntBounds(position, direction);

        var tdelta = new EntityVector(
            Math.Min(EntityVector.FromBlock(step.X / EntityVector.ToBlockDouble(direction.X == 0 ? 1 : direction.X)), rayRadius),
            Math.Min(EntityVector.FromBlock(step.Y / EntityVector.ToBlockDouble(direction.Y == 0 ? 1 : direction.Y)), rayRadius),
            Math.Min(EntityVector.FromBlock(step.Z / EntityVector.ToBlockDouble(direction.Z == 0 ? 1 : direction.Z)), rayRadius)
        );

        Vector3i hitNormal = default;

        while (true)
        {
            var b = GetBlock(p);
            if (b != 0 && !Data.GetBlockData(b).IsLiquid)
            {
                return (b, p, hitNormal);
            }

            if (tmax.X < tmax.Z)
            {
                if (tmax.X < tmax.Y)
                {
                    if (tmax.X > rayRadius) break;
                    p += EntityVector.UnitX * step.X;
                    tmax.X += tdelta.X;
                    hitNormal = Vector3i.UnitX * Math.Sign(-step.X);
                }
                else
                {
                    if (tmax.Y > rayRadius) break;

                    p += EntityVector.UnitY * step.Y;
                    tmax.Y += tdelta.Y;
                    hitNormal = Vector3i.UnitY * Math.Sign(-step.Y);
                }
            }
            else
            {
                if (tmax.Z < tmax.Y)
                {
                    if (tmax.Z > rayRadius) break;
                    p += EntityVector.UnitZ * step.Z;
                    tmax.Z += tdelta.Z;
                    hitNormal = Vector3i.UnitZ * Math.Sign(-step.Z);
                }
                else
                {
                    if (tmax.Y > rayRadius) break;
                    p += EntityVector.UnitY * step.Y;
                    tmax.Y += tdelta.Y;
                    hitNormal = Vector3i.UnitY * Math.Sign(-step.Y);
                }
            }

        }

        return (0, p, hitNormal);
    }

}
