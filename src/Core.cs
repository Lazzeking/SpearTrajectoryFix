using HarmonyLib;
using SpearTrajectory.Patches;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ProjectileTrajectoryFix;

// quick fix for Projectile Trajectory the line is a bit off on 1.22.7
public class Core : ModSystem
{
    public static ICoreClientAPI? Capi;

    private Harmony? harmony;

    // the line is only drawn on the client, nothing to do on servers
    public override bool ShouldLoad(EnumAppSide side)
    {
        return side == EnumAppSide.Client;
    }

    // go before Projectile Trajectory, it looks for Combat Overhaul as soon as it starts
    public override double ExecuteOrder()
    {
        return 0;
    }

    // patch in Start so we're in before its own Start
    public override void Start(ICoreAPI api)
    {
        harmony = new Harmony(Mod.Info.ModID);
        harmony.PatchAll();
    }

    // kept around for the player's stats later on
    public override void StartClientSide(ICoreClientAPI api)
    {
        Capi = api;
    }

    public override void Dispose()
    {
        harmony?.UnpatchAll(harmony.Id);
    }

    // how fast a vanilla spear or arrow leaves, same as the game does it
    // draw strength comes from traits/gear, it ignored this
    public static float Speed(Item item)
    {
        float strength = 1f;

        EntityPlayer? player = Capi?.World.Player?.Entity;
        if (player != null)
        {
            strength = player.Stats.GetBlended("bowDrawingStrength");
        }

        if (item is ItemSpear)
        {
            return 0.65f * strength;
        }

        return strength;
    }
}

// it guessed the launch itself: arrows came out of the shoulder like spears and the spread was upside down
// so just ask the game how it throws
[HarmonyPatch(typeof(PatchAimingData), nameof(PatchAimingData.GetRealProjectileDirection))]
public static class LaunchPatch
{
    public static bool Prefix(EntityAgent entity, ref (Vec3d, Vec3d, float) __result)
    {
        Item? item = entity.RightHandItemSlot?.Itemstack?.Item;

        // anything that isn't a vanilla spear or bow goes through its own code
        if (item is not (ItemSpear or ItemBow))
        {
            return true;
        }

        // numbers straight from ItemSpear / ItemBow: spread, height, sideways, forward, parallax
        FastVec3d start;
        FastVec3d direction;
        if (item is ItemSpear)
        {
            // from the shoulder, a bit in front of you
            (start, direction) = EntityProjectileBase.GetProjectileDirection(entity, 0.75, 0, 0.3, -0.15, 20);
        }
        else
        {
            // straight from the eyes, a bit behind
            (start, direction) = EntityProjectileBase.GetProjectileDirection(entity, 0.75, 0, 0, 0.21, 0);
        }

        Vec3d startPos = new Vec3d(start.X, start.Y, start.Z);
        Vec3d directionVec = new Vec3d(direction.X, direction.Y, direction.Z);
        float speed = Core.Speed(item);

        __result = (startPos, directionVec, speed);

        // skip its own version
        return false;
    }
}

// how fast it flies and how it falls, for vanilla spears and bows
[HarmonyPatch(typeof(TrajectoryPhysics), nameof(TrajectoryPhysics.For))]
public static class PhysicsPatch
{
    public static void Postfix(Item item, TrajectoryPhysics __result)
    {
        if (item is not (ItemSpear or ItemBow))
        {
            return;
        }

        // the line takes its speed from here, not from the launch above
        __result.Velocity = Core.Speed(item);

        // weird name but this turns off extra falling the game doesn't do, long shots were landing short
        __result.UseCOPhysics = true;
    }
}
