using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SpearTrajectory.Bridge;
using SpearTrajectory.Patches;
using SpearTrajectory.Systems;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ProjectileTrajectoryFix;

// Combat Overhaul Fork stuff, all read off the items json so no need for its dll
public static class CombatOverhaul
{
    // bows have ArrowVelocity right in the item
    public static bool IsBow(Item item)
    {
        return item.Attributes?["ArrowVelocity"].Exists == true;
    }

    // spears and such keep a ThrowAttack somewhere in their modes, just grab the first one
    public static JsonObject? Throw(Item item)
    {
        JToken? token = item.Attributes?.Token?.SelectToken("$..ThrowAttack");
        if (token == null)
        {
            return null;
        }

        return new JsonObject(token);
    }

    // how fast it leaves
    public static float Speed(Item item)
    {
        if (IsBow(item))
        {
            return item.Attributes["ArrowVelocity"].AsFloat(1);
        }

        JsonObject? throwAttack = Throw(item);
        if (throwAttack == null)
        {
            return 1;
        }

        return throwAttack["Velocity"].AsFloat(1);
    }

    // degrees above the crosshair, 1.5 is the fork's default
    public static float Zeroing(Item item)
    {
        if (IsBow(item))
        {
            return item.Attributes["Zeroing"].AsFloat(1.5f);
        }

        JsonObject? throwAttack = Throw(item);
        if (throwAttack == null)
        {
            return 0;
        }

        return throwAttack["Zeroing"].AsFloat(1.5f);
    }

    // the projectile entity it fires, that's where gravity and drag live
    public static string Projectile(Item item)
    {
        // arrows all fly the same so whatever, flint
        if (IsBow(item))
        {
            return "combatoverhaul:arrow-flint";
        }

        // spears say it themselves, like combatoverhaul:spear-generic-{material}
        string code = item.Attributes?.Token?.SelectToken("$..EntityCode")?.ToString() ?? "";
        return code.Replace("{material}", item.Variant["material"]);
    }
}

// this is why CO never worked, the fork moved everything to OverhaullibLegacyCompat
// it looks for the assembly by name, so just swap the name it looks for
[HarmonyPatch(typeof(CombatOverhaulBridge), "InitReflection")]
public static class FindForkPatch
{
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
    {
        foreach (CodeInstruction op in code)
        {
            if (op.operand is "Overhaullib")
            {
                op.operand = "OverhaullibLegacyCompat";
            }

            yield return op;
        }
    }
}

// it was spamming warnings every tick when holding a bow for a while, move that to verbose debug
[HarmonyPatch(typeof(CombatOverhaulBridge), nameof(CombatOverhaulBridge.SetReticleVisible))]
public static class QuietReticlePatch
{
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
    {
        MethodInfo verboseDebug = AccessTools.Method(typeof(ILogger), "VerboseDebug", new[] { typeof(string) });

        foreach (CodeInstruction op in code)
        {
            if (op.operand is MethodInfo { Name: "Warning" })
            {
                op.operand = verboseDebug;
            }

            yield return op;
        }
    }
}

// the fork aims a little above the crosshair, it forgot that
[HarmonyPatch(typeof(PatchAimingData), nameof(PatchAimingData.GetRealProjectileDirection))]
public static class ZeroingPatch
{
    public static void Postfix(EntityAgent entity, ref (Vec3d, Vec3d, float) __result)
    {
        Item? item = entity.RightHandItemSlot?.Itemstack?.Item;
        CombatOverhaulBridge? bridge = TrajectoryModSystem.COBridge;

        // only CO weapons while actually aiming
        if (item == null || bridge == null)
        {
            return;
        }
        if (!bridge.IsCOItem(item) || !bridge.IsAiming())
        {
            return;
        }

        Vec3d direction = __result.Item2;

        // how far it goes sideways, straight up or down has nothing to tilt
        double flat = Math.Sqrt(direction.X * direction.X + direction.Z * direction.Z);
        if (flat < 0.0001)
        {
            return;
        }

        // tilt it up by the zeroing angle, keeping the same heading
        double angle = Math.Atan2(direction.Y, flat);
        angle += CombatOverhaul.Zeroing(item) * GameMath.DEG2RAD;

        double x = direction.X / flat * Math.Cos(angle);
        double y = Math.Sin(angle);
        double z = direction.Z / flat * Math.Cos(angle);

        __result.Item2 = new Vec3d(x, y, z);
    }
}

// speeds and gravity were eyeballed for old CO, take them from the weapon and its projectile instead
// (spears_thrown_distance comes along for free, it edits the spear projectile)
[HarmonyPatch(typeof(TrajectoryPhysics), nameof(TrajectoryPhysics.For))]
public static class CombatOverhaulPhysicsPatch
{
    public static void Postfix(Item item, bool isCOItem, TrajectoryPhysics __result)
    {
        if (!isCOItem || Core.Capi == null)
        {
            return;
        }

        __result.Velocity = CombatOverhaul.Speed(item);

        // find the projectile's physics behavior and copy its gravity and drag
        EntityProperties? projectile = Core.Capi.World.GetEntityType(new AssetLocation(CombatOverhaul.Projectile(item)));
        JsonObject[] behaviors = projectile?.Client?.BehaviorsAsJsonObj ?? Array.Empty<JsonObject>();

        foreach (JsonObject behavior in behaviors)
        {
            if (behavior["code"].AsString() != "CombatOverhaul:ProjectilePhysics")
            {
                continue;
            }

            double gravityFactor = behavior["gravityFactor"].AsDouble(1);
            double dragFactor = behavior["airDragFactor"].AsDouble(1);

            // same math the game uses for passive physics
            __result.GravityPerSecond = GlobalConstants.GravityPerSecond * gravityFactor;
            __result.AirDragValue = 1 - (1 - GlobalConstants.AirDragAlways) * dragFactor;
        }
    }
}
