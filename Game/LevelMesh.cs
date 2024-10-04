using System.Runtime.InteropServices;
using Foster.Framework;

namespace Game;

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 16)]
struct ChunkVertex : IVertex, IFormattable
{
    // This vertex format used to be 8 bytes wide, but had to make it larger to fit 
    // in temperature and humidity :(

    byte PositionX, PositionY;  // A.X
    short PositionZ;            // A.Y 
    byte PackedFaceData, Block; // A.Z
    byte Light, Occlusion;      // A.W

    byte Temperature, Humidity;          // B.X
    byte ClimateOverlayBlock, Padding1;  // B.Y
    byte Padding2, Padding3;             // B.Z 
    byte Padding4, Padding5;             // B.W

    public ChunkVertex(Vector3i position, Vector2i texCoord, Vector3i normal, byte skyLight, byte blockLight, byte occlusion, byte block, byte temperature, byte humidity, byte climateOverlayBlock)
    {
        unchecked
        {
            PositionX = (byte)position.X;
            PositionY = (byte)position.Y;
            PositionZ = (short)position.Z;
            Light = (byte)((skyLight << 4) | blockLight);
            Occlusion = occlusion;
            PackedFaceData = (byte)((texCoord.X << 7) | (texCoord.Y << 6) | ((normal.X + 1) << 4) | ((normal.Y + 1) << 2) | (normal.Z + 1));
            Block = block;
            Temperature = temperature;
            Humidity = humidity;
            ClimateOverlayBlock = climateOverlayBlock;
        }
    }

    public readonly VertexFormat Format => VertexFormat;

    public static VertexFormat VertexFormat = VertexFormat.Create<ChunkVertex>([
        new VertexFormat.Element(0, VertexType.UShort4, false),
        new VertexFormat.Element(1, VertexType.UShort4, false),
    ]);

    public readonly string ToString(string? format, IFormatProvider? formatProvider)
    {
        return $"[{PositionX}, {PositionY}, {PositionZ}]";
    }
}

static class LevelMesh
{
    public struct OutputData
    {
        public Vector2i StackPosition;
        public int VertexStart, VertexCount;
        public int IndexStart, IndexCount;
        public int OpaqueIndexStart, OpaqueIndexCount;
        public int LiquidIndexStart, LiquidIndexCount;
        public int LowZ, HighZ;

        public TimeSpan MeshingTime;
        public TimeSpan LightingTime;

        public OutputData(Vector2i stackPosition, int vertexStart, int vertexCount, int indexStart, int indexCount,
                          int opaqueIndexStart, int opaqueIndexCount, int liquidIndexStart, int liquidIndexCount,
                          int lowZ, int highZ, TimeSpan meshingTime, TimeSpan lightingTime)
        {
            this.StackPosition = stackPosition;
            this.VertexStart = vertexStart;
            this.VertexCount = vertexCount;
            this.IndexStart = indexStart;
            this.IndexCount = indexCount;
            this.LiquidIndexStart = liquidIndexStart;
            this.OpaqueIndexStart = opaqueIndexStart;
            this.OpaqueIndexCount = opaqueIndexCount;
            this.LiquidIndexStart = liquidIndexStart;
            this.LiquidIndexCount = liquidIndexCount;
            this.LowZ = lowZ;
            this.HighZ = highZ;
            this.MeshingTime = meshingTime;
            this.LightingTime = lightingTime;
        }
    }

    public class OutputState
    {
        public object Lock;
        public Stack<OutputData> Entries;
        public List<ChunkVertex> Vertices;
        public List<Int32> Indices;

        public OutputState()
        {
            Lock = new();
            Entries = [];
            Vertices = [];
            Indices = [];
        }
    }

    public struct TaskData
    {
        public Vector2i Position;
        public ChunkStack[] Stacks;

        public TaskData(Vector2i position, ChunkStack[] stacks)
        {
            this.Position = position;
            this.Stacks = stacks;
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

    const byte SunlightRadius = Level.SunlightRadius;

    static readonly Vector3i[] POSITIONS = [
        new(0, 0, 0),
        new(1, 0, 0),
        new(0, 0, 1),
        new(1, 0, 1), // 0 1 2 3
        new(0, 1, 0),
        new(1, 1, 0),
        new(0, 1, 1),
        new(1, 1, 1), // 4 5 6 7
    ];

    static readonly Vector2i[] UVS = [
        new(0, 0), // 0
        new(1, 0), // 1
        new(0, 1), // 2
        new(1, 1), // 3
        new(0, 1), // 2
        new(1, 0), // 1
    ];

    static readonly Vector3i[] NORMALS = [
        new(-1, 0, 0), // X-
        new(+1, 0, 0), // X+
        new(0, -1, 0), // Y-
        new(0, +1, 0), // Y+
        new(0, 0, -1), // Z-
        new(0, 0, +1), // Z+
    ];

    static readonly int[][] INDICES = [
        [4, 0, 6, 2, 6, 0], // X-
        [1, 5, 3, 7, 3, 5], // X+
        [0, 1, 2, 3, 2, 1], // Y-
        [5, 4, 7, 6, 7, 4], // Y+
        [4, 5, 0, 1, 0, 5], // Z-
        [2, 3, 6, 7, 6, 3], // Z+
    ];

    static readonly int[][] INDICES_FLIPPED = [
        [0, 2, 4, 6, 4, 2], // X-
        [5, 7, 1, 3, 1, 7], // X+
        [1, 3, 0, 2, 0, 3], // Y-
        [4, 6, 5, 7, 5, 6], // Y+
        [5, 1, 4, 0, 4, 1], // Z- 
        [3, 7, 2, 6, 2, 7], // Z+
    ];

    static readonly int[][][] AO_INDICES = [
        [[03, 15, 06], [03, 09, 00], [15, 21, 24], [09, 21, 18]], // X-
        [[05, 11, 02], [05, 17, 08], [11, 23, 20], [17, 23, 26]], // X+
        [[01, 09, 00], [01, 11, 02], [09, 19, 18], [11, 19, 20]], // Y-
        [[07, 17, 08], [07, 15, 06], [17, 25, 26], [15, 25, 24]], // Y+
        [[03, 07, 06], [05, 07, 08], [01, 03, 00], [01, 05, 02]], // Z-
        [[19, 21, 18], [19, 23, 20], [21, 25, 24], [23, 25, 26]], // Z+
    ];

    static readonly int[][][] LIGHT_INDICES = [
        [[03, 15, 06, 12], [03, 09, 00, 12], [15, 21, 24, 12], [09, 21, 18, 12]], // X-
        [[05, 11, 02, 14], [05, 17, 08, 14], [11, 23, 20, 14], [17, 23, 26, 14]], // X+
        [[01, 09, 00, 10], [01, 11, 02, 10], [09, 19, 18, 10], [11, 19, 20, 10]], // Y-
        [[07, 17, 08, 16], [07, 15, 06, 16], [17, 25, 26, 16], [15, 25, 24, 16]], // Y+
        [[03, 07, 06, 04], [05, 07, 08, 04], [01, 03, 00, 04], [01, 05, 02, 04]], // Z-
        [[19, 21, 18, 22], [19, 23, 20, 22], [21, 25, 24, 22], [23, 25, 26, 22]], // Z+
    ];

    unsafe static byte GetAO(bool side1, bool side2, bool corner)
    {
        return (byte)((side1 && side2) ? 0 : (3 - ((side1 ? 1 : 0) + (side2 ? 1 : 0) + (corner ? 1 : 0))));
    }

    unsafe static byte GetLight(bool b_side1, bool b_side2, bool b_corner, byte self, byte side1, byte side2, byte corner, byte center)
    {
        return (byte)Math.Max(Math.Max(b_side1 ? 0 : side1, b_side2 ? 0 : side2), Math.Max(((b_side1 && b_side2) || b_corner) ? 0 : corner, center));
    }

    unsafe static Block GetSafe(in Block[][] data, Vector3i p)
    {
        var ni = p.Wrap(out var diff).GetLocalBlockIndex();
        var nc = diff.GetNeighbourIndex();
        var nb = nc < 0 ? Data.DefaultSolid : (nc >= data.Length ? Data.DefaultNonSolid : data[nc][ni]);
        return nb;
    }

    unsafe static Block GetFast(in Block[][] data, Vector3i p)
    {
        var ni = p.Wrap(out var diff).GetLocalBlockIndex();
        var nc = diff.GetNeighbourIndex();
        return data[nc][ni];
    }

    //

    public unsafe static void Generate(object? _taskData)
    {
        var startTime = Time.Now;

        if (_taskData == null) return;
        (var taskData, var output) = ((TaskData, OutputState))_taskData;

        var data = new Block[3 * 3 * StackChunkCount][];
        var climateMap = new (byte, byte)[ChunkSize, ChunkSize];

        for (int y = -1, i = 0; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++, i++)
            {
                var neighbour = taskData.Stacks[i];

                do { } while (!neighbour.RWLock.TryEnterReadLock(-1));

                for (int c = 0; c < StackChunkCount; c++)
                {
                    data[i + c * 9] = new Block[ChunkVolume];
                    neighbour.Chunks[c].Span.CopyTo(data[i + c * 9]);
                }

                neighbour.RWLock.ExitReadLock();
            }
        }

        for (int y = 0; y < ChunkSize; y++)
        {
            for (int x = 0; x < ChunkSize; x++)
            {
                climateMap[x, y] = taskData.Stacks[4].ClimateMap[x, y];
            }
        }


        var light = new byte[3 * 3 * StackChunkCount * ChunkVolume];

        int maxBlockZ = 0, minAirZ = 0; // Used later to optimize meshing

        var lightingStart = Time.Now;

        CalculateLighting(in data, ref light, ref maxBlockZ, ref minAirZ);

        var lightingTime = Time.Now - lightingStart;

        //
        //
        //

        var verticesOpaque = new List<ChunkVertex>();
        var indicesOpaque = new List<int>();

        var verticesLiquid = new List<ChunkVertex>();
        var indicesLiquid = new List<int>();

        var n = stackalloc bool[27];
        var nsl = stackalloc byte[27];
        var nbl = stackalloc byte[27];
        var fv = stackalloc bool[6];
        var occ = stackalloc byte[4];
        var slgt = stackalloc byte[4];
        var blgt = stackalloc byte[4];

        int lowZ = StackSizeZ;
        int highZ = 0;

        //
        // Opaque pass
        //

        bool hasWater = false;

        maxBlockZ = Math.Min(maxBlockZ + 1, StackSizeZ - 1);
        minAirZ = Math.Max(minAirZ - 1, 0);

        for (int z = minAirZ; z < maxBlockZ; z++)
        {
            for (int y = 0; y < ChunkSize; y++)
            {
                for (int x = 0; x < ChunkSize; x++)
                {
                    var b = data[4 + z / ChunkSize * 9][x + y * ChunkSize + (z % ChunkSize) * ChunkArea];
                    if (b == 0) continue;
                    var bd = Data.GetBlockData(b);

                    if (bd.IsTranslucent)
                    {
                        hasWater = true;
                        continue;
                    }

                    for (int f = 0; f < 6; f++)
                    {
                        var np = NORMALS[f] + (x, y, z);
                        var nb = GetSafe(data, np);
                        var nbd = Data.GetBlockData(nb);
                        fv[f] = nb == 0 || nbd.IsTranslucent;
                    }

                    if (!fv[0] && !fv[1] && !fv[2] && !fv[3] && !fv[4] && !fv[5]) continue;
                    lowZ = Math.Min(lowZ, z);
                    highZ = Math.Max(highZ, z);

                    for (int dz = -1, j = 0; dz <= 1; dz++)
                    {
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++, j++)
                            {
                                var np = new Vector3i(x + dx, y + dy, z + dz);
                                var nb = GetSafe(data, np);
                                var nbd = Data.GetBlockData(nb);

                                n[j] = nb != 0 && !nbd.IsTranslucent;

                                int il = (np.X + ChunkSize) + (np.Y + ChunkSize) * ChunkSize * 3 + (np.Z) * ChunkArea * 9;
                                nsl[j] = (byte)((il >= 0 && il < light.Length) ? (light[il] & 0xF) : SunlightRadius);
                                nbl[j] = (byte)((il >= 0 && il < light.Length) ? (light[il] >> 4) : SunlightRadius);
                            }
                        }
                    }

                    for (int f = 0; f < 6; f++)
                    {
                        if (!fv[f]) continue;

                        occ[0] = GetAO(n[AO_INDICES[f][0][0]], n[AO_INDICES[f][0][1]], n[AO_INDICES[f][0][2]]);
                        occ[1] = GetAO(n[AO_INDICES[f][1][0]], n[AO_INDICES[f][1][1]], n[AO_INDICES[f][1][2]]);
                        occ[2] = GetAO(n[AO_INDICES[f][2][0]], n[AO_INDICES[f][2][1]], n[AO_INDICES[f][2][2]]);
                        occ[3] = GetAO(n[AO_INDICES[f][3][0]], n[AO_INDICES[f][3][1]], n[AO_INDICES[f][3][2]]);

                        slgt[0] = GetLight(n[AO_INDICES[f][0][0]], n[AO_INDICES[f][0][1]], n[AO_INDICES[f][0][2]], nsl[13], nsl[LIGHT_INDICES[f][0][0]], nsl[LIGHT_INDICES[f][0][1]], nsl[LIGHT_INDICES[f][0][2]], nsl[LIGHT_INDICES[f][0][3]]);
                        slgt[1] = GetLight(n[AO_INDICES[f][1][0]], n[AO_INDICES[f][1][1]], n[AO_INDICES[f][1][2]], nsl[13], nsl[LIGHT_INDICES[f][1][0]], nsl[LIGHT_INDICES[f][1][1]], nsl[LIGHT_INDICES[f][1][2]], nsl[LIGHT_INDICES[f][1][3]]);
                        slgt[2] = GetLight(n[AO_INDICES[f][2][0]], n[AO_INDICES[f][2][1]], n[AO_INDICES[f][2][2]], nsl[13], nsl[LIGHT_INDICES[f][2][0]], nsl[LIGHT_INDICES[f][2][1]], nsl[LIGHT_INDICES[f][2][2]], nsl[LIGHT_INDICES[f][2][3]]);
                        slgt[3] = GetLight(n[AO_INDICES[f][3][0]], n[AO_INDICES[f][3][1]], n[AO_INDICES[f][3][2]], nsl[13], nsl[LIGHT_INDICES[f][3][0]], nsl[LIGHT_INDICES[f][3][1]], nsl[LIGHT_INDICES[f][3][2]], nsl[LIGHT_INDICES[f][3][3]]);

                        blgt[0] = GetLight(n[AO_INDICES[f][0][0]], n[AO_INDICES[f][0][1]], n[AO_INDICES[f][0][2]], nbl[13], nbl[LIGHT_INDICES[f][0][0]], nbl[LIGHT_INDICES[f][0][1]], nbl[LIGHT_INDICES[f][0][2]], nbl[LIGHT_INDICES[f][0][3]]);
                        blgt[1] = GetLight(n[AO_INDICES[f][1][0]], n[AO_INDICES[f][1][1]], n[AO_INDICES[f][1][2]], nbl[13], nbl[LIGHT_INDICES[f][1][0]], nbl[LIGHT_INDICES[f][1][1]], nbl[LIGHT_INDICES[f][1][2]], nbl[LIGHT_INDICES[f][1][3]]);
                        blgt[2] = GetLight(n[AO_INDICES[f][2][0]], n[AO_INDICES[f][2][1]], n[AO_INDICES[f][2][2]], nbl[13], nbl[LIGHT_INDICES[f][2][0]], nbl[LIGHT_INDICES[f][2][1]], nbl[LIGHT_INDICES[f][2][2]], nbl[LIGHT_INDICES[f][2][3]]);
                        blgt[3] = GetLight(n[AO_INDICES[f][3][0]], n[AO_INDICES[f][3][1]], n[AO_INDICES[f][3][2]], nbl[13], nbl[LIGHT_INDICES[f][3][0]], nbl[LIGHT_INDICES[f][3][1]], nbl[LIGHT_INDICES[f][3][2]], nbl[LIGHT_INDICES[f][3][3]]);

                        int offset = verticesOpaque.Count;
                        bool flipped = (occ[0] + occ[3]) > (occ[1] + occ[2]);

                        for (int v = 0; v < 4; v++)
                        {
                            int pi = (flipped ? INDICES_FLIPPED[f][v] : INDICES[f][v]);
                            int vi = (flipped ? INDICES_FLIPPED[2][v] : INDICES[2][v]);

                            verticesOpaque.Add(new ChunkVertex(
                                position: POSITIONS[pi] + (x, y, z),
                                texCoord: UVS[vi],
                                normal: NORMALS[f],
                                skyLight: slgt[vi],
                                blockLight: blgt[vi],
                                occlusion: occ[vi],
                                block: (byte)bd.TexturesPacked[f],
                                temperature: climateMap[x, y].Item1,
                                humidity: climateMap[x, y].Item2,
                                climateOverlayBlock: (byte)bd.ClimateOverlayTexturesPacked[f]
                            ));
                        }

                        indicesOpaque.AddRange([offset + 0, offset + 1, offset + 2, offset + 3, offset + 2, offset + 1]);
                    }
                }
            }
        }

        //
        // Liquid pass
        //

        for (int z = minAirZ; z < maxBlockZ; z++)
        {
            if (!hasWater) break;

            for (int y = 0; y < ChunkSize; y++)
            {
                for (int x = 0; x < ChunkSize; x++)
                {
                    Block b = data[4 + z / ChunkSize * 9][x + y * ChunkSize + (z % ChunkSize) * ChunkArea];
                    var bd = Data.GetBlockData(b);
                    if (!bd.IsTranslucent) continue;

                    for (int f = 0; f < 6; f++)
                    {
                        var np = NORMALS[f] + (x, y, z);
                        var nb = GetSafe(data, np);
                        var nbd = Data.GetBlockData(nb);
                        fv[f] = nb == 0 || (nbd.IsTranslucent && nb != b);
                    }

                    if (!fv[0] && !fv[1] && !fv[2] && !fv[3] && !fv[4] && !fv[5]) continue;
                    lowZ = Math.Min(lowZ, z);
                    highZ = Math.Max(highZ, z);

                    for (int dz = -1, j = 0; dz <= 1; dz++)
                    {
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++, j++)
                            {
                                var np = new Vector3i(x + dx, y + dy, z + dz);
                                var nb = GetSafe(data, np);
                                var nbd = Data.GetBlockData(nb);

                                n[j] = nb != 0 && !nbd.IsTranslucent;

                                int il = (np.X + ChunkSize) + (np.Y + ChunkSize) * ChunkSize * 3 + (np.Z) * ChunkArea * 9;
                                nsl[j] = (byte)((il >= 0 && il < light.Length) ? (light[il] & 0xF) : SunlightRadius);
                                nbl[j] = (byte)((il >= 0 && il < light.Length) ? (light[il] >> 4) : SunlightRadius);
                            }
                        }
                    }

                    for (int f = 0; f < 6; f++)
                    {
                        if (!fv[f]) continue;

                        occ[0] = GetAO(n[AO_INDICES[f][0][0]], n[AO_INDICES[f][0][1]], n[AO_INDICES[f][0][2]]);
                        occ[1] = GetAO(n[AO_INDICES[f][1][0]], n[AO_INDICES[f][1][1]], n[AO_INDICES[f][1][2]]);
                        occ[2] = GetAO(n[AO_INDICES[f][2][0]], n[AO_INDICES[f][2][1]], n[AO_INDICES[f][2][2]]);
                        occ[3] = GetAO(n[AO_INDICES[f][3][0]], n[AO_INDICES[f][3][1]], n[AO_INDICES[f][3][2]]);

                        slgt[0] = GetLight(n[AO_INDICES[f][0][0]], n[AO_INDICES[f][0][1]], n[AO_INDICES[f][0][2]], nsl[13], nsl[LIGHT_INDICES[f][0][0]], nsl[LIGHT_INDICES[f][0][1]], nsl[LIGHT_INDICES[f][0][2]], nsl[LIGHT_INDICES[f][0][3]]);
                        slgt[1] = GetLight(n[AO_INDICES[f][1][0]], n[AO_INDICES[f][1][1]], n[AO_INDICES[f][1][2]], nsl[13], nsl[LIGHT_INDICES[f][1][0]], nsl[LIGHT_INDICES[f][1][1]], nsl[LIGHT_INDICES[f][1][2]], nsl[LIGHT_INDICES[f][1][3]]);
                        slgt[2] = GetLight(n[AO_INDICES[f][2][0]], n[AO_INDICES[f][2][1]], n[AO_INDICES[f][2][2]], nsl[13], nsl[LIGHT_INDICES[f][2][0]], nsl[LIGHT_INDICES[f][2][1]], nsl[LIGHT_INDICES[f][2][2]], nsl[LIGHT_INDICES[f][2][3]]);
                        slgt[3] = GetLight(n[AO_INDICES[f][3][0]], n[AO_INDICES[f][3][1]], n[AO_INDICES[f][3][2]], nsl[13], nsl[LIGHT_INDICES[f][3][0]], nsl[LIGHT_INDICES[f][3][1]], nsl[LIGHT_INDICES[f][3][2]], nsl[LIGHT_INDICES[f][3][3]]);

                        blgt[0] = GetLight(n[AO_INDICES[f][0][0]], n[AO_INDICES[f][0][1]], n[AO_INDICES[f][0][2]], nbl[13], nbl[LIGHT_INDICES[f][0][0]], nbl[LIGHT_INDICES[f][0][1]], nbl[LIGHT_INDICES[f][0][2]], nbl[LIGHT_INDICES[f][0][3]]);
                        blgt[1] = GetLight(n[AO_INDICES[f][1][0]], n[AO_INDICES[f][1][1]], n[AO_INDICES[f][1][2]], nbl[13], nbl[LIGHT_INDICES[f][1][0]], nbl[LIGHT_INDICES[f][1][1]], nbl[LIGHT_INDICES[f][1][2]], nbl[LIGHT_INDICES[f][1][3]]);
                        blgt[2] = GetLight(n[AO_INDICES[f][2][0]], n[AO_INDICES[f][2][1]], n[AO_INDICES[f][2][2]], nbl[13], nbl[LIGHT_INDICES[f][2][0]], nbl[LIGHT_INDICES[f][2][1]], nbl[LIGHT_INDICES[f][2][2]], nbl[LIGHT_INDICES[f][2][3]]);
                        blgt[3] = GetLight(n[AO_INDICES[f][3][0]], n[AO_INDICES[f][3][1]], n[AO_INDICES[f][3][2]], nbl[13], nbl[LIGHT_INDICES[f][3][0]], nbl[LIGHT_INDICES[f][3][1]], nbl[LIGHT_INDICES[f][3][2]], nbl[LIGHT_INDICES[f][3][3]]);

                        int offset = verticesOpaque.Count + verticesLiquid.Count;

                        for (int v = 0; v < 4; v++)
                        {
                            verticesLiquid.Add(new ChunkVertex(
                                position: POSITIONS[INDICES[f][v]] + (x, y, z),
                                texCoord: UVS[INDICES[2][v]],
                                normal: NORMALS[f],
                                skyLight: slgt[v],
                                blockLight: blgt[v],
                                occlusion: occ[v],
                                block: (byte)bd.TexturesPacked[f],
                                temperature: climateMap[x, y].Item1,
                                humidity: climateMap[x, y].Item2,
                                climateOverlayBlock: (byte)bd.ClimateOverlayTexturesPacked[f]
                            ));
                        }

                        indicesLiquid.AddRange([offset + 0, offset + 1, offset + 2, offset + 3, offset + 2, offset + 1]);
                    }
                }
            }
        }

        var meshingTime = (Time.Now - startTime) - lightingTime;

        Monitor.Enter(output.Lock);

        output.Entries.Push(new(
            stackPosition: taskData.Position,
            vertexStart: output.Vertices.Count,
            vertexCount: verticesOpaque.Count + verticesLiquid.Count,
            indexStart: output.Indices.Count,
            indexCount: indicesOpaque.Count + indicesLiquid.Count,
            opaqueIndexStart: 0,
            opaqueIndexCount: indicesOpaque.Count,
            liquidIndexStart: indicesOpaque.Count,
            liquidIndexCount: indicesLiquid.Count,
            lowZ: lowZ,
            highZ: highZ,
            lightingTime: lightingTime,
            meshingTime: meshingTime
        ));

        if (verticesOpaque.Count > 0) output.Vertices.AddRange(verticesOpaque);
        if (indicesOpaque.Count > 0) output.Indices.AddRange(indicesOpaque);

        if (verticesLiquid.Count > 0) output.Vertices.AddRange(verticesLiquid);
        if (indicesLiquid.Count > 0) output.Indices.AddRange(indicesLiquid);

        Monitor.Exit(output.Lock);
    }

    public static unsafe void CalculateLighting(in Block[][] data, ref byte[] light, ref int maxBlockZ, ref int minAirZ)
    {
        const byte LR = SunlightRadius;

        var lightMask = new byte[3 * ChunkSize, 3 * ChunkSize];
        var lightSkyQueue = new Queue<Vector3i>();
        var lightBlockQueue = new Queue<Vector3i>();

        for (int y = ChunkSize - LR; y < ChunkSize * 3 - LR; y++)
        {
            for (int x = ChunkSize - LR; x < ChunkSize * 3 - LR; x++)
            {
                lightMask[x, y] = LR;
            }
        }

        for (int z = StackSizeZ - 1; z > 0; z--)
        {
            for (int y = ChunkSize - LR; y < ChunkSize * 3 - LR; y++)
            {
                for (int x = ChunkSize - LR; x < ChunkSize * 3 - LR; x++)
                {
                    var b = GetFast(data, new Vector3i(x - ChunkSize, y - ChunkSize, z));
                    var bd = Data.GetBlockData(b);

                    if (b == 0)
                    {
                        minAirZ = z;
                        continue;
                    }

                    maxBlockZ = Math.Max(maxBlockZ, z);

                    if (bd.IsTranslucent)
                    {
                        lightMask[x, y] -= Math.Min(lightMask[x, y], (byte)1);
                        minAirZ = z;
                    }
                    else
                    {
                        if (bd.IsLight)
                        {
                            lightBlockQueue.Enqueue(new Vector3i(x + 1, y, z));
                            lightBlockQueue.Enqueue(new Vector3i(x - 1, y, z));
                            lightBlockQueue.Enqueue(new Vector3i(x, y + 1, z));
                            lightBlockQueue.Enqueue(new Vector3i(x, y - 1, z));
                            lightBlockQueue.Enqueue(new Vector3i(x, y, z + 1));
                            lightBlockQueue.Enqueue(new Vector3i(x, y, z - 1));
                        }

                        lightMask[x, y] = 0;
                    }
                }
            }

            for (int y = ChunkSize - LR; y < ChunkSize * 3 - LR; y++)
            {
                for (int x = ChunkSize - LR; x < ChunkSize * 3 - LR; x++)
                {
                    var m = lightMask[x, y];

                    if (m != LR)
                    {
                        var ln = Math.Max(
                            Math.Max(lightMask[x + 1, y], lightMask[x, y + 1]),
                            Math.Max(lightMask[x - 1, y], lightMask[x, y - 1])
                        );

                        if (ln != 0)
                        {
                            light[x + y * ChunkSize * 3 + z * ChunkArea * 9] = (byte)(ln - 1);
                            lightSkyQueue.Enqueue(new Vector3i(x, y, z));
                        }
                    }
                    else
                    {
                        light[x + y * ChunkSize * 3 + z * ChunkArea * 9] = m;
                    }
                }
            }
        }

        void sprop(in byte[] lightData, ref Queue<Vector3i> queue, Vector3i p, byte value)
        {
            ref var valueRef = ref lightData[p.X + p.Y * ChunkSize * 3 + p.Z * ChunkArea * 9];

            if (valueRef < value)
            {
                valueRef = value;
                queue.Enqueue(p);
            }
        }

        while (lightSkyQueue.Count > 0)
        {
            var p = lightSkyQueue.Dequeue();

            var l = light[p.X + p.Y * ChunkSize * 3 + p.Z * ChunkArea * 9];
            if (l == 0) continue;

            var b = GetSafe(data, new Vector3i(p.X - ChunkSize, p.Y - ChunkSize, p.Z));
            var bd = Data.GetBlockData(b);
            if (b != 0 && !bd.IsTranslucent) continue;

            if (p.X > ChunkSize - LR) sprop(light, ref lightSkyQueue, p + (-1, 0, 0), (byte)(l - 1));
            if (p.X < ChunkSize * 3 - LR) sprop(light, ref lightSkyQueue, p + (+1, 0, 0), (byte)(l - 1));
            if (p.Y > ChunkSize - LR) sprop(light, ref lightSkyQueue, p + (0, -1, 0), (byte)(l - 1));
            if (p.Y < ChunkSize * 3 - LR) sprop(light, ref lightSkyQueue, p + (0, +1, 0), (byte)(l - 1));
            if (p.Z > 0) sprop(light, ref lightSkyQueue, p + (0, 0, -1), (byte)(l - 1));
            if (p.Z < StackSizeZ - 1) sprop(light, ref lightSkyQueue, p + (0, 0, +1), (byte)(l - 1));
        }

        //
        // Block lighting
        //

        void bprop(in byte[] lightData, ref Queue<Vector3i> queue, Vector3i p, byte value)
        {
            ref var valueRef = ref lightData[p.X + p.Y * ChunkSize * 3 + p.Z * ChunkArea * 9];
            if ((valueRef >> 4) < value)
            {
                valueRef = (byte)((valueRef & 0x0F) | (value << 4));
                queue.Enqueue(p);
            }
        }

        for (int i = 0; i < lightBlockQueue.Count; i++)
        {
            var p = lightBlockQueue.ElementAt(i);
            light[p.X + p.Y * ChunkSize * 3 + p.Z * ChunkArea * 9] |= (byte)((LR) << 4);
        }

        while (lightBlockQueue.Count > 0)
        {
            var p = lightBlockQueue.Dequeue();

            var l = light[p.X + p.Y * ChunkSize * 3 + p.Z * ChunkArea * 9] >> 4;
            if (l == 0) continue;
            l -= 1;

            var b = GetSafe(data, new Vector3i(p.X - ChunkSize, p.Y - ChunkSize, p.Z));
            var bd = Data.GetBlockData(b);
            if (b != 0 && !bd.IsTranslucent) continue;

            if (p.X > ChunkSize - LR) bprop(light, ref lightBlockQueue, p + (-1, 0, 0), (byte)l);
            if (p.X < ChunkSize * 3 - LR) bprop(light, ref lightBlockQueue, p + (+1, 0, 0), (byte)l);
            if (p.Y > ChunkSize - LR) bprop(light, ref lightBlockQueue, p + (0, -1, 0), (byte)l);
            if (p.Y < ChunkSize * 3 - LR) bprop(light, ref lightBlockQueue, p + (0, +1, 0), (byte)l);
            if (p.Z > 0) bprop(light, ref lightBlockQueue, p + (0, 0, -1), (byte)l);
            if (p.Z < StackSizeZ - 1) bprop(light, ref lightBlockQueue, p + (0, 0, +1), (byte)l);
        }
    }
}