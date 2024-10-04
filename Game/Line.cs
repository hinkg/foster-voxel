using System.Numerics;
using System.Runtime.InteropServices;
using Foster.Framework;

namespace Game;

static class Line
{
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 16)]
    struct LineVertex : IVertex
    {
        Vector3 Position;
        VecByte4 Color;

        public LineVertex(Vector3 position, Color color)
        {
            Position = position;
            Color = new VecByte4(color.R, color.G, color.B, color.A);
        }

        public readonly VertexFormat Format => VertexFormat;

        public static VertexFormat VertexFormat = VertexFormat.Create<LineVertex>([
            new VertexFormat.Element(0, VertexType.Float3, false),
            new VertexFormat.Element(1, VertexType.UByte4, true),
        ]);
    }

    private static List<(EntityVector a, EntityVector b, Color c)> lineList = [];
    private static List<(EntityVector a, EntityVector b, Color c)> lineListFixed = [];
    private static bool insideFixedUpdate = false;

    private static Mesh? mesh;
    private static Material? material;

    public static void NewFrame()
    {
        lineList.Clear();

        if (mesh == null) mesh = new();
        if (material == null) material = Game.MakeMaterial(Game.ShaderInfo[Renderers.OpenGL]["LevelLine"]);
    }

    public static void NewFixedFrame()
    {
        lineListFixed.Clear();
        insideFixedUpdate = true;
    }

    public static void EndFixedFrame()
    {
        insideFixedUpdate = false;
    }

    public static void Push(EntityVector a, EntityVector b, Color color)
    {
        if (insideFixedUpdate) lineListFixed.Add((a, b, color));
        else lineList.Add((a, b, color));
    }

    public static void Push((long x, long y, long z) a, (long x, long y, long z) b, Color color)
    {
        Push(new EntityVector(a.x, a.y, a.z), new EntityVector(b.x, b.y, b.z), color);
    }

    public static void PushCube(EntityVector a, Vector3 size, Color color)
    {
        var b = a + size;

        Push((a.X, a.Y, a.Z), (b.X, a.Y, a.Z), color);
        Push((a.X, a.Y, a.Z), (a.X, b.Y, a.Z), color);
        Push((b.X, b.Y, a.Z), (b.X, a.Y, a.Z), color);
        Push((b.X, b.Y, a.Z), (a.X, b.Y, a.Z), color);

        Push((a.X, a.Y, b.Z), (b.X, a.Y, b.Z), color);
        Push((a.X, a.Y, b.Z), (a.X, b.Y, b.Z), color);
        Push((b.X, b.Y, b.Z), (b.X, a.Y, b.Z), color);
        Push((b.X, b.Y, b.Z), (a.X, b.Y, b.Z), color);

        Push((a.X, a.Y, a.Z), (a.X, a.Y, b.Z), color);
        Push((a.X, b.Y, a.Z), (a.X, b.Y, b.Z), color);
        Push((b.X, a.Y, a.Z), (b.X, a.Y, b.Z), color);
        Push((b.X, b.Y, a.Z), (b.X, b.Y, b.Z), color);
    }

    public static void PushCubeFace(EntityVector a, Vector3 size, int face, Color color)
    {

        var b = a + size;

        switch (face)
        {
            case 0: // X-
                Push((a.X, a.Y, a.Z), (a.X, b.Y, a.Z), color);
                Push((a.X, a.Y, b.Z), (a.X, b.Y, b.Z), color);
                Push((a.X, a.Y, a.Z), (a.X, a.Y, b.Z), color);
                Push((a.X, b.Y, a.Z), (a.X, b.Y, b.Z), color);
                break;
            case 1: // X+
                Push((b.X, a.Y, a.Z), (b.X, b.Y, a.Z), color);
                Push((b.X, a.Y, b.Z), (b.X, b.Y, b.Z), color);
                Push((b.X, a.Y, a.Z), (b.X, a.Y, b.Z), color);
                Push((b.X, b.Y, a.Z), (b.X, b.Y, b.Z), color);
                break;
            case 2: // Y-
                Push((a.X, a.Y, a.Z), (b.X, a.Y, a.Z), color);
                Push((a.X, a.Y, b.Z), (b.X, a.Y, b.Z), color);
                Push((a.X, a.Y, a.Z), (a.X, a.Y, b.Z), color);
                Push((b.X, a.Y, a.Z), (b.X, a.Y, b.Z), color);
                break;
            case 3: // Y+
                Push((a.X, b.Y, a.Z), (b.X, b.Y, a.Z), color);
                Push((a.X, b.Y, b.Z), (b.X, b.Y, b.Z), color);
                Push((a.X, b.Y, a.Z), (a.X, b.Y, b.Z), color);
                Push((b.X, b.Y, a.Z), (b.X, b.Y, b.Z), color);
                break;
            case 4: // Z-
                Push((a.X, a.Y, a.Z), (b.X, a.Y, a.Z), color);
                Push((a.X, a.Y, a.Z), (a.X, b.Y, a.Z), color);
                Push((b.X, b.Y, a.Z), (b.X, a.Y, a.Z), color);
                Push((b.X, b.Y, a.Z), (a.X, b.Y, a.Z), color);
                break;
            case 5: // Z+
                Push((a.X, a.Y, b.Z), (b.X, a.Y, b.Z), color);
                Push((a.X, a.Y, b.Z), (a.X, b.Y, b.Z), color);
                Push((b.X, b.Y, b.Z), (b.X, a.Y, b.Z), color);
                Push((b.X, b.Y, b.Z), (a.X, b.Y, b.Z), color);
                break;
            default:
                break;
        }
    }

    public static void PushCubeNormal(EntityVector a, Vector3 size, Vector3i normal, Color color)
    {
        if (normal.X == -1) PushCubeFace(a, size, 0, color);
        if (normal.X == +1) PushCubeFace(a, size, 1, color);
        if (normal.Y == -1) PushCubeFace(a, size, 2, color);
        if (normal.Y == +1) PushCubeFace(a, size, 3, color);
        if (normal.Z == -1) PushCubeFace(a, size, 4, color);
        if (normal.Z == +1) PushCubeFace(a, size, 5, color);
    }

    public static void Draw(Target? target, Matrix4x4 localViewMatrix, EntityVector eyePosition)
    {
        if (mesh == null || material == null) return;

        List<LineVertex> vertices = [];
        List<int> indices = [];

        Vector3 LineWidth = new Vector3(1 * (1.0f / App.HeightInPixels)) * 2;
        void MakeLineQuad((EntityVector a, EntityVector b, Color c) line)
        {
            var a = (line.a - eyePosition).ToVector3();
            var b = (line.b - eyePosition).ToVector3();
            var r = Vector3.Cross(a, b).Normalized();

            int o = vertices.Count;
            indices.AddRange([o + 0, o + 1, o + 2, o + 3, o + 2, o + 1]);

            float aw = a.Length();
            float bw = b.Length();

            var z_offset_a = a * LineWidth * aw * 4;
            var z_offset_b = b * LineWidth * bw * 4;

            vertices.Add(new LineVertex(a - (r * LineWidth * aw) - z_offset_a, line.c));
            vertices.Add(new LineVertex(a + (r * LineWidth * aw) - z_offset_a, line.c));
            vertices.Add(new LineVertex(b - (r * LineWidth * bw) - z_offset_b, line.c));
            vertices.Add(new LineVertex(b + (r * LineWidth * bw) - z_offset_b, line.c));
        }

        foreach (var line in lineList) MakeLineQuad(line);
        foreach (var line in lineListFixed) MakeLineQuad(line);

        if (mesh.VertexCount <= vertices.Count) mesh.SetVertices(vertices.Count + 1024, LineVertex.VertexFormat);
        if (mesh.IndexCount <= indices.Count) mesh.SetIndices<int>(indices.Count + 1024, IndexFormat.ThirtyTwo);

        mesh.SetSubVertices<LineVertex>(0, CollectionsMarshal.AsSpan(vertices));
        mesh.SetSubIndices<int>(0, CollectionsMarshal.AsSpan(indices));

        material.Set("u_localViewMatrix", localViewMatrix);

        new DrawCommand()
        {
            Target = target,
            Material = material,
            DepthCompare = DepthCompare.LessOrEqual,
            DepthMask = true,
            CullMode = CullMode.None,
            Mesh = mesh,
            MeshIndexStart = 0,
            MeshIndexCount = indices.Count,
        }.Submit();
    }
}