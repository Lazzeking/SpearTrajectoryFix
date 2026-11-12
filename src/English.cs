using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SpearTrajectory.Bridge;
using SpearTrajectory.Systems;
using Vintagestory.API.Client;

namespace ProjectileTrajectoryFix;

// its log is in spanish, same messages in english
[HarmonyPatch]
public static class EnglishLogPatch
{
    // spanish text -> english text, has to match exactly
    private static readonly Dictionary<string, string> English = new()
    {
        ["[SpearTrajectory] CO está instalado pero falló la integración: "] = "[ST] Combat Overhaul is installed but linking up with it failed: ",
        ["[ST] No se encontró overhaullib."] = "[ST] Overhaul lib not found.",
        ["[ST] Integración con Combat Overhaul activada."] = "[ST] Combat Overhaul support on.",
        ["Cambiando reticle visibility.."] = "[ST] Hiding Combat Overhaul's crosshair",
        ["[ST] Error leyendo combatoverhaul.yaml: "] = "[ST] Could not read combatoverhaul.yaml: ",
    };

    // the methods those messages are in
    // not StartClientSide! it calls PatchAll() with no assembly and crashes if we touch it
    public static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Constructor(typeof(CombatOverhaulBridge), new[] { typeof(ICoreClientAPI) });
        yield return AccessTools.Method(typeof(CombatOverhaulBridge), "InitReflection");
        yield return AccessTools.Method(typeof(CombatOverhaulBridge), nameof(CombatOverhaulBridge.SetReticleVisible));
        yield return AccessTools.Method(typeof(CombatOverhaulBridge), nameof(CombatOverhaulBridge.GetSpearsThrownDistance));
    }

    // swap every spanish string we know for the english one
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
    {
        foreach (CodeInstruction op in code)
        {
            if (op.operand is string spanish && English.TryGetValue(spanish, out string? english))
            {
                op.operand = english;
            }

            yield return op;
        }
    }
}

// well they were left in, lets remove them
// (its Start does nothing but print "Hello from template mod")
[HarmonyPatch(typeof(TrajectoryModSystem), nameof(TrajectoryModSystem.Start))]
public static class NoHelloPatch
{
    public static bool Prefix()
    {
        return false;
    }
}
