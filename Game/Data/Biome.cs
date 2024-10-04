using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foster.Framework;

namespace Game;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$DecoratorType")]
[JsonDerivedType(typeof(DecoratorSmallTree), typeDiscriminator: "SmallTree")]
abstract class DecoratorData
{
    public required string DataName;
    public required float Temperature;
    public required float Humidity;
    public required float ClimateRange;
    public required float Frequency;

    public abstract void Generate(Span<Block> blocks, in Vector3i position, int hash);
}

class DecoratorSmallTree : DecoratorData
{
    public Block Log;
    public Block Leaves;
    public int Height;
    public int HeightVariation;

    public override void Generate(Span<Block> blocks, in Vector3i position, int hash)
    {
        var height = Height + (hash % HeightVariation);
        LevelGen.GenerateColumn(blocks, position, height, this.Log);
        LevelGen.GenerateCrown(blocks, position + (0, 0, height), 2, this.Leaves);
    }
}

class GenerationData
{
    public Block BeachBlock;
    public Block WaterBlock;
}

class TopLayerData
{
    public Block Surface;
    public Block Subsurface;
    public int Depth;
    public float Temperature;
    public float Humidity;
}

static partial class Data
{
    public static GenerationData GenerationData = new();

    static void ParseGenerationData(in GenerationData newData)
    {
        GenerationData = newData;
    }

    //

    static TopLayerData[] topLayerData = [];

    static int[,] topLayerMap = new int[32, 32];

    /// <summary>
    /// Indexes topLayerMap by temperature ( -1.0 .. +1.0 ), humidity ( -1.0 .. +1.0 )
    /// </summary>
    public static TopLayerData GetTopLayerData(float temperature, float humidity)
    {
        var x = (byte)(Math.Clamp(temperature * 0.5f + 0.5f, 0, 1) * 255);
        var y = (byte)(Math.Clamp(humidity * 0.5f + 0.5f, 0, 1) * 255);
        return topLayerData[topLayerMap[x, y]];
    }

    /// <summary>
    /// Indexes topLayerMap by temperature ( 0..255 ), humidity ( 0..255 )
    /// </summary>
    public static TopLayerData GetTopLayerData(byte temperature, byte humidity)
    {
        return topLayerData[topLayerMap[temperature / 8, humidity / 8]];
    }

    public static TopLayerData GetTopLayerDataSearch(byte temperature, byte humidity)
    {
        var temp = ((((float)temperature) / 255) * 2) - 1.0f;
        var humi = ((((float)humidity) / 255) * 2) - 1.0f;

        var p = new Vector2(temp, humi);

        var closestIndex = 0;
        var closestDistance = float.MaxValue;

        for (int i = 0; i < topLayerData.Length; i++)
        {
            float d = Vector2.Distance(p, new Vector2(topLayerData[i].Temperature, topLayerData[i].Humidity));

            if (d < closestDistance)
            {
                closestIndex = i;
                closestDistance = d;
            }
        }

        return topLayerData[closestIndex];
    }

    static void ParseTopLayerData(in TopLayerData[] newData)
    {
        //static float ManhattanDistance(Vector2 a, Vector2 b) => MathF.Abs(a.X - b.X) + MathF.Abs(a.Y - b.Y);
        //static float ChebyshevDistance(Vector2 a, Vector2 b) => Math.Max(MathF.Abs(a.X - b.X), MathF.Abs(a.Y - b.Y));

        topLayerData = topLayerData.Concat(newData).ToArray();

        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                float temp = (((float)x) / 16) - 1.0f;
                float humi = (((float)y) / 16) - 1.0f;
                var p = new Vector2(temp, humi);

                var closestIndex = 0;
                var closestDistance = float.MaxValue;

                for (int i = 0; i < topLayerData.Length; i++)
                {
                    float d = Vector2.Distance(p, new Vector2(topLayerData[i].Temperature, topLayerData[i].Humidity));

                    if (d < closestDistance)
                    {
                        closestIndex = i;
                        closestDistance = d;
                    }
                }

                topLayerMap[x, y] = closestIndex;
            }
        }
    }

    //

    public static DecoratorData[] DecoratorData = [];

    static void ParseDecoratorData(in DecoratorData[] newData)
    {
        DecoratorData = DecoratorData.Concat(newData).ToArray();
    }

    public static void GetDecoratorList(byte temperature, byte humidity, ref List<DecoratorData> outList)
    {
        float temp = (((float)temperature) / 255) * 2 - 1;
        float humi = (((float)humidity) / 255) * 2 - 1;

        static bool WithinRange(float a, float x, float y) => a >= x && a <= y;

        for (int i = 0; i < DecoratorData.Length; i++)
        {
            var data = DecoratorData[i];

            if (Vector2.Distance(new Vector2(temp, humi), new Vector2(data.Temperature, data.Humidity)) < data.ClimateRange)
            {
                outList.Add(data);
            }
        }
    }
}