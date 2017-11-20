using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using RimWorld;
using Harmony;

namespace StockpileAugmentations
{
    [StaticConstructorOnStartup]
    public static class OnStartup
    {
        static OnStartup()
        {
            LongEventHandler.QueueLongEvent(Patcher, "Running patches", false, null);
        }
        static void Patcher()
        {
            HarmonyInstance harmony = HarmonyInstance.Create("net.spdskatr.factoryframework.patches");
            harmony.Patch(typeof(ResourceCounter).GetMethod(nameof(ResourceCounter.UpdateResourceCounts)), null, new HarmonyMethod(typeof(OnStartup), nameof(ResourceCounterPostfix)));
        }
        static void ResourceCounterPostfix(ResourceCounter __instance)
        {
            Dictionary<ThingDef, int> countedAmounts = Traverse.Create(__instance).Field("countedAmounts").GetValue<Dictionary<ThingDef, int>>();
            Map map = Traverse.Create(__instance).Field("map").GetValue<Map>();

            try
            {
                map.listerBuildings.allBuildingsColonist.OfType<Building_MassStorageDevice>().ToList()
                    .FindAll((Building_MassStorageDevice b) => b.internalStoredDef != null && b.ThingCount > 0)
                    .ForEach((Building_MassStorageDevice storage) =>
                    {
                        if (storage.internalStoredDef.CountAsResource) countedAmounts[storage.internalStoredDef] += storage.ThingCount;
                    });
            }
            catch (Exception ex)
            {
                Log.Error("SS Mass Storage caught exception while editing resource counts: " + ex.ToString());
            }
            finally
            {
                Traverse.Create(__instance).Field("countedAmounts").SetValue(countedAmounts);
            }
        }
    }
}
