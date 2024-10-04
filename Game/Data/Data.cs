using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Game;

static partial class Data
{
    // These two are always available
    public const string DefaultNonSolidName = "Air";
    public const string DefaultSolidName = "Stone";
    public const Block DefaultNonSolid = (Block)0;
    public const Block DefaultSolid = (Block)1;

    //

    public static JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        IncludeFields = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    public class JsonDataListBlock(BlockData[] blockData)
    {
        public BlockData[]? BlockData = blockData;
    }

    public class JsonDataListItem(ItemData[] itemData)
    {
        public ItemData[]? ItemData = itemData;
    }

    public class JsonDataListGen(GenerationData generationData)
    {
        public GenerationData? GenerationData = generationData;
    }

    public class JsonDataListTopLayer(TopLayerData[] topLayerData)
    {
        public TopLayerData[]? TopLayerData = topLayerData;
    }

    public class JsonDataListDecorator(DecoratorData[] decoratorData)
    {
        public DecoratorData[]? DecoratorData = decoratorData;
    }

    static void ParseDataMap<T>(string mapPath, ref Dictionary<string, T> dataMap) where T : Enum
    {
        if (!File.Exists(mapPath))
        {
            Log.Warn($"No data map file found at \"{mapPath}\"");
            return;
        }

        var fileData = File.ReadAllBytes(mapPath);
        var dataList = JsonSerializer.Deserialize<Dictionary<string, int>>(fileData, JsonOptions);

        if (dataList == null || dataList.Count == 0)
        {
            Log.Error($"Found no map entries in file \"{mapPath}\"");
            return;
        }

        foreach (var (dataName, index) in dataList)
        {
            dataMap[dataName] = (T)Convert.ChangeType(index, Enum.GetUnderlyingType(typeof(T)));
        }

        Log.Trace($"Parsed {dataList.Count} entries from \"{Path.GetFileName(mapPath)}\"");
    }

    static void WriteDataMap<T>(string mapPath, in Dictionary<string, T> dataMap) where T : Enum
    {
        Dictionary<string, int> convMap = [];

        foreach (var (dataName, index) in dataMap)
        {
            if (typeof(T) == typeof(Block) && dataName == "Air") continue;
            if (typeof(T) == typeof(Block) && dataName == "Stone") continue;
            convMap[dataName] = (int)Convert.ChangeType(index, typeof(int));
        }

        var json = JsonSerializer.Serialize(convMap, JsonOptions);
        File.WriteAllText(mapPath, json);
    }

    public static void IntializeDataMaps(string blockMapPath, string itemMapPath)
    {
        BlockMap = [];
        BlockMap["Air"] = 0;
        BlockMap["Stone"] = (Block)1;
        ParseDataMap(blockMapPath, ref BlockMap);
        BlockData = new BlockData[BlockMap.Count];
        BlockData[0] = new BlockData() { DataName = "Air", DisplayName = "Air" };
        BlockData[1] = new BlockData() { DataName = "Stone", DisplayName = "Stone" };

        itemMap = [];
        ParseDataMap(itemMapPath, ref itemMap);
        itemData = new ItemData[itemMap.Count];
    }

    public static void WriteDataMaps(string blockMapPath, string itemMapPath)
    {
        WriteDataMap(blockMapPath, BlockMap);
        WriteDataMap(itemMapPath, itemMap);
    }

    struct JsonDataEntry
    {
        public string DataName;
    }

    struct JsonDataList
    {
        public JsonDataEntry[]? BlockData;
        public JsonDataEntry[]? ItemData;
    }

    private static void AddNewJsonDataEntries<T, D>(in JsonDataEntry[] entries, ref Dictionary<string, T> dataMap, ref D[] dataArray) where T : Enum
    {
        var newIDCounter = dataArray.Length;

        foreach (var entry in entries)
        {
            if (!dataMap.ContainsKey(entry.DataName))
            {
                dataMap[entry.DataName] = (T)Convert.ChangeType(newIDCounter, Enum.GetUnderlyingType(typeof(T)));
                newIDCounter += 1;
            }
        }

        if (newIDCounter > dataArray.Length)
        {
            Log.Trace($"Added {newIDCounter - dataArray.Length} entries to {typeof(T)} data");
            Array.Resize(ref dataArray, newIDCounter);
        }
    }

    public static void ParseJsonEntries(string path)
    {
        var fileData = File.ReadAllBytes(path);

        bool parsedAny = false;
        var dataList = JsonSerializer.Deserialize<JsonDataList>(fileData, JsonOptions);

        // Block
        if (dataList.BlockData != null && dataList.BlockData.Length > 0)
        {
            AddNewJsonDataEntries(dataList.BlockData, ref BlockMap, ref BlockData);
            parsedAny = true;
        }

        // Item
        if (dataList.ItemData != null && dataList.ItemData.Length > 0)
        {
            AddNewJsonDataEntries(dataList.ItemData, ref itemMap, ref itemData);
            parsedAny = true;
        }

        //
        if (!parsedAny)
        {
            Log.Error($"Found no data entries in file \"{path}\"");
            return;
        }
    }

    public static void ParseJsonData(string path)
    {
        if (BlockMap.Count < 1)
        {
            throw new Exception("IntializeDataMaps needs to be called before ParseJsonData");
        }

        var fileData = File.ReadAllBytes(path);

        bool parsedAny = false;

        // Block
        {
            var dataListBlock = JsonSerializer.Deserialize<JsonDataListBlock>(fileData, JsonOptions);
            if (dataListBlock!.BlockData != null && dataListBlock.BlockData.Length > 0)
            {
                ParseBlockData(dataListBlock.BlockData);
                parsedAny = true;
            }
        }

        // Item
        {
            var dataListItem = JsonSerializer.Deserialize<JsonDataListItem>(fileData, JsonOptions);
            if (dataListItem!.ItemData != null && dataListItem.ItemData.Length > 0)
            {
                ParseItemData(dataListItem.ItemData);
                parsedAny = true;
            }
        }

        // Generation
        {
            var dataListGeneration = JsonSerializer.Deserialize<JsonDataListGen>(fileData, JsonOptions);
            if (dataListGeneration!.GenerationData != null)
            {
                ParseGenerationData(dataListGeneration.GenerationData);
                parsedAny = true;
            }
        }

        // Top Layer
        {
            var dataListTopLayer = JsonSerializer.Deserialize<JsonDataListTopLayer>(fileData, JsonOptions);
            if (dataListTopLayer!.TopLayerData != null && dataListTopLayer.TopLayerData.Length > 0)
            {
                ParseTopLayerData(dataListTopLayer.TopLayerData);
                parsedAny = true;
            }
        }

        // Decorator
        {
            var dataListItem = JsonSerializer.Deserialize<JsonDataListDecorator>(fileData, JsonOptions);
            if (dataListItem!.DecoratorData != null && dataListItem.DecoratorData.Length > 0)
            {
                ParseDecoratorData(dataListItem.DecoratorData);
                parsedAny = true;
            }
        }

        //
        if (!parsedAny)
        {
            Log.Error($"Found no data entries in file \"{path}\"");
            return;
        }
    }
}