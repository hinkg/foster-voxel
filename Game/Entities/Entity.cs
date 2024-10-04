using System.Numerics;
using System.Runtime.InteropServices;
using Foster.Framework;

namespace Game;

abstract class Entity
{
    public EntityVector Position;
    public EntityVector PositionPrev;

    public Vector2 Rotation;
    public Vector2 RotationPrev;

    public EntityVector VelocityPrev;
    public EntityVector Velocity;

    // Currently used to determine audio volume of collisions, 1.0f is default
    public abstract float Mass { get; }

    private float StepTimer;

    // Disabled when 0.0
    public abstract float StepInterval { get; }

    public abstract string AudioOnKill { get; }

    public int Health;
    public abstract int MaxHealth { get; }

    public bool ShouldKill;
    public bool DoGravity;

    public bool IsGrounded;
    public bool IsGroundedPrev;

    public bool HasLanded => !IsGroundedPrev && IsGrounded;
    public bool HasBecomeAirborne => IsGroundedPrev && !IsGrounded;

    public bool IsInLiquid;
    public bool IsInLiquidPrev;
    public bool EnteredLiquid => !IsInLiquidPrev && IsInLiquid;
    public bool ExitedLiquid => IsInLiquidPrev && !IsInLiquid;

    protected Entity()
    {
        DoGravity = true;
        Health = MaxHealth;
    }

    public abstract int ModelID { get; }

    public abstract Vector3 BoundingBox { get; }
    public abstract float EyeHeight { get; }

    public EntityAABB GetBoundingBox() => EntityAABB.FromEntity(this.Position, EntityVector.FromBlock(BoundingBox));
    public EntityAABB GetBoundingBox(float alpha) => EntityAABB.FromEntity(GetInterpolatedPosition(alpha), EntityVector.FromBlock(BoundingBox));

    public (Vector3i, Vector3i) GetBoundingBoxRange()
    {
        return (
            new Vector3i((-Vector3.One).Floor()),
            new Vector3i(BoundingBox.Ceiling())
        );
    }

    public EntityVector GetInterpolatedPosition(float alpha) => PositionPrev + ((Position - PositionPrev) * alpha);
    public Vector2 GetInterpolatedRotation(float alpha) => Rotation + (Rotation - RotationPrev) * alpha;
    public EntityVector GetInterpolatedEyePosition(float alpha) => GetInterpolatedPosition(alpha) + EntityVector.UnitZ * EyeHeight;

    public Vector3 Forward => Vector3.Normalize(new(
                -MathF.Sin(Rotation.Y) * MathF.Abs(MathF.Sin(Rotation.X)),
                 MathF.Cos(Rotation.Y) * MathF.Abs(MathF.Sin(Rotation.X)),
                -MathF.Cos(Rotation.X)));

    private static void TestAxis(int axis, ref EntityVector position, ref EntityVector velocity, EntityAABB boundingBox, in Span<EntityAABB> blocks)
    {
        if (velocity[axis] == 0) return;

        position[axis] += velocity[axis];
        boundingBox.Position[axis] += velocity[axis];

        if (EntityAABB.IntersectList(boundingBox, blocks, out var col))
        {
            // ???
            if (axis == 0 || axis == 1)
                position[axis] = velocity[axis] < 0 ? (col.Position[axis] + col.Size[axis] + boundingBox.Size[axis] / 2) : (col.Position[axis] - boundingBox.Size[axis] / 2);
            else
                position[axis] = velocity[axis] < 0 ? (col.Position[axis] + col.Size[axis]) : (col.Position[axis] - boundingBox.Size[axis]);

            velocity[axis] = velocity[axis] < 0 ? Math.Max(velocity[axis], 0) : Math.Min(velocity[axis], 0);
        }
    }

    public void Simulate(Level level)
    {
        // Gather a list of surrounding blocks
        List<EntityAABB> collisionBlocks = [];
        List<EntityAABB> liquidBlocks = [];
        List<(EntityAABB, Block)> allBlocks = [];

        (Vector3i low, Vector3i high) = this.GetBoundingBoxRange();
        Vector3i blockPos = this.Position.GetGlobalBlock();
        level.GetCollisionBlocksRange(blockPos + low, blockPos + high, ref collisionBlocks, ref liquidBlocks, ref allBlocks);

        var blocks = CollectionsMarshal.AsSpan(collisionBlocks);
        var liquids = CollectionsMarshal.AsSpan(liquidBlocks);

        // Test if entity is grounded
        var isGroundedAABB = this.GetBoundingBox();
        isGroundedAABB.Position.Z -= EntityVector.FromBlock(0.1f);
        isGroundedAABB.Size.Z = EntityVector.FromBlock(0.2f);

        this.IsGroundedPrev = this.IsGrounded;
        this.IsGrounded = EntityAABB.IntersectList(isGroundedAABB, blocks) && Velocity.Z <= 0;

        // Test if entity is touching liquid
        this.IsInLiquidPrev = this.IsInLiquid;
        this.IsInLiquid = EntityAABB.IntersectList(this.GetBoundingBox(), liquids);

        if (this.IsGrounded)
        {
            Block closest = 0;
            long closestDist = long.MaxValue;

            foreach (var (aabb, block) in allBlocks)
            {
                if ((aabb.Position.Z + aabb.Size.Z) > (isGroundedAABB.Position.Z + isGroundedAABB.Position.Z)) continue;
                var newDist = (aabb.Center - isGroundedAABB.Center).ManhattanLength;
                if (newDist < closestDist)
                {
                    closestDist = newDist;
                    closest = block;
                }
            }

            var audio = Data.GetBlockData(closest).AudioOnStep;

            // Landing sound

            if (this.HasLanded && !this.IsInLiquid)
            {
                StepTimer = 0;
                Audio.Play(audio, 1.3f, isGroundedAABB.Center * Mass, AudioGroup.Step);
            }

            // Step sound

            if (StepInterval != 0.0f && (Math.Abs(Velocity.X) > 100000 || Math.Abs(Velocity.Y) > 100000))
            {
                StepTimer += 0.25f * Velocity.ToVector3().XY().Length();

                if (StepTimer > StepInterval)
                {
                    StepTimer = 0;
                    Audio.Play(audio, 1.0f, isGroundedAABB.Center * Mass, AudioGroup.Step);
                }
            }
        }

        // Apply gravity
        if (this.DoGravity)
        {
            this.Velocity.Z -= EntityVector.FromBlock(0.0024f * (this.IsInLiquid ? 0.6f : 1.0f));
        }

        // Test collision
        // This is suboptimal
        TestAxis(0, ref this.Position, ref this.Velocity, this.GetBoundingBox(), blocks); // X
        TestAxis(1, ref this.Position, ref this.Velocity, this.GetBoundingBox(), blocks); // Y
        TestAxis(2, ref this.Position, ref this.Velocity, this.GetBoundingBox(), blocks); // Z

        // Apply drag
        const float airDrag = 0.994f;
        const float groundDrag = 0.86f;
        const float liquidDrag = 0.92f;

        this.Velocity *= EntityVector.FromBlock(
            this.IsInLiquid ? liquidDrag : ((this.IsGrounded || !this.DoGravity) ? groundDrag : airDrag),
            this.IsInLiquid ? liquidDrag : ((this.IsGrounded || !this.DoGravity) ? groundDrag : airDrag),
            this.IsInLiquid ? liquidDrag : (!this.DoGravity ? groundDrag : airDrag)
        );
    }

    public abstract void Update(Level level, Span<Entity> entities);
    public abstract void Render(ref List<EntityRenderBlock> renderList, int tick);
}

struct EntityRenderBlock
{
    public EntityVector position;
    public Matrix4x4 localTransform;
    public int MeshIndexStart;
    public int MeshIndexCount;

    public EntityRenderBlock(EntityVector position, Vector3 offset, Vector3 rotation, Vector3 size, (int indexStart, int indexCount) mesh)
    {
        this.position = position;
        this.MeshIndexStart = mesh.indexStart;
        this.MeshIndexCount = mesh.indexCount;
        this.localTransform = Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(offset) * Matrix4x4.CreateRotationZ(rotation.Z);
    }
}

struct EntityModelData
{
    public int IndexStart;
    public int IndexCount;

    public EntityModelData(int indexStart, int indexCount)
    {
        IndexStart = indexStart;
        IndexCount = indexCount;
    }

    public EntityModelData((int Start, int Count) index)
    {
        IndexStart = index.Start;
        IndexCount = index.Count;
    }
}

static partial class Data
{
    static EntityModelData[] entityModels;
    static Mesh entityMesh;
    static Dictionary<Item, int> itemModelMap = [];

    static (List<MeshVertex>, List<int>) CreateEntityBlockMesh(Vector2i[] textures)
    {
        List<MeshVertex> vertices = [];
        List<int> indices = [];

        Vector3[] POSITIONS = [
            new(0, 0, 0),
            new(1, 0, 0),
            new(0, 0, 1),
            new(1, 0, 1), // 0 1 2 3
            new(0, 1, 0),
            new(1, 1, 0),
            new(0, 1, 1),
            new(1, 1, 1), // 4 5 6 7
        ];

        Vector2[] UVS = [
            new(0, 1), // 0
            new(1, 1), // 1
            new(0, 0), // 2
            new(1, 0), // 3
            new(0, 0), // 2
            new(1, 1), // 1
        ];

        Vector3[] NORMALS = [
            new(-1, 0, 0), // X-
            new(+1, 0, 0), // X+
            new(0, -1, 0), // Y-
            new(0, +1, 0), // Y+
            new(0, 0, -1), // Z-
            new(0, 0, +1), // Z+
        ];

        int[][] INDICES = [
            [4, 0, 6, 2, 6, 0], // X-
            [1, 5, 3, 7, 3, 5], // X+
            [0, 1, 2, 3, 2, 1], // Y-
            [5, 4, 7, 6, 7, 4], // Y+
            [4, 5, 0, 1, 0, 5], // Z-
            [2, 3, 6, 7, 6, 3], // Z+
        ];

        for (int f = 0; f < 6; f++)
        {
            var low = (new Vector2(textures[f].X, textures[f].Y) / 16) + (Vector2.One / 64);
            var size = Vector2.One / 32;

            for (int v = 0; v < 4; v++)
            {
                vertices.Add(new(
                    POSITIONS[INDICES[f][v]] - new Vector3(0.5f, 0.5f, 0.0f),
                    low + (size * UVS[INDICES[2][v]]),
                    NORMALS[f]
                ));
            }

            indices.AddRange([f * 4 + 0, f * 4 + 1, f * 4 + 2, f * 4 + 3, f * 4 + 2, f * 4 + 1]);
        }

        return (vertices, indices);
    }

    public static void MakeEntityData()
    {
        var modelData = new List<EntityModelData>();
        modelData.Add(new EntityModelData(0, 0));

        var vertices = new List<MeshVertex>();
        var indices = new List<int>();

        (int, int) AddToMesh((List<MeshVertex> vertices, List<int> indices) newData)
        {
            var start = indices.Count;

            for (int i = 0; i < newData.indices.Count; i++)
            {
                newData.indices[i] += vertices.Count;
            }

            vertices.AddRange(newData.vertices);
            indices.AddRange(newData.indices);

            return (start, newData.indices.Count);
        }

        for (int i = 0; i < itemData.Length; i++)
        {
            var data = GetItemData((Item)i);

            if (!BlockMap.ContainsKey(data.DataName))
            {
                itemModelMap[(Item)i] = 0;
                continue;
            }

            itemModelMap[(Item)i] = modelData.Count;

            var blockData = GetBlockData(GetBlock(data.DataName));
            modelData.Add(new EntityModelData(AddToMesh(CreateEntityBlockMesh(blockData.Textures))));
        }

        entityModels = modelData.ToArray();

        entityMesh = new();
        entityMesh.SetVertices<MeshVertex>(CollectionsMarshal.AsSpan(vertices));
        entityMesh.SetIndices<int>(CollectionsMarshal.AsSpan(indices));
    }

    public static EntityModelData GetItemModelData(Item item) => entityModels[itemModelMap[item]];
    public static int GetItemModelID(Item item) => itemModelMap[item];
    public static Mesh GetEntityMesh() => entityMesh;
}