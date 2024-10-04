using System.Numerics;
using System.Runtime.InteropServices;
using Foster.Framework;

namespace Game;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct VecByte4(byte x, byte y, byte z, byte w)
{
    public byte X = x;
    public byte Y = y;
    public byte Z = z;
    public byte W = w;

    public static VecByte4 Zero => new(0, 0, 0, 0);
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct MeshVertex : IVertex
{
    public Vector3 Pos;
    public Vector2 UV;
    public Vector3 Normal;
    public Color Color;
    public VecByte4 extra;

    public MeshVertex(Vector3 inPosition, Vector2 inUV, Vector3 inNormal, Color inColor, VecByte4 inExtra)
    {
        Pos = inPosition;
        UV = inUV;
        Normal = inNormal;
        Color = inColor;
        extra = inExtra;
    }

    public MeshVertex(Vector3 inPosition, Vector2 inUV, Vector3 inNormal)
    {
        Pos = inPosition;
        UV = inUV;
        Normal = inNormal;
    }

    public MeshVertex(Vector3 inPosition, Vector2 inUV)
    {
        Pos = inPosition;
        UV = inUV;
    }

    public readonly VertexFormat Format => VertexFormat;

    public static VertexFormat VertexFormat = VertexFormat.Create<MeshVertex>([
        new VertexFormat.Element(0, VertexType.Float3, false),
        new VertexFormat.Element(1, VertexType.Float2, false),
        new VertexFormat.Element(2, VertexType.Float3, false),
        new VertexFormat.Element(3, VertexType.UByte4, true),
        new VertexFormat.Element(4, VertexType.UByte4, false),
    ]);
}

class Model
{
    public Mesh mesh;
    public List<MeshVertex> vertices;
    public List<int> indices;

    public Image? image;
    public Texture? texture;

    public Model(string path)
    {
        var gltf_model = SharpGLTF.Schema2.ModelRoot.Load(Path.Join(path));

        mesh = new();
        vertices = [];
        indices = [];

        if (gltf_model.LogicalImages.Count > 0)
        {
            using var stream = new MemoryStream(gltf_model.LogicalImages[0].Content.Content.ToArray());
            this.image = new(stream);
            this.texture = new(image);
        }

        foreach (var primitive in gltf_model.LogicalMeshes[0].Primitives)
        {
            var verts = primitive.GetVertexAccessor("POSITION").AsVector3Array();
            var uvs = primitive.GetVertexAccessor("TEXCOORD_0").AsVector2Array();
            var normals = primitive.GetVertexAccessor("NORMAL").AsVector3Array();
            var colors = primitive.GetVertexAccessor("COLOR_0")?.AsColorArray();

            for (int i = 0; i < verts.Count; i++) 
                vertices.Add(new MeshVertex(verts[i], uvs[i], normals[i], colors != null ? colors[i] : Color.White, VecByte4.Zero));
            
            foreach (var index in primitive.GetIndices())
                indices.Add((int)index);
        }

        mesh.SetVertices<MeshVertex>(CollectionsMarshal.AsSpan(vertices));
        mesh.SetIndices<int>(CollectionsMarshal.AsSpan(indices));
    }

    public void Draw(Target? target, Material material, Matrix4x4 matrix, CullMode cullMode = CullMode.Back)
    {
        material.Set("u_modelMatrix", matrix);
        if (texture != null) material.Set("u_texture0", texture);

        var call = new DrawCommand(target, mesh, material)
        {
            DepthCompare = DepthCompare.Less,
            DepthMask = true,
            CullMode = cullMode,
            MeshIndexStart = 0,
            MeshIndexCount = mesh.IndexCount
        };

        call.Submit();
    }
}