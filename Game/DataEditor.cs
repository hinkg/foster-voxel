using System.Numerics;
using System.Text.Json;
using Foster.Framework;

namespace Game;

class MenuDataEditor : GameMenu
{
    int selectedEditor = 0;

    int selectedBlock = 1;
    int selectedPropertyEditor = -1;
    int selectedPropertyItem = -1;

    const int TexturePropertyEditor = 0;
    int texturePropertyEditorFace = 0;

    const int AudioPropertyEditor = 1;
    int audioPropertyEditorItem = 0;

    bool editingBiomeOverlayTextures = false;

    Image terrainImage;
    Texture terrainTexture;

    public override void OnCreate(Game game)
    {
        terrainImage = new Image("Assets/Textures/terrain.png");

        for (int y = 0; y < terrainImage.Height; y++)
        {
            for (int x = 0; x < terrainImage.Width; x++)
            {
                var p = terrainImage.GetPixel(x, y);
                if (p.A < 255) p = Color.Black;
                terrainImage.SetPixel(x, y, p);
            }
        }

        terrainTexture = new Texture(terrainImage);
    }

    public override void OnDestroy(Game game)
    {

    }

    public override void OnHide(Game game)
    {

    }

    public override void OnShow(Game game)
    {

    }

    public override void Update(Game game)
    {
        if (!game.level.IsLoaded) return;

        var r0 = GUI.Viewport.AlignCenter(1000, 600);
        GUI.Panel(r0);

        // Top tab bar
        var topTabBarRect = r0.CutTop(32);
        GUI.Panel(topTabBarRect);

        if (GUI.Selectable(topTabBarRect.CutLeft(144).Inflate(-4), "Blocks", selectedEditor == 0)) selectedEditor = 0;
        if (GUI.Selectable(topTabBarRect.CutLeft(144).Inflate(-4), "Biome Gradient", selectedEditor == 1)) selectedEditor = 1;

        if (selectedEditor == 0) BlockPropertyEditor(game, r0);
        if (selectedEditor == 1) BiomeGradientEditor(game, r0);
    }

    private void BlockPropertyEditor(Game game, Rect r0)
    {
        // Block list

        var blockListRect = GUI.BeginChild(r0.CutLeft(144).Inflate(-4));

        for (int i = 1; i < Data.BlockData.Length; i++)
        {
            var rectLine = blockListRect.CutTop(24).Inflate(-2);
            var bd = Data.BlockData[i];
            if (GUI.Selectable(rectLine, $"{i,2} {bd.DataName}", i == selectedBlock)) selectedBlock = i;
        }

        GUI.EndChild();

        // Bottom menu bar
        var bottomMenuRect = r0.CutBottom(36).Inflate(-4);

        GUI.Panel(bottomMenuRect);

        if (GUI.Button(bottomMenuRect.CutRight(64).Inflate(-4), "Save"))
        {
            var json = JsonSerializer.Serialize(new Data.JsonDataListBlock(Data.BlockData.AsSpan(1).ToArray()), Data.JsonOptions);
            File.WriteAllText("Assets/blocks.json", json);
        }

        // Property list
        void PropertyString(ref Rect rect, string name, string value, int editor = -1, int item = -1)
        {
            var r0 = rect.CutTop(28);
            bool active = (selectedPropertyEditor == editor && selectedPropertyItem == item);

            GUI.Panel(r0.Inflate(-2), editor != -1 ? (r0.Hovered() || active) : false, false);

            var rl = r0.GetLeft(r0.Width / 2);
            var rr = r0.GetRight(r0.Width / 2);

            GUI.TextRect(rl.Inflate(-8, 0), name, false, GUI.TextAlignMode.Left);
            GUI.TextRect(rr.Inflate(-8, 0), value, false, GUI.TextAlignMode.Left);

            if (r0.Pressed())
            {
                selectedPropertyEditor = editor;
                selectedPropertyItem = item;
            }
        }

        void PropertyTextures(ref Rect rect, string name, Vector2i[] textures, bool areOverlayTextures)
        {
            var r0 = rect.CutTop(28);
            GUI.Panel(r0.Inflate(-2));

            var rl = r0.CutLeft(r0.Width / 2);
            var rr = r0;
            rr.Width -= 4;

            GUI.TextRect(rl.Inflate(-8, 0), name, false, GUI.TextAlignMode.Left);

            static Quad MakeUV(int x, int y)
            {
                var uv = new Vector2(x, y) / 16;
                return new Quad(
                    uv,
                    uv + (Vector2.UnitX / 16),
                    uv + (Vector2.One / 16),
                    uv + (Vector2.UnitY / 16)
                );
            }

            {
                var br = rr.CutRight(24).Inflate(-4, -6);
                GUI.TextRect(br, "A", true, GUI.TextAlignMode.Center);

                if (br.Pressed())
                {
                    selectedPropertyEditor = TexturePropertyEditor;
                    texturePropertyEditorFace = 6;
                    editingBiomeOverlayTextures = areOverlayTextures;
                }

                if ((editingBiomeOverlayTextures == areOverlayTextures && selectedPropertyEditor == TexturePropertyEditor && texturePropertyEditorFace == 6) || br.Hovered())
                {
                    GUI.RectLine(br.Inflate(2), Color.White);
                }
            }

            for (int f = 5; f >= 0; f--)
            {
                var br = rr.CutRight(24).Inflate(-4, -6);

                if (br.Pressed())
                {
                    selectedPropertyEditor = TexturePropertyEditor;
                    texturePropertyEditorFace = f;
                    editingBiomeOverlayTextures = areOverlayTextures;
                }

                if ((editingBiomeOverlayTextures == areOverlayTextures && selectedPropertyEditor == TexturePropertyEditor && texturePropertyEditorFace == f) || br.Hovered())
                {
                    GUI.RectLine(br.Inflate(2), Color.White);
                }

                GUI.QuadTexture(br.ToQuad(), MakeUV(textures[f].X, textures[f].Y), terrainTexture);
            }
        }

        var propRect = r0.CutLeft(280).Inflate(-4);
        GUI.Panel(propRect);
        propRect = propRect.Inflate(-4);

        ref var blockData = ref Data.BlockData[selectedBlock];

        PropertyString(ref propRect, nameof(blockData.DataName), blockData.DataName);
        PropertyString(ref propRect, nameof(blockData.DisplayName), blockData.DisplayName);
        PropertyTextures(ref propRect, "Textures", blockData.Textures, false);
        PropertyTextures(ref propRect, "Climate Tex", blockData.ClimateOverlayTextures, true);
        PropertyString(ref propRect, "Audio Step", blockData.AudioOnStep, AudioPropertyEditor, 0);
        PropertyString(ref propRect, "Audio Place", blockData.AudioOnPlace, AudioPropertyEditor, 1);
        PropertyString(ref propRect, "Audio Destroy", blockData.AudioOnDestroy, AudioPropertyEditor, 2);

        // Texture editor

        if (selectedPropertyEditor == TexturePropertyEditor)
        {
            var rt = r0.AlignCenter(512, 512);
            GUI.RectTexture(rt, new Rect(0, 0, terrainTexture.Width, terrainTexture.Height), terrainTexture);

            for (int f = (texturePropertyEditorFace == 6 ? 0 : texturePropertyEditorFace); f <= (texturePropertyEditorFace == 6 ? 5 : texturePropertyEditorFace); f++)
            {
                var t = editingBiomeOverlayTextures ? blockData.ClimateOverlayTextures[f] : blockData.Textures[f];
                var rto = rt.GetLeft(rt.Width / 16).GetTop(rt.Height / 16).Translate(t.X * (rt.Width / 16), t.Y * (rt.Height / 16));

                GUI.RectLine(rto.Inflate(0), Color.Black);
                GUI.RectLine(rto.Inflate(1), Color.White);
                GUI.RectLine(rto.Inflate(2), Color.Black);

                if (rt.Active())
                {
                    var tx = ((Input.Mouse.Position - rt.Position) / 32).Floor();

                    if (editingBiomeOverlayTextures)
                        blockData.ClimateOverlayTextures[f] = new Vector2i((int)tx.X, (int)tx.Y);
                    else
                        blockData.Textures[f] = new Vector2i((int)tx.X, (int)tx.Y);
                }
            }
        }

        // Audio editor/selector

        if (selectedPropertyEditor == AudioPropertyEditor)
        {
            var audioListRect = r0.CutLeft(128).Inflate(-4);
            GUI.Panel(audioListRect);

            ref string selectedAudio = ref blockData.AudioOnStep;
            if (selectedPropertyItem == 1) selectedAudio = ref blockData.AudioOnPlace;
            if (selectedPropertyItem == 2) selectedAudio = ref blockData.AudioOnDestroy;

            if (GUI.Selectable(audioListRect.CutTop(28).Inflate(-4), "None", selectedAudio == ""))
            {
                selectedAudio = "";
            }

            foreach ((var name, var data) in Audio.SoundMap)
            {
                if (GUI.Selectable(audioListRect.CutTop(28).Inflate(-4), $"{name}", name == selectedAudio))
                {
                    Audio.Play(name, 0.5f);
                    selectedAudio = name;
                }
            }
        }
    }

    private void RecalculateGradient(Game game)
    {
        for (int y = 0; y < game.level.ClimateColorImage.Height; y++)
        {
            for (int x = 0; x < game.level.ClimateColorImage.Width; x++)
            {
                float u = ((float)x) / game.level.ClimateColorImage.Width;
                float v = ((float)y) / game.level.ClimateColorImage.Height;

                var c = Color.Lerp(Color.Lerp(Level.Climate_P00, Level.Climate_P10, u), Color.Lerp(Level.Climate_P01, Level.Climate_P11, u), v);
                game.level.ClimateColorImage.SetPixel(x, y, c);
            }
        }

        game.level.ClimateColorTexture = new Texture(game.level.ClimateColorImage);
    }

    int gradEditorSelectedPoint = 0;

    private void BiomeGradientEditor(Game game, Rect r0)
    {
        var r1 = r0.CutLeft(384).Inflate(-4);
        GUI.Panel(r1);

        var rt = r0.CutRight(r0.Height).AlignCenter(384, 384);
        GUI.Panel(rt.Inflate(8));

        GUI.RectTexture(rt, new Rect(game.level.ClimateColorTexture.Width, game.level.ClimateColorTexture.Height), game.level.ClimateColorTexture);

        if (new Rect(rt.TopLeft, rt.Center).Pressed()) gradEditorSelectedPoint = 0;
        if (new Rect(rt.TopRight, rt.Center).Pressed()) gradEditorSelectedPoint = 1;
        if (new Rect(rt.BottomLeft, rt.Center).Pressed()) gradEditorSelectedPoint = 2;
        if (new Rect(rt.BottomRight, rt.Center).Pressed()) gradEditorSelectedPoint = 3;

        if (gradEditorSelectedPoint == 0) GUI.CircleLine(rt.TopLeft, 4f, Color.White, Color.Black);
        if (gradEditorSelectedPoint == 1) GUI.CircleLine(rt.TopRight, 4f, Color.White, Color.Black);
        if (gradEditorSelectedPoint == 2) GUI.CircleLine(rt.BottomLeft, 4f, Color.White, Color.Black);
        if (gradEditorSelectedPoint == 3) GUI.CircleLine(rt.BottomRight, 4f, Color.White, Color.Black);

        ref var selectedColor = ref Level.Climate_P00;
        if (gradEditorSelectedPoint == 1) selectedColor = ref Level.Climate_P10;
        if (gradEditorSelectedPoint == 2) selectedColor = ref Level.Climate_P01;
        if (gradEditorSelectedPoint == 3) selectedColor = ref Level.Climate_P11;
    }
}