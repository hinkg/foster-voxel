using System.Text.Json;
using System.Text.Json.Serialization;

namespace Game;

[JsonConverter(typeof(BlockJsonConverter))]
public enum Block : byte { };

class BlockJsonConverter : JsonConverter<Block>
{
    public override Block Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return Data.GetBlock(reader.GetString()!);
    }

    public override void Write(Utf8JsonWriter writer, Block value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}

struct BlockData
{
    public string DataName;
    public string DisplayName;

    public string AudioOnStep;
    public string AudioOnDestroy;
    public string AudioOnPlace;

    public bool IsLight;
    public bool IsTranslucent;
    public bool IsLiquid;

    [JsonIgnore]
    public Item ItemDrop;

    public Vector2i[] Textures;
    public Vector2i[] ClimateOverlayTextures;

    [JsonIgnore]
    public int[] TexturesPacked;

    [JsonIgnore]
    public int[] ClimateOverlayTexturesPacked;

    public BlockData()
    {
        DataName = "Null";
        DisplayName = "Null";
        Textures = new Vector2i[6];
        TexturesPacked = new int[6];
        ClimateOverlayTextures = new Vector2i[6];
        ClimateOverlayTexturesPacked = new int[6];
        AudioOnStep = "";
        AudioOnDestroy = "";
        AudioOnPlace = "";
    }
}

static partial class Data
{
    public static Dictionary<string, Block> BlockMap = [];
    public static BlockData[] BlockData = [];

    static void ParseBlockData(in BlockData[] newData)
    {
        foreach (var entry in newData)
        {
            var n = entry;

            for (int f = 0; f < 6; f++)
                n.TexturesPacked[f] = (n.Textures[f].X << 4) | n.Textures[f].Y;

            for (int f = 0; f < 6; f++)
                if (n.ClimateOverlayTextures[f] == Vector2i.Zero) n.ClimateOverlayTextures[f] = new Vector2i(0, 1);

            for (int f = 0; f < 6; f++)
                n.ClimateOverlayTexturesPacked[f] = (n.ClimateOverlayTextures[f].X << 4) | n.ClimateOverlayTextures[f].Y;

            BlockData[(int)BlockMap[entry.DataName]] = entry;
        }
    }

    public static unsafe BlockData GetBlockData(Block block) => BlockData[(int)block];
    public static unsafe Block GetBlock(string name) => BlockMap[name];
}