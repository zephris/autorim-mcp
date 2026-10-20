using System.Collections.Generic;
using System.Linq;
using AutoRim.Bridge;
using AutoRim.Core;
using Verse;

namespace AutoRim.Commands
{
    internal static class StorageFilterHelpers
    {
        private static readonly Dictionary<string, string> Switches = new Dictionary<string, string> {
            {"allowTainted", "AllowDeadmansApparel"},
            {"allowClean", "AllowNonDeadmansApparel"},
            {"allowBiocodedWeapons", "AllowBiocodedWeapons"},
            {"allowNonBiocodedWeapons", "AllowNonBiocodedWeapons"},
            {"allowBiocodedApparel", "AllowBiocodedApparel"},
            {"allowNonBiocodedApparel", "AllowNonBiocodedApparel"}
        };

        internal static ThingFilter Prepare(ThingFilter original, JsonValue args)
        {
            BillFilterHelpers.CheckFields(args, Switches.Keys.Concat(new[] {"quality", "hitPoints"}).ToArray());
            if (args.Count == 0) throw CommandException.BadArgs("filter must contain at least one change.");
            var copy = new ThingFilter();
            copy.CopyAllowancesFrom(original);
            copy.AllowedHitPointsPercents = original.AllowedHitPointsPercents;
            copy.AllowedQualityLevels = original.AllowedQualityLevels;
            foreach (var special in DefDatabase<SpecialThingFilterDef>.AllDefsListForReading)
                copy.SetAllow(special, original.Allows(special));
            BillFilterHelpers.ApplyRanges(copy, args);
            foreach (var entry in Switches)
            {
                if (!args.Has(entry.Key)) continue;
                var def = DefDatabase<SpecialThingFilterDef>.GetNamedSilentFail(entry.Value);
                if (def == null) throw CommandException.BadArgs($"Special filter '{entry.Value}' is unavailable in this game.");
                copy.SetAllow(def, BillFilterHelpers.Boolean(args[entry.Key], entry.Key));
            }
            return copy;
        }

        internal static JsonValue Describe(ThingFilter filter)
        {
            var defs = JsonValue.NewArray();
            foreach (var def in filter.AllowedThingDefs.OrderBy(d => d.defName)) defs.Add(def.defName);
            var result = JsonValue.NewObject().Set("allowedDefs", defs)
                .Set("hitPoints", JsonValue.NewObject().Set("min", filter.AllowedHitPointsPercents.min).Set("max", filter.AllowedHitPointsPercents.max))
                .Set("quality", JsonValue.NewObject().Set("min", filter.AllowedQualityLevels.min.ToString()).Set("max", filter.AllowedQualityLevels.max.ToString()));
            foreach (var entry in Switches)
            {
                var def = DefDatabase<SpecialThingFilterDef>.GetNamedSilentFail(entry.Value);
                if (def != null) result.Set(entry.Key, filter.Allows(def));
            }
            return result;
        }
    }

    public class StorageSetFilterCommand : CommandBase
    {
        public override string Name => "storage.set_filter";
        public override string Description => "Edits storage quality, durability, taint and biocoding restrictions; omitted fields are preserved.";
        public override JsonValue Execute(JsonValue args)
        {
            var settings = StorageHelpers.ResolveSettings(args, GameState.RequireMap(), out string label);
            var prepared = StorageFilterHelpers.Prepare(settings.filter, args["filter"]);
            settings.filter.CopyAllowancesFrom(prepared);
            settings.filter.AllowedHitPointsPercents = prepared.AllowedHitPointsPercents;
            settings.filter.AllowedQualityLevels = prepared.AllowedQualityLevels;
            return JsonValue.NewObject().Set("target", label).Set("filter", StorageFilterHelpers.Describe(settings.filter));
        }
    }
}
