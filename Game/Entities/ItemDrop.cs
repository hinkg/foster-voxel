using System.Numerics;
using Foster.Framework;

namespace Game;

class ItemDrop : Entity
{
    public Item ItemID;

    public override string AudioOnKill => "plop";

    public override int MaxHealth => 1;
    public override Vector3 BoundingBox => new(0.25f, 0.25f, 0.25f);
    public override float EyeHeight => 0.125f;

    public override int ModelID => Data.GetItemModelID(this.ItemID);

    public override float Mass => 0.3f;
    public override float StepInterval => 0.0f;

    int startTick = 0;
    const int RotationPeriod = 500;
    const int WavePeriod = 300;

    public ItemDrop(Item id, EntityVector position, Vector2 rotation)
    {
        this.ItemID = id;
        this.Position = position;
        this.PositionPrev = position;
        this.Rotation = rotation;
        this.RotationPrev = rotation;
        this.Velocity = default;
    }

    public ItemDrop(Item id, Vector3i blockPosition, int currentTick)
    {
        this.ItemID = id;
        this.Position = EntityVector.FromBlock(blockPosition) + EntityVector.FromBlock(1, 1, 0) / 2;
        this.PositionPrev = this.Position;

        this.Velocity = EntityVector.FromBlock(
            (((currentTick % 23) / 23.0f) - 0.5f) * 0.015f,
            (((currentTick % 17) / 17.0f) - 0.5f) * 0.015f,
            0.05f
        );

        this.startTick = currentTick;
    }

    public override void Update(Level level, Span<Entity> entities)
    {
        foreach (var entity in entities)
        {
            if (entity.GetType() != typeof(Player)) continue;

            var d = (entity.Position + ((EntityVector.UnitZ * entity.EyeHeight) / 2)) - this.Position;

            if (d.ToVector3().Length() < 2)
            {
                this.Velocity += EntityVector.FromBlock(d.ToVector3().Normalized()) * 0.01f;
            }

            if (EntityAABB.Intersect(this.GetBoundingBox(), entity.GetBoundingBox()))
            {
                ((Player)entity).AddToInventory(this.ItemID);
                this.ShouldKill = true;
                break;
            }
        }

        return;
    }

    public override void Render(ref List<EntityRenderBlock> renderList, int currentTick)
    {
        int tick = startTick + currentTick;
        Vector3 localOffset = Vector3.UnitZ * (MathF.Sin(((tick % WavePeriod) + Time.FixedAlpha) / WavePeriod * MathF.Tau) * 0.5f + 0.5f) * BoundingBox.Z;
        float rotation = ((tick % RotationPeriod) + Time.FixedAlpha) / RotationPeriod * MathF.Tau;

        var modelData = Data.GetItemModelData(ItemID);
        renderList.Add(new EntityRenderBlock(this.GetInterpolatedPosition(Time.FixedAlpha), localOffset, Vector3.UnitZ * rotation, BoundingBox, (modelData.IndexStart, modelData.IndexCount)));
    }
}