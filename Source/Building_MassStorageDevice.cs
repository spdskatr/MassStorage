﻿using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace StockpileAugmentations
{
    public class Building_MassStorageDevice : Building, IStoreSettingsParent
    {
        public StorageSettings settings;
        public int ThingCount;
        public ThingDef storedDef;
        public float rotProgressInt;
        public bool StorageTabVisible
        {
            get
            {
                return true;
            }
        }
        public StorageSettings GetStoreSettings()
        {
            return settings;
        }
        public StorageSettings GetParentStoreSettings()
        {
            return def.building.fixedStorageSettings;
        }
        public Zone_Stockpile ResidingZone
        {
            get
            {
                return (Position.GetZone(Map) != null && Position.GetZone(Map) is Zone_Stockpile) ? Position.GetZone(Map) as Zone_Stockpile : null;
            }
        }
        public ThingDef internalStoredDef
        {
            get
            {
                return storedDef;
            }
        }
        public int maxCount
        {
            get
            {
                return 2147483647;
            }
        }
        public CompPowerTrader PowerTraderComp
        {
            get
            {
                return GetComp<CompPowerTrader>();
            }
        }
        public int ItemsStoredExternally
        {
            get
            {
                int i = 0;
                GenAdj.CellsOccupiedBy(this).ToList().ForEach(c => c.GetThingList(Map).FindAll(t => t.def.category == ThingCategory.Item).ForEach(t => i += t.stackCount));
                return i;
            }
        }
        public bool StoredIsRottable
        {
            get
            {
                if (storedDef == null) return false;
                return storedDef.GetCompProperties<CompProperties_Rottable>() != null;
            }
        }
        public int TicksUntilRotAtCurrentTemp
        {
            get
            {
                float num = GenTemperature.GetTemperatureForCell(Position, Map);
                num = Mathf.RoundToInt(num);
                float num2 = GenTemperature.RotRateAtTemperature(num);
                if (num2 <= 0f)
                {
                    return 2147483647;
                }
                float num3 = storedDef.GetCompProperties<CompProperties_Rottable>().TicksToRotStart - rotProgressInt;
                if (num3 <= 0f)
                {
                    return 0;
                }
                return Mathf.RoundToInt(num3 / num2);
            }
        }
        public override string GetInspectString()
        {
            string thingName = (storedDef != null) ? ThingCount.ToString() + "x " + storedDef.label.CapitalizeFirst() : "nothing";
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.AppendLine(base.GetInspectString());
            stringBuilder.AppendFormat("In internal storage: {0} (Item(s) stored externally: {1})", thingName, ItemsStoredExternally);
            if (StoredIsRottable)
            {
                stringBuilder.AppendLine();
                stringBuilder.AppendFormat("Spoils in: {0}", TicksUntilRotAtCurrentTemp.ToStringTicksToPeriodVagueMax());
            }
            return stringBuilder.ToString();
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ThingCount, "thingCount");
            Scribe_Values.Look(ref rotProgressInt, "rotProgress");
            Scribe_Defs.Look(ref storedDef, "storedDef");
            Scribe_Deep.Look(ref settings, "settings", this);
        }
        public override void DeSpawn()
        {
            DropAll();
            base.DeSpawn();
        }
        public override void PostMake()
        {
            base.PostMake();
            settings = new StorageSettings(this);
            if (def.building.defaultStorageSettings != null)
            {
                settings.CopyFrom(def.building.defaultStorageSettings);
            }
        }
        public override void Tick()
        {
            base.Tick();
            float powerMultiplier = (ThingCount > 1) ? (int)Math.Floor(Math.Log10(ThingCount)) : 0;
            PowerTraderComp.powerOutputInt = (float)(Math.Pow(2, powerMultiplier)) * -1 * def.GetCompProperties<CompProperties_Power>().basePowerConsumption;
            if (!PowerTraderComp.PowerOn) return;
            if (Find.TickManager.TicksGame % 40 == 0)
            {
                //Code executed 40 times less often
                List<IntVec3> clist = GenAdj.CellsOccupiedBy(this).ToList();
                CheckIfStorageInvalid(clist);
                TrySpawnItemsAtOutput(clist);
                if (ResidingZone != null)
                {
                    CollectApplicableItems();
                    TickRottables();
                }
            }
        }

        public void TickRottables()
        {
            if (storedDef == null || Find.TickManager.TicksAbs % 250 != 0) return;
            if (StoredIsRottable)
            {
                float rotProgress = rotProgressInt;
                float num = 1f;
                float temperatureForCell = GenTemperature.GetTemperatureForCell(Position, Map);
                num *= GenTemperature.RotRateAtTemperature(temperatureForCell);
                rotProgressInt += Mathf.Round(num * 250f);
                if (rotProgressInt >= storedDef.GetCompProperties<CompProperties_Rottable>().TicksToRotStart)
                {
                    Messages.Message("MessageRottedAwayInStorage".Translate(storedDef.label).CapitalizeFirst(), MessageTypeDefOf.SilentInput);
                    storedDef = null;
                    ThingCount = 0;
                    rotProgressInt = 1;
                }
            }
            else
            {
                rotProgressInt = 0;
            }
        }

        public void CollectApplicableItems()
        {
            foreach (IntVec3 cell in ResidingZone.cells)
            {
                if (GenAdj.CellsOccupiedBy(this).Any(c => c == cell)) continue;
                QualityCategory qc;
                List<Thing> thingsAtCell = (from Thing t in cell.GetThingList(Map)
                                            where t.def.category == ThingCategory.Item && !(t is Corpse) && t.def.EverHaulable && !t.TryGetQuality(out qc) && !t.def.MadeFromStuff && ((t.TryGetComp<CompForbiddable>() != null) ? !t.TryGetComp<CompForbiddable>().Forbidden : true)
                                            select t).ToList();
                foreach (Thing t in thingsAtCell)
                {
                    if (!settings.AllowedToAccept(t) || cell.GetThingList(Map).Any(u => u is Building_MassStorageDevice && (u as Building_MassStorageDevice).storedDef == t.def)) continue;
                    AcceptItem(t);
                }
            }
        }

        public void TrySpawnItemsAtOutput(List<IntVec3> clist)
        {
            foreach (IntVec3 cell in clist)
            {
                if (ThingCount <= 0) continue;
                Thing thing = cell.GetThingList(Map).Find(t => t.def == storedDef);
                bool any = cell.GetThingList(Map).Any(t => t.def.category == ThingCategory.Item);
                if (thing != null)
                {
                    int potential = thing.def.stackLimit - thing.stackCount;
                    if (potential > ThingCount)
                    {
                        thing.stackCount += ThingCount;
                        ThingCount = 0;
                        continue;
                    }
                    ThingCount -= potential;
                    thing.stackCount += potential;
                    continue;
                }
                if (!any && storedDef != null)
                {
                    Thing t = ThingMaker.MakeThing(storedDef);
                    if (t.def.stackLimit > ThingCount)
                    {
                        t.stackCount = ThingCount;
                        ThingCount = 0;
                    }
                    else
                    {
                        ThingCount -= t.def.stackLimit;
                        t.stackCount = t.def.stackLimit;
                    }
                    GenPlace.TryPlaceThing(t, cell, Map, ThingPlaceMode.Direct);
                }

            }
        }

        public void CheckIfStorageInvalid(List<IntVec3> clist)
        {
            if (clist.FindAll(intvec => intvec.GetFirstItem(Map) != null).NullOrEmpty() && storedDef != null && ThingCount <= 0)
            {
                storedDef = null;
            }
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos()) yield return g;
            foreach (Gizmo g2 in StorageSettingsClipboard.CopyPasteGizmosFor(settings))
            {
                yield return g2;
            }
            if (Prefs.DevMode)
            {
                yield return new Command_Action
                {
                    icon = ContentFinder<Texture2D>.Get("UI/Buttons/Drop", true),
                    defaultLabel = "DEBUG: Drop all items",
                    defaultDesc = "Drops all items stored in internal storage and disallows the item in storage. WARNING: Some items will be lost if storage exceeds ~300 stacks.",
                    action = () => DropAll(),
                    activateSound = SoundDefOf.Click,
                };
                yield return new Command_Action
                {
                    defaultLabel = "DEBUG: Add 1 million of current item",
                    defaultDesc = "If no item stored, adds Steel.",
                    action = delegate
                    {
                        if (storedDef == null) storedDef = ThingDefOf.Steel;
                        ThingCount += 1000000;
                    },
                    activateSound = SoundDefOf.Click,
                };
                yield return new Command_Action
                {
                    defaultLabel = "DEBUG: Reset without dropping items",
                    action = delegate
                    {
                        storedDef = null;
                        ThingCount = 0;
                    },
                    activateSound = SoundDefOf.Click,
                };
            }
        }
        public virtual void AcceptItem (Thing t)
        {
            if (storedDef == null)
            {
                ThingCount = t.stackCount;
                storedDef = t.def;
                if (t.TryGetComp<CompRottable>() != null)
                {
                    float ratio = t.stackCount / (ThingCount + t.stackCount);
                    rotProgressInt = Mathf.Lerp(rotProgressInt, t.TryGetComp<CompRottable>().RotProgress, ratio);
                }
                t.Destroy();
                t.def.soundDrop.PlayOneShot(SoundInfo.InMap(new TargetInfo(Position, Map, false), MaintenanceType.None));
                return;
            }
            if (storedDef == t.def)
            {
                if (StoredIsRottable)
                {
                    float ratio = t.stackCount / (ThingCount + t.stackCount);
                    rotProgressInt = Mathf.Lerp(rotProgressInt, t.TryGetComp<CompRottable>().RotProgress, ratio);
                }
                ThingCount += t.stackCount;
                t.Destroy();
                t.def.soundDrop.PlayOneShot(SoundInfo.InMap(new TargetInfo(Position, Map), MaintenanceType.None));
                return;
            }
        }
        public void DropAll(bool disableItem = false)
        {
            if (storedDef == null)
            {
                ThingCount = 0;
                return;
            }
            while (ThingCount > 0)
            {
                Thing t = ThingMaker.MakeThing(storedDef);
                t.stackCount = (ThingCount > t.stackCount) ? t.def.stackLimit : ThingCount;
                ThingCount -= t.stackCount;
                GenPlace.TryPlaceThing(t, Position, Map, ThingPlaceMode.Near);
            }
            if (disableItem) settings.filter.SetAllow(storedDef, false);
            storedDef = null;
        }
    }
}
