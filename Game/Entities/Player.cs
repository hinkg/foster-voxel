using System.Numerics;

namespace Game;

struct InventorySlot
{
    public Item Item;
    public int Count;

    public InventorySlot(Item item, int count)
    {
        this.Item = item;
        this.Count = count;
    }

    public static InventorySlot Clear => new InventorySlot(0, 0);
}

class Player : Entity
{
    public override int MaxHealth => 10;
    public override Vector3 BoundingBox => new(0.6f, 0.6f, 1.8f);
    public override float EyeHeight => 1.5f;

    public EntityVector MovementDirection;

    public int JumpGraceTicks;
    public int JumpCoyoteTicks;

    public InventorySlot[] Inventory;
    public Span<InventorySlot> Hotbar => Inventory.AsSpan(0, 10);

    public override int ModelID => 0;

    public override string AudioOnKill => "";

    public override float Mass => 1.0f;

    public override float StepInterval => 0.65f;

    public Player(EntityVector position, Vector2 rotation) : base()
    {
        this.Position = position;
        this.PositionPrev = position;
        this.Rotation = rotation;
        this.RotationPrev = rotation;
        this.Velocity = default;

        this.Inventory = new InventorySlot[30];
    }

    public void DoJump()
    {
        JumpGraceTicks = 10;
    }

    public void AddToInventory(Item item)
    {
        for (int i = 0; i < Inventory.Length; i++)
        {
            if (Inventory[i].Item == item || Inventory[i].Item == 0)
            {
                Inventory[i].Item = item;
                Inventory[i].Count += 1;
                return;
            }
        }
    }

    public override void Update(Level level, Span<Entity> entities)
    {
        var horiSpeed = 0.0075f;
        horiSpeed *= (!IsInLiquid && IsGrounded || !DoGravity) ? 1.0f : (Velocity.ToVector3().XY().Length() < 0.047 ? 0.4f : 0.01f);
        horiSpeed *= IsInLiquid ? 0.7f : 1.0f;

        var vertSpeed = 0.0075f * (IsInLiquid ? 0.5f : (DoGravity ? 0.0f : 1.0f));

        if (!DoGravity)
        {
            horiSpeed *= 16.0f;
            vertSpeed *= 16.0f;
        }

        Velocity.X += EntityVector.Mul(MovementDirection.X, horiSpeed);
        Velocity.Y += EntityVector.Mul(MovementDirection.Y, horiSpeed);
        Velocity.Z += EntityVector.Mul(MovementDirection.Z, vertSpeed);

        if (MovementDirection.Z > 0 && ExitedLiquid)
        {
            Velocity.Z += EntityVector.FromBlock(0.01f);
        }

        if (HasBecomeAirborne) JumpCoyoteTicks = 10;

        if (!IsInLiquid && (IsGrounded || JumpCoyoteTicks > 0) && JumpGraceTicks > 0 && Velocity.Z <= 0)
        {
            Velocity.Z = EntityVector.FromBlock(0.09f);
            JumpGraceTicks = 0;
            JumpCoyoteTicks = 0;
        }

        if (JumpGraceTicks > 0) JumpGraceTicks -= 1;
        if (JumpCoyoteTicks > 0) JumpCoyoteTicks -= 1;
    }

    public override void Render(ref List<EntityRenderBlock> renderList, int tick)
    {
        return;
    }
}