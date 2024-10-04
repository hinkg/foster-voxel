using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Game;

enum Item : ushort { };

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$ItemType")]
[JsonDerivedType(typeof(ItemBlock), typeDiscriminator: "ItemBlock")]
abstract class ItemData
{
    public string DataName;
    public string DisplayName;

    protected ItemData(string dataName, string displayName)
    {
        DataName = dataName;
        DisplayName = displayName;
    }

    public virtual Block OnPlace() => 0;
}

class ItemBlock : ItemData
{
    public Block PlaceTarget;

    public ItemBlock(string dataName, string displayName, Block placeTarget) : base(dataName, displayName)
    {
        PlaceTarget = placeTarget;
    }

    public override Block OnPlace() => PlaceTarget;
}

static partial class Data
{
    static Dictionary<string, Item> itemMap = [];
    static ItemData[] itemData = [];

    public static Span<ItemData> GetItemDataList() => itemData.AsSpan();

    static void ParseItemData(in ItemData[] newData)
    {
        foreach (var entry in newData)
        {
            itemData[(int)itemMap[entry.DataName]] = entry;
        }
    }

    public static void MakeItemBlockData()
    {
        List<ItemBlock> newList = [];
        var newItemIDCounter = itemData.Length;

        for (int i = 0; i < BlockData.Length; i++)
        {
            var b = (Block)i;
            ref var bd = ref BlockData[(int)b];

            if (bd.DataName == null)
            {
                throw new Exception($"Block {(int)b} has null DataName");
            }

            if (!itemMap.ContainsKey(bd.DataName))
            {
                itemMap[bd.DataName] = (Item)newItemIDCounter;
                bd.ItemDrop = (Item)newItemIDCounter;
                newItemIDCounter += 1;

                newList.Add(new ItemBlock(bd.DataName, bd.DisplayName, b));
            }
            else
            {
                bd.ItemDrop = itemMap[bd.DataName];

                if (itemData[(int)itemMap[bd.DataName]] == null)
                {
                    itemData[(int)itemMap[bd.DataName]] = new ItemBlock(bd.DataName, bd.DisplayName, b);
                }
            }

            //Log.Trace($"Set item drop for block \"{bd.DataName}\" to \"{(int)bd.ItemDrop}\"");
        }

        if (newList.Count > 0)
        {
            Array.Resize(ref itemData, newItemIDCounter);
            ParseItemData(newList.ToArray());
        }
    }

    public static unsafe ItemData GetItemData(Item item) => itemData[(int)item];
    public static unsafe Item GetItem(string name) => itemMap[name];
    public static unsafe Item GetItemBlock(string name) => itemMap[name];
}