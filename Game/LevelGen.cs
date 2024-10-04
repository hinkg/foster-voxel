using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Foster.Framework;
using Icaria.Engine.Procedural;

namespace Game;

static class LevelGen
{
    public struct TaskData
    {
        public ChunkStack Stack;
        public int Seed;

        public TaskData(ChunkStack stack, int seed)
        {
            this.Stack = stack;
            this.Seed = seed;
        }
    }

    public struct OutputData
    {
        public Vector2i Position;
        public TimeSpan GenTime;

        public OutputData(Vector2i position, TimeSpan genTime)
        {
            this.Position = position;
            this.GenTime = genTime;
        }
    }

    public class OutputState
    {
        public object Lock;
        public Stack<OutputData> Entries;

        public OutputState()
        {
            Lock = new();
            Entries = [];
        }
    }

    //
    //
    //

    const int ChunkSize = Level.ChunkSize;
    const int ChunkArea = Level.ChunkArea;
    const int ChunkVolume = Level.ChunkVolume;
    const int StackChunkCount = Level.StackChunkCount;
    const int StackSizeZ = Level.StackSizeZ;

    const int KernelSizeXY = 4;
    const int KernelSizeZ = 8;
    const int KernelCountXY_Real = (ChunkSize / KernelSizeXY) * 2;
    const int KernelCountZ_Real = (ChunkSize / KernelSizeZ) * StackChunkCount;
    const int KernelCountXY_Padded = KernelCountXY_Real + 1;
    const int KernelCountZ_Padded = KernelCountZ_Real + 1;

    [InlineArray(KernelCountXY_Padded * 2 * KernelCountXY_Padded * 2 * KernelCountZ_Padded)]
    struct ChunkKernels
    {
        float kernel;
        public readonly float FromXYZ(int x, int y, int z) => this[x + y * KernelCountXY_Padded + z * KernelCountXY_Padded * KernelCountXY_Padded];
    }

    //
    // Utility
    //

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static float ValueMix(float x, float y, float a)
    {
        return x + (y - x) * a;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static float ValueMix3D(float v000, float v100, float v010, float v110, float v001, float v101, float v011, float v111, float x, float y, float z)
    {
        return ValueMix(ValueMix(ValueMix(v000, v100, x), ValueMix(v010, v110, x), y), ValueMix(ValueMix(v001, v101, x), ValueMix(v011, v111, x), y), z);
    }

    unsafe static void SetSafe(Span<Block> data, Vector3i pos, Block b)
    {
        if (pos.X < 0 || pos.X >= ChunkSize * 2) return;
        if (pos.Y < 0 || pos.Y >= ChunkSize * 2) return;
        if (pos.Z < 0 || pos.Z >= StackSizeZ) return;
        data[pos.X + pos.Y * ChunkSize * 2 + pos.Z * ChunkArea * 4] = b;
    }

    unsafe static void Set(Span<Block> data, Vector3i pos, Block b)
    {
        data[pos.X + pos.Y * ChunkSize * 2 + pos.Z * ChunkArea * 4] = b;
    }

    unsafe static void SetIfSolid(Span<Block> data, Vector3i pos, Block b)
    {
        int i = pos.X + pos.Y * ChunkSize * 2 + pos.Z * ChunkArea * 4;
        data[i] = data[i] != 0 ? b : 0;
    }

    unsafe static Block Get(Span<Block> data, Vector3i pos)
    {
        return data[pos.X + pos.Y * ChunkSize * 2 + pos.Z * ChunkArea * 4];
    }

    public static void GenerateColumn(Span<Block> data, Vector3i position, int height, Block b)
    {
        if (position.X < 0 || position.X >= ChunkSize * 2) return;
        if (position.Y < 0 || position.Y >= ChunkSize * 2) return;

        int clampedHeight = Math.Min(position.Z + height, StackSizeZ - 1);

        for (int z = position.Z; z <= clampedHeight; z++, position.Z++)
        {
            Set(data, position, b);
        }
    }

    public static void GenerateCrown(Span<Block> data, Vector3i position, int radius, Block b)
    {
        int r = radius;

        for (int z = -radius + 1; z <= radius; z++)
        {
            if ((position.Z + z) >= StackSizeZ) break;
            GenerateCircle(data, position + (0, 0, z), r, b);
            if (z != -radius && z % 2 == 0) r--;
        }
    }

    static void GenerateCircle(Span<Block> data, Vector3i position, int radius, Block b)
    {
        for (int j = -radius; j <= radius; j++)
        {
            for (int i = -radius; i <= radius; i++)
            {
                if (Math.Abs(i) == radius && Math.Abs(j) == radius) continue;

                int m = (int)Math.Sqrt(i * i + j * j);
                if (m > radius) continue;

                SetSafe(data, position + (i, j, 0), b);
            }
        }
    }

    //
    //
    //

    unsafe public static void Generate(object? _taskData)
    {
        var startTime = Time.Now;

        if (_taskData == null) return;
        (var taskData, var output) = ((TaskData, OutputState))_taskData;
        var stackPosition = taskData.Stack.Position;
        int seed = taskData.Seed;

        //
        // Generate climate maps
        //

        var tempMap = new byte[ChunkSize * 2, ChunkSize * 2];
        var humiMap = new byte[ChunkSize * 2, ChunkSize * 2];

        var climateVarPeriod = new NoisePeriod(64, 64);

        for (int y = 0; y < ChunkSize * 2; y++)
        {
            for (int x = 0; x < ChunkSize * 2; x++)
            {
                var f0 = 0.002f;
                var f1 = 0.1f;
                var o0 = -1.1f;

                float px = (x - ChunkSize / 2) + (stackPosition.X * ChunkSize);
                float py = (y - ChunkSize / 2) + (stackPosition.Y * ChunkSize);
                var (hs, ts) = IcariaNoise.GradientNoiseVec2(px * f0, py * f0, seed);
                var (hv, tv) = IcariaNoise.GradientNoiseVec2(px * f1, py * f1, seed);

                var humi = Math.Clamp((hs * 3.0f) + hv * 0.1, -1.0, 1.0) * 0.5f + 0.5f;
                var temp = Math.Clamp((ts * 3.0f) + tv * 0.1, -1.0, 1.0) * 0.5f + 0.5f;
                humi *= temp;

                tempMap[x, y] = (byte)((temp) * 255);
                humiMap[x, y] = (byte)((humi) * 255);
            }
        }

        //

        var data = new Block[ChunkVolume * StackChunkCount * 4];
        var heightmap = new int[ChunkSize * 2, ChunkSize * 2];
        var waterMap = new bool[ChunkSize * 2, ChunkSize * 2];

        // Shape pass
        GenerateShape(stackPosition, seed, ref data, ref heightmap);

        for (int y = 0; y < ChunkSize * 2; y++)
        {
            for (int x = 0; x < ChunkSize * 2; x++)
            {
                var h = heightmap[x, y];
                heightmap[x, y] = Math.Min(h, StackSizeZ - 4);
            }
        }

        //
        // Water fill 
        //

        for (int y = 0; y < ChunkSize * 2; y++)
        {
            for (int x = 0; x < ChunkSize * 2; x++)
            {
                var h = heightmap[x, y];
                var waterblock = Data.GenerationData.WaterBlock;

                for (int z = 30; z < 70; z++)
                {
                    var p = new Vector3i(x, y, z);
                    if (z <= h && Get(data, p) != 0) continue;

                    waterMap[x, y] = true;
                    Set(data, p, waterblock);
                }
            }
        }

        //
        // Top layer decoration, (grass, beaches, etc) 
        //

        for (int y = 2; y < (ChunkSize * 2) - 2; y++)
        {
            for (int x = 2; x < (ChunkSize * 2) - 2; x++)
            {
                var h = heightmap[x, y];

                bool w = false;

                if (h < 16) continue;

                // Check for neighbouring water blocks

                for (int dy = -2; dy <= 2; dy++)
                {
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        if (waterMap[x + dx, y + dy])
                        {
                            w = true;
                            break;
                        }
                    }

                    if (w) break;
                }

                var tl = Data.GetTopLayerDataSearch(tempMap[x, y], humiMap[x, y]);

                for (int z = 30; z <= h; z++)
                {
                    var p = new Vector3i(x, y, z);

                    var b = Get(data, p);
                    if (b == 0 || b == Data.GenerationData.WaterBlock) continue;

                    var above = Get(data, p + (0, 0, 1));
                    if (above != 0 && above != Data.GenerationData.WaterBlock) continue;

                    var top = (w && z < 72) ? Data.GenerationData.BeachBlock : (z < h ? tl.Subsurface : tl.Surface);
                    var sub = (w && z < 72) ? Data.GenerationData.BeachBlock : tl.Subsurface;

                    Set(data, p, top);

                    for (int i = 1; i <= tl.Depth; i++)
                    {
                        SetIfSolid(data, p - (0, 0, i), sub);
                    }
                }
            }
        }


        //
        // Decorators
        //

        static uint HashTriple32(uint x)
        {
            x ^= x >> 17;
            x *= 0xed5ad4bbU;
            x ^= x >> 11;
            x *= 0xac4c1b51U;
            x ^= x >> 15;
            x *= 0x31848babU;
            x ^= x >> 14;
            return x;
        }

        var occupiedMap = new bool[(ChunkSize * 2), (ChunkSize * 2)];

        List<(Vector3i position, DecoratorData decorator, int hash)> placements = [];
        List<DecoratorData> decorators = [];

        for (int y = 4; y < (ChunkSize * 2) - 4; y += 4)
        {
            for (int x = 4; x < (ChunkSize * 2) - 4; x += 4)
            {
                decorators.Clear();
                Data.GetDecoratorList(tempMap[x, y], humiMap[x, y], ref decorators);

                for (int i = 0; i < decorators.Count; i++)
                {
                    int rx = x + stackPosition.X * ChunkSize - ChunkSize / 2;
                    int ry = y + stackPosition.Y * ChunkSize - ChunkSize / 2;

                    var hash = HashTriple32((uint)rx + (uint)ry * 13) + i * 23;

                    int px = x + (int)(hash % 6);
                    int py = y + (int)(hash % 6);

                    if (hash % 100 < decorators[i].Frequency * 100) continue;

                    if (heightmap[x - 1, y] > heightmap[x, y]) continue;
                    if (heightmap[x + 1, y] > heightmap[x, y]) continue;
                    if (heightmap[x, y - 1] > heightmap[x, y]) continue;
                    if (heightmap[x, y + 1] > heightmap[x, y]) continue;

                    if (occupiedMap[px / 4, py / 4]) continue;
                    else occupiedMap[px / 4, py / 4] = true;

                    placements.Add((new Vector3i(px, py, 6), decorators[i], (int)hash));
                }
            }
        }

        foreach (var placement in placements)
        {
            int tx = placement.position.X;
            int ty = placement.position.Y;

            if (tx < 0 || tx >= ChunkSize * 2) continue;
            if (ty < 0 || ty >= ChunkSize * 2) continue;

            int h = heightmap[tx, ty];
            if (h < 70) continue;

            placement.decorator.Generate(data, new Vector3i(tx, ty, h), placement.hash);
        }

        //
        // Copy working data to final data location
        //

        taskData.Stack.RWLock.EnterWriteLock();

        // Copy blocks
        for (int c = 0; c < StackChunkCount; c++)
        {
            var dstRef = taskData.Stack.Chunks[c].Span;
            var srcRef = MemoryMarshal.CreateSpan(ref data[c * ChunkVolume * 4], ChunkVolume * 4);

            if (taskData.Stack.IsLoadedFromDisk[c]) continue;

            for (int z = 0, i = 0; z < ChunkSize; z++)
            {
                for (int y = 0; y < ChunkSize; y++)
                {
                    for (int x = 0; x < ChunkSize; x++, i++)
                    {
                        dstRef[i] = Get(srcRef, new Vector3i(x + ChunkSize / 2, y + ChunkSize / 2, z));
                    }
                }
            }
        }

        // Copy climate map
        for (int y = 0; y < ChunkSize; y++)
        {
            for (int x = 0; x < ChunkSize; x++)
            {
                int cx = x + ChunkSize / 2;
                int cy = y + ChunkSize / 2;

                taskData.Stack.ClimateMap[x, y] = (tempMap[cx, cy], humiMap[cx, cy]);
            }
        }

        taskData.Stack.RWLock.ExitWriteLock();

        Monitor.Enter(output.Lock);
        output.Entries.Push(new OutputData(taskData.Stack.Position, Time.Now - startTime));
        Monitor.Exit(output.Lock);
    }

    private static unsafe void GenerateShape(Vector2i stackPosition, int seed, ref Block[] data, ref int[,] heightmap)
    {
        var kernels = new ChunkKernels();

        for (int y = 0; y < KernelCountXY_Padded; y++)
        {
            for (int x = 0; x < KernelCountXY_Padded; x++)
            {
                float px = (x * KernelSizeXY) + (stackPosition.X * ChunkSize) - (ChunkSize / 2) - KernelSizeXY;
                float py = (y * KernelSizeXY) + (stackPosition.Y * ChunkSize) - (ChunkSize / 2) - KernelSizeXY;

                for (int z = 0; z < KernelCountZ_Padded; z++)
                {
                    float pz = z * KernelSizeZ;
                    var s = SampleDensity2(seed, px, py, pz);
                    kernels[x + (y * KernelCountXY_Padded) + (z * KernelCountXY_Padded * KernelCountXY_Padded)] = s;
                }
            }
        }

        //
        // Generate solid from interpolating kernels
        //

        static unsafe void InterpolateKernel(ref Block[] data, ref int[,] heightmap, int kz, int ky, int kx, float v000, float v100, float v010, float v110, float v001, float v101, float v011, float v111)
        {
            for (int z = 0; z < KernelSizeZ; z++)
            {
                for (int y = 0; y < KernelSizeXY; y++)
                {
                    for (int x = 0; x < KernelSizeXY; x++)
                    {
                        int i = (x + kx * KernelSizeXY) + (y + ky * KernelSizeXY) * ChunkSize * 2 + (z + kz * KernelSizeZ) * ChunkArea * 4;

                        float u = ((float)x) / (KernelSizeXY);
                        float v = ((float)y) / (KernelSizeXY);
                        float w = ((float)z) / (KernelSizeZ);

                        if (ValueMix3D(v000, v100, v010, v110, v001, v101, v011, v111, u, v, w) > 0.0f)
                        {
                            data[i] = Data.DefaultSolid;
                            heightmap[x + (kx * KernelSizeXY), y + (ky * KernelSizeXY)] = z + kz * KernelSizeZ;
                        }
                    }
                }
            }
        }

        static unsafe void FillKernelBlocks(ref Block[] data, ref int[,] heightmap, int kz, int ky, int kx)
        {
            for (int z = 0; z < KernelSizeZ; z++)
            {
                for (int y = 0; y < KernelSizeXY; y++)
                {
                    for (int x = 0; x < KernelSizeXY; x++)
                    {
                        int i = (x + kx * KernelSizeXY) + (y + ky * KernelSizeXY) * ChunkSize * 2 + (z + kz * KernelSizeZ) * ChunkArea * 4;
                        data[i] = Data.DefaultSolid;
                        heightmap[x + (kx * KernelSizeXY), y + (ky * KernelSizeXY)] = z + kz * KernelSizeZ;
                    }
                }
            }
        }

        for (int kz = 0; kz < KernelCountZ_Real; kz++)
        {
            for (int ky = 1; ky < KernelCountXY_Real; ky++)
            {
                for (int kx = 1; kx < KernelCountXY_Real; kx++)
                {
                    float v000 = kernels.FromXYZ(kx + 0, ky + 0, kz + 0);
                    float v100 = kernels.FromXYZ(kx + 1, ky + 0, kz + 0);
                    float v010 = kernels.FromXYZ(kx + 0, ky + 1, kz + 0);
                    float v110 = kernels.FromXYZ(kx + 1, ky + 1, kz + 0);
                    float v001 = kernels.FromXYZ(kx + 0, ky + 0, kz + 1);
                    float v101 = kernels.FromXYZ(kx + 1, ky + 0, kz + 1);
                    float v011 = kernels.FromXYZ(kx + 0, ky + 1, kz + 1);
                    float v111 = kernels.FromXYZ(kx + 1, ky + 1, kz + 1);

                    if (v000 <= 0.0f && v100 <= 0.0f && v010 <= 0.0f && v110 <= 0.0f && v001 <= 0.0f && v101 <= 0.0f && v011 <= 0.0f && v111 <= 0.0f) continue;

                    if (v000 > 0.0f && v100 > 0.0f && v010 > 0.0f && v110 > 0.0f && v001 > 0.0f && v101 > 0.0f && v011 > 0.0f && v111 > 0.0f)
                    {
                        FillKernelBlocks(ref data, ref heightmap, kz, ky, kx);
                    }
                    else
                    {
                        InterpolateKernel(ref data, ref heightmap, kz, ky, kx, v000, v100, v010, v110, v001, v101, v011, v111);
                    }
                }
            }
        }
    }

    static float Smoothstep(float x, float n)
    {
        return (x < 0.5f) ? 0.5f * MathF.Pow(2.0f * x, n) : 1.0f - 0.5f * MathF.Pow(2.0f * (1.0f - x), n);
    }

    static float SmoothSnap(float t, float m)
    {
        float c = (t > 0.5) ? 1 : 0;
        float s = 1 - c * 2;
        return c + s * MathF.Pow((c + s * t) * 2, m) * 0.5f;
    }

    static float Value3D((float x, float y, float z) p, int seed, in float frequency)
    {
        return IcariaNoise.GradientNoise3D(p.x * frequency, p.y * frequency, p.z * frequency, seed);
    }

    static float Value3D((float x, float y, float z) p, int seed, in (float xy, float z) frequency)
    {
        return IcariaNoise.GradientNoise3D(p.x * frequency.xy, p.y * frequency.xy, p.z * frequency.z, seed);
    }

    static float Value2D((float x, float y) p, int seed, in float frequency)
    {
        return IcariaNoise.GradientNoise(p.x * frequency, p.y * frequency, seed);
    }

    private static unsafe float Clamp(float v, float min, float max) => Math.Clamp(v, min, max);
    private static unsafe float Min(float v, float min) => MathF.Min(v, min);
    private static unsafe float Max(float v, float max) => MathF.Max(v, max);
    private static unsafe float Abs(float v) => MathF.Abs(v);

    private static unsafe float SampleDensity2(int seed, float x, float y, float z)
    {

        var f0 = 0.01f;
        var f1 = 0.02f;
        var f2 = 0.06f;

        var a0 = 48.0f;
        var a1 = 24.0f;
        var a2 = 16.0f;

        var v0 = Value2D((x, y), seed, f0);
        var v1 = Value3D((x, y, z), seed, f1);
        var v2 = Value3D((x, y, z), seed, f2);

        float v = 80 - z;
        v += v0 * a0;
        v += v1 * a1;
        v += v2 * a2;

        return v;
    }
}