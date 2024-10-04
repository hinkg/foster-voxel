using System.Numerics;
using System.Runtime.InteropServices;
using Foster.Framework;
using Icaria.Engine.Procedural;

namespace Game;

class Sky
{
    private Mesh quad;
    private Material atmosphereMaterial;
    private Target atmosphereRenderTarget;
    private TextureSampler atmosphereSampler;
    private Material objectMaterial;
    private Model skyboxSphere;
    private Material skyboxMaterial;

    // Clouds

    public byte[] CloudImage;
    public Texture CloudTexture;
    public TextureSampler CloudTextureWrapSampler;
    public Mesh CloudMesh;
    public Material CloudMaterial;

    private const int CloudPixelSize = 8;
    private const int CloudImageSize = 256;

    public const int CloudPeriod = 900_00;

    // Stars

    public Mesh starMesh;
    public Material starMaterial;

    // Sun

    // Full cycle (day and night) is 1440 seconds, 24 minutes
    public const int SunOrbitPeriod = 1440_00;
    public const int SunInitialOrbit = 0;
    public const float SunOrbitTilt = 0.5f;
    public const float SunOrbitRotation = 0.0f;

    public static float GetSunOrbitProgress(int currentTick, float tickAlpha)
    {
        return (((SunInitialOrbit + currentTick) % SunOrbitPeriod) + tickAlpha) / SunOrbitPeriod;
    }

    public static Vector3 GetSunDirection(int currentTick, float tickAlpha)
    {
        float progress = GetSunOrbitProgress(currentTick, tickAlpha);
        return Vector3.Transform(new Vector3(0.0f, 0.0f, -1.0f), GetOrbitTransform(SunOrbitTilt, SunOrbitRotation, progress));
    }

    public static Matrix4x4 GetSunTransform(int currentTick, float tickAlpha)
    {
        float progress = GetSunOrbitProgress(currentTick, tickAlpha);
        return GetOrbitTransform(SunOrbitTilt, SunOrbitRotation, progress);
    }

    // Moon

    public const int MoonOrbitPeriod = 2000_00;
    public const int MoonInitialOrbit = 1525_00;
    public const float MoonOrbitTilt = 1.0f;
    public const float MoonOrbitRotation = 1.0f;

    public static float GetMoonOrbitProgress(int currentTick, float tickAlpha)
    {
        return (((MoonInitialOrbit + currentTick) % MoonOrbitPeriod) + tickAlpha) / MoonOrbitPeriod;
    }

    public static Vector3 GetMoonDirection(int currentTick, float tickAlpha)
    {
        float progress = GetMoonOrbitProgress(currentTick, tickAlpha);
        return Vector3.Transform(new Vector3(0.0f, 0.0f, -1.0f), GetOrbitTransform(MoonOrbitTilt, MoonOrbitRotation, progress));
    }

    //

    public static Matrix4x4 GetOrbitTransform(float tilt, float rotation, float progress)
    {
        return Matrix4x4.CreateRotationY(-(progress * MathF.Tau + MathF.PI / 2.0f)) * Matrix4x4.CreateRotationX(tilt) * Matrix4x4.CreateRotationY(rotation);
    }

    //

    public Sky()
    {
        quad = new Mesh();

        quad.SetVertices<MeshVertex>([
            new(new(-1.0f, -1.0f, 0.0f), new(0.0f, 0.0f), Vector3.Zero, Color.White, VecByte4.Zero),
            new(new(+1.0f, -1.0f, 0.0f), new(1.0f, 0.0f), Vector3.Zero, Color.White, VecByte4.Zero),
            new(new(-1.0f, +1.0f, 0.0f), new(0.0f, 1.0f), Vector3.Zero, Color.White, VecByte4.Zero),
            new(new(+1.0f, +1.0f, 0.0f), new(1.0f, 1.0f), Vector3.Zero, Color.White, VecByte4.Zero),
        ]);

        quad.SetIndices([0, 1, 2, 3, 2, 1]);

        objectMaterial = Game.MakeMaterial(Game.ShaderInfo[Renderers.OpenGL]["SkyObject"]);

        skyboxSphere = new Model("Assets/Cube.glb");
        skyboxMaterial = Game.MakeMaterial(Game.ShaderInfo[Renderers.OpenGL]["SkyBox"]);

        atmosphereMaterial = Game.MakeMaterial(Game.ShaderInfo[Renderers.OpenGL]["SkyAtmosphere"]);
        atmosphereRenderTarget = new Target(512, 256, [TextureFormat.R8G8B8A8]);

        atmosphereSampler = new TextureSampler(TextureFilter.Linear, TextureWrap.Repeat, TextureWrap.ClampToEdge);

        CloudMaterial = Game.MakeMaterial(Game.ShaderInfo[Renderers.OpenGL]["SkyClouds"]);
        CloudImage = new byte[CloudImageSize * CloudImageSize];
        CloudTextureWrapSampler = new TextureSampler(TextureFilter.Linear, TextureWrap.Repeat, TextureWrap.Repeat);

        starMaterial = Game.MakeMaterial(Game.ShaderInfo[Renderers.OpenGL]["SkyStars"]);
    }

    public Texture GetCloudTexture()
    {
        return CloudTexture;
    }

    public Vector2 GetCloudOffset(EntityVector eyePosition, int currentTick, float tickAlpha)
    {
        var scrollY = EntityVector.FromBlock(((currentTick % CloudPeriod) + tickAlpha) / CloudPeriod * CloudImageSize * CloudPixelSize);
        float localX = EntityVector.ToBlockFloat(MathExt.Mod(eyePosition.X, EntityVector.FromBlock(CloudImageSize * CloudPixelSize)));
        float localY = EntityVector.ToBlockFloat(MathExt.Mod(eyePosition.Y + scrollY, EntityVector.FromBlock(CloudImageSize * CloudPixelSize)));
        return new Vector2(localX, localY);
    }

    public Texture GetAtmosphereTexture()
    {
        return atmosphereRenderTarget.Attachments[0];
    }

    public void ReloadResources()
    {
        try
        {
            var newMaterial = Game.MakeMaterial(Game.ShaderInfo[Renderers.OpenGL]["SkyAtmosphere"]);
            atmosphereMaterial.Clear();
            atmosphereMaterial = newMaterial;

            newMaterial = Game.MakeMaterial(Game.ShaderInfo[Renderers.OpenGL]["SkyBox"]);
            skyboxMaterial.Clear();
            skyboxMaterial = newMaterial;

            newMaterial = Game.MakeMaterial(Game.ShaderInfo[Renderers.OpenGL]["SkyObject"]);
            objectMaterial.Clear();
            objectMaterial = newMaterial;

            newMaterial = Game.MakeMaterial(Game.ShaderInfo[Renderers.OpenGL]["SkyClouds"]);
            CloudMaterial.Clear();
            CloudMaterial = newMaterial;

            newMaterial = Game.MakeMaterial(Game.ShaderInfo[Renderers.OpenGL]["SkyStars"]);
            starMaterial.Clear();
            starMaterial = newMaterial;
        }
        catch
        {
            return;
        }
    }

    public void Render(Target? target, Matrix4x4 localViewMatrix, EntityVector eyePosition, int currentTick)
    {
        var sunDir = GetSunDirection(currentTick, Time.FixedAlpha);
        var moonDir = GetMoonDirection(currentTick, Time.FixedAlpha);

        // Draw sky texture

        atmosphereMaterial.Set("u_sunDirection", sunDir);
        atmosphereMaterial.Set("u_moonDirection", moonDir);

        new DrawCommand(atmosphereRenderTarget, quad, atmosphereMaterial)
        {
            DepthCompare = DepthCompare.Always,
            DepthMask = false,
            CullMode = CullMode.None,
            MeshIndexStart = 0,
            MeshIndexCount = quad.IndexCount
        }.Submit();

        // Draw sky to main framebuffer

        skyboxMaterial.Set("u_viewMatrix", localViewMatrix);
        skyboxMaterial.Set("u_modelMatrix", Matrix4x4.Identity);

        skyboxMaterial.Set("u_skyTexture", atmosphereRenderTarget.Attachments[0]);
        skyboxMaterial.Set("u_skyTexture_sampler", atmosphereSampler);

        new DrawCommand(target, skyboxSphere.mesh, skyboxMaterial)
        {
            DepthCompare = DepthCompare.Always,
            DepthMask = false,
            CullMode = CullMode.Front,
            MeshIndexStart = 0,
            MeshIndexCount = skyboxSphere.mesh.IndexCount,
        }.Submit();

        // Draw stars

        var starTransform = GetOrbitTransform(SunOrbitTilt, SunOrbitRotation, GetSunOrbitProgress(currentTick, Time.FixedAlpha));
        starMaterial.Set("u_localViewMatrix", localViewMatrix);
        starMaterial.Set("u_modelMatrix", starTransform);
        starMaterial.Set("u_skyTexture", atmosphereRenderTarget.Attachments[0]);

        new DrawCommand(target, starMesh, starMaterial)
        {
            DepthCompare = DepthCompare.Always,
            DepthMask = false,
            CullMode = CullMode.None,
            MeshIndexStart = 0,
            MeshIndexCount = starMesh.IndexCount,
            BlendMode = BlendMode.Add
        }.Submit();

        //

        var sunTransform = Matrix4x4.CreateScale(0.075f)
            * Matrix4x4.CreateTranslation(0.0f, 0.0f, -1.0f)
            * GetOrbitTransform(SunOrbitTilt, SunOrbitRotation, GetSunOrbitProgress(currentTick, Time.FixedAlpha));

        var moonTransform = Matrix4x4.CreateScale(0.075f)
            * Matrix4x4.CreateTranslation(0.0f, 0.0f, -1.0f)
            * GetOrbitTransform(MoonOrbitTilt, MoonOrbitRotation, GetMoonOrbitProgress(currentTick, Time.FixedAlpha));


        objectMaterial.Set("u_viewMatrix", localViewMatrix);
        objectMaterial.Set("u_skyTexture", atmosphereRenderTarget.Attachments[0]);

        // Draw sun
        DrawObject(target, sunDir, -sunDir, sunTransform);

        // Draw moon
        DrawObject(target, sunDir, moonDir, moonTransform);

        // Draw clouds
        DrawClouds(target, localViewMatrix, eyePosition, currentTick);

        void DrawObject(Target? target, Vector3 dir, Vector3 selfDir, Matrix4x4 transform, DepthCompare depthMode = DepthCompare.Always, float alpha = 1.0f)
        {
            objectMaterial.Set("u_modelMatrix", transform);
            objectMaterial.Set("u_lightDir", dir);
            objectMaterial.Set("u_selfDir", selfDir);
            objectMaterial.Set("u_alpha", alpha);

            new DrawCommand(target, quad, objectMaterial)
            {
                DepthCompare = depthMode,
                DepthMask = false,
                CullMode = CullMode.None,
                MeshIndexStart = 0,
                MeshIndexCount = quad.IndexCount,
                BlendMode = BlendMode.Premultiply,
            }.Submit();
        }
    }

    const int StarCount = 512;
    const float StarSize = 0.004f;
    const float StarSizeVariation = 0.003f;

    public void LoadStars(int seed)
    {
        List<MeshVertex> vertices = [];
        List<int> indices = [];

        Vector3 GetPoint(float i)
        {
            float G = 2.39996322972865332f;
            var y = 1 - (i / (StarCount - 1) * 2);
            var r = MathF.Sqrt(1 - (y * y));
            var t = G * i;

            float x = MathF.Cos(t) * r;
            float z = MathF.Sin(t) * r;
            return new Vector3(x, y, z);
        }

        Vector3 GetRight(Vector3 v)
        {
            var r1 = new Vector3(v.Y, -v.X, 0.0f);
            var r2 = new Vector3(v.Z, 0.0f, -v.X);
            return r1 != Vector3.Zero ? r1.Normalized() : r2.Normalized();
        }

        for (float i = 0; i < StarCount; i += 1)
        {
            var pz = GetPoint(i + IcariaNoise.GradientNoise(i * 2.1f, 0, seed) * 0.2f);
            var pr = GetRight(pz);

            pr = Vector3.Transform(pr, Matrix4x4.CreateFromAxisAngle(pz, i * 11.1f));

            var pu = Vector3.Cross(pz, pr);

            float size = StarSize + (IcariaNoise.GradientNoise(i * 4 * 1.3f, 0, seed) * StarSizeVariation);

            indices.AddRange([
                vertices.Count + 0,
                vertices.Count + 1,
                vertices.Count + 2,
                vertices.Count + 3,
                vertices.Count + 2,
                vertices.Count + 1
            ]);

            vertices.AddRange([
                new MeshVertex(pz - (pr * size) - (pu * size), new Vector2(0, 0)),
                new MeshVertex(pz + (pr * size) - (pu * size), new Vector2(1, 0)),
                new MeshVertex(pz - (pr * size) + (pu * size), new Vector2(0, 1)),
                new MeshVertex(pz + (pr * size) + (pu * size), new Vector2(1, 1))
            ]);
        }

        starMesh = new();
        starMesh.SetVertices<MeshVertex>(CollectionsMarshal.AsSpan(vertices));
        starMesh.SetIndices<int>(CollectionsMarshal.AsSpan(indices));
    }

    public void LoadClouds(int seed)
    {
        var noisePeriod0 = new NoisePeriod(CloudImageSize / 8, CloudImageSize / 8, 0);
        var noisePeriod1 = new NoisePeriod(CloudImageSize / 32, CloudImageSize / 32, 0);

        for (int y = 0; y < CloudImageSize; y++)
        {
            for (int x = 0; x < CloudImageSize; x++)
            {
                float v = 0.0f;
                v += IcariaNoise.GradientNoisePeriodic(x / 8.0f, y / 8.0f, noisePeriod0, seed);
                v += IcariaNoise.GradientNoisePeriodic(x / 32.0f, y / 32.0f, noisePeriod1, seed) * 2;
                CloudImage[x + y * CloudImageSize] = (byte)(v < 0.3 ? 0 : 255);
            }
        }

        CloudTexture = new Texture(CloudImageSize, CloudImageSize, TextureFormat.R8);
        CloudTexture.SetData<byte>(CloudImage.AsSpan());

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
            new(0, 0), // 0
            new(1, 0), // 1
            new(0, 1), // 2
            new(1, 1), // 3
            new(0, 1), // 2
            new(1, 0), // 1
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

        List<MeshVertex> vertices = [];
        List<int> indices = [];

        var fv = new bool[6];
        fv[4] = true;
        fv[5] = true;

        for (int f = 5; f >= 0; f--)
        {
            for (int y = 0; y < CloudImageSize; y++)
            {
                for (int x = 0; x < CloudImageSize; x++)
                {
                    if (CloudImage[x + y * CloudImageSize] == 0) continue;
                    fv[0] = CloudImage[MathExt.Mod(x - 1, CloudImageSize) + y * CloudImageSize] == 0;
                    fv[1] = CloudImage[MathExt.Mod(x + 1, CloudImageSize) + y * CloudImageSize] == 0;
                    fv[2] = CloudImage[x + MathExt.Mod(y - 1, CloudImageSize) * CloudImageSize] == 0;
                    fv[3] = CloudImage[x + MathExt.Mod(y + 1, CloudImageSize) * CloudImageSize] == 0;

                    if (!fv[f]) continue;

                    int offset = vertices.Count;

                    for (int v = 0; v < 4; v++)
                    {
                        vertices.Add(new MeshVertex(
                            inPosition: POSITIONS[INDICES[f][v]] + new Vector3(x, y, 0),
                            inUV: UVS[INDICES[2][v]],
                            inNormal: NORMALS[f]
                        ));
                    }

                    indices.AddRange([offset + 0, offset + 1, offset + 2, offset + 3, offset + 2, offset + 1]);
                }
            }
        }

        CloudMesh = new();
        CloudMesh.SetVertices<MeshVertex>(CollectionsMarshal.AsSpan(vertices));
        CloudMesh.SetIndices<int>(CollectionsMarshal.AsSpan(indices));
    }

    private void DrawClouds(Target? target, Matrix4x4 localViewMatrix, EntityVector eyePosition, int currentTick)
    {
        CloudMaterial.Set("u_localViewMatrix", localViewMatrix);
        CloudMaterial.Set("u_skyTexture", GetAtmosphereTexture());
        CloudMaterial.Set("u_skyTexture_sampler", atmosphereSampler);
        CloudMaterial.Set("u_sunDirection", GetSunDirection(currentTick, Time.FixedAlpha));
        CloudMaterial.Set("u_moonDirection", GetMoonDirection(currentTick, Time.FixedAlpha));

        var dc = new DrawCommand()
        {
            Target = target,
            Material = CloudMaterial,
            DepthCompare = DepthCompare.Less,
            DepthMask = true,
            CullMode = CullMode.Back,
            Mesh = CloudMesh,
            MeshIndexStart = 0,
            MeshIndexCount = CloudMesh.IndexCount
        };

        for (int dy = -1; dy <= +1; dy++)
        {
            for (int dx = -1; dx <= +1; dx++)
            {
                var offset = GetCloudOffset(eyePosition, currentTick, Time.FixedAlpha);
                var modelMatrix = Matrix4x4.Identity;
                modelMatrix *= Matrix4x4.CreateScale(CloudPixelSize);
                modelMatrix *= Matrix4x4.CreateTranslation(-offset.X + (dx * (CloudImageSize * CloudPixelSize)), -offset.Y + (dy * (CloudImageSize * CloudPixelSize)), 256.0f - eyePosition.ToVector3().Z);

                CloudMaterial.Set("u_modelMatrix", modelMatrix);
                dc.Submit();

            }
        }
    }
}