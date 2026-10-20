using System;
using System.Collections.Generic;
using System.Linq;
using AutoRim.Bridge;
using AutoRim.Core;
using RimWorld;
using Verse;

namespace AutoRim.Commands
{
    internal static class BillFilterHelpers
    {
        private static readonly string[] FilterFields = {
            "reset", "allowDefs", "disallowDefs", "allowCategories", "disallowCategories",
            "allowTainted", "allowClean", "hitPoints", "quality"
        };

        internal static void CheckFields(JsonValue value, params string[] fields)
        {
            if (value.Type != JsonType.Object) throw CommandException.BadArgs("Expected a JSON object.");
            foreach (var field in value.Fields)
                if (!fields.Contains(field.Key)) throw CommandException.BadArgs($"Unsupported field '{field.Key}'.");
        }

        internal static float Number(JsonValue value, string name, float min, float max, bool integer = false)
        {
            double n = value.AsDouble(double.NaN);
            if (value.Type != JsonType.Number || double.IsNaN(n) || double.IsInfinity(n) || n < min || n > max || (integer && n != Math.Floor(n)))
                throw CommandException.BadArgs($"'{name}' must be {(integer ? "an integer" : "a number")} from {min} to {max}.");
            return (float)n;
        }

        internal static bool Boolean(JsonValue value, string name)
        {
            if (value.Type != JsonType.Bool) throw CommandException.BadArgs($"'{name}' must be a boolean.");
            return value.AsBool();
        }

        internal static IntRange SkillRange(JsonValue value)
        {
            CheckFields(value, "min", "max");
            int min = (int)Number(value["min"], "allowedSkillRange.min", 0, 20, true);
            int max = (int)Number(value["max"], "allowedSkillRange.max", 0, 20, true);
            if (min > max) throw CommandException.BadArgs("allowedSkillRange.min must not exceed max.");
            return new IntRange(min, max);
        }

        private static IEnumerable<string> Strings(JsonValue parent, string name)
        {
            if (!parent.Has(name)) return Enumerable.Empty<string>();
            var value = parent[name];
            if (value.Type != JsonType.Array || value.Count > 1000)
                throw CommandException.BadArgs($"'{name}' must be an array of at most 1000 defNames.");
            var result = new List<string>();
            foreach (var item in value.Items)
            {
                if (item.Type != JsonType.String || string.IsNullOrWhiteSpace(item.AsString()))
                    throw CommandException.BadArgs($"'{name}' must contain nonempty defName strings.");
                result.Add(item.AsString());
            }
            return result.Distinct(StringComparer.Ordinal);
        }

        private static T Exact<T>(string name) where T : Def
        {
            var def = DefDatabase<T>.GetNamedSilentFail(name);
            if (def == null) throw CommandException.BadArgs($"Unknown {typeof(T).Name} '{name}'. Use exact defNames.");
            return def;
        }

        internal static HashSet<ThingDef> RecipeAllowed(Bill bill)
        {
            var result = new HashSet<ThingDef>();
            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
                if ((bill.recipe.fixedIngredientFilter == null || bill.recipe.fixedIngredientFilter.Allows(def)) &&
                    bill.recipe.ingredients != null && bill.recipe.ingredients.Any(i => i.filter != null && i.filter.Allows(def))) result.Add(def);
            return result;
        }

        // This changes only an isolated copy. The caller commits it once every requested
        // bill/filter field has passed validation, so a typo cannot partly alter the bill.
        internal static ThingFilter Prepare(Bill bill, JsonValue args)
        {
            CheckFields(args, FilterFields);
            if (args.Count == 0) throw CommandException.BadArgs("ingredientFilter must contain at least one change.");
            var filter = new ThingFilter();
            filter.CopyAllowancesFrom(bill.ingredientFilter);
            filter.AllowedHitPointsPercents = bill.ingredientFilter.AllowedHitPointsPercents;
            filter.AllowedQualityLevels = bill.ingredientFilter.AllowedQualityLevels;
            var eligible = RecipeAllowed(bill);
            if ((args.Has("allowTainted") || args.Has("allowClean")) && !eligible.Any(d => d.IsApparel))
                throw CommandException.BadArgs("allowTainted and allowClean apply only to recipes accepting apparel.");
            if (args.Has("reset"))
            {
                if (args["reset"].Type != JsonType.String) throw CommandException.BadArgs("ingredientFilter.reset must be nothing or recipe.");
                string reset = args["reset"].AsString();
                if (reset != "nothing" && reset != "recipe") throw CommandException.BadArgs("ingredientFilter.reset must be nothing or recipe.");
                filter.SetDisallowAll();
                if (reset == "recipe")
                    foreach (var def in eligible) filter.SetAllow(def, true);
                // A whitelist reset changes ingredient types only. Preserve existing
                // special rules (notably "tainted only") unless explicitly requested.
                foreach (var special in DefDatabase<SpecialThingFilterDef>.AllDefsListForReading)
                    filter.SetAllow(special, bill.ingredientFilter.Allows(special));
            }
            foreach (var name in Strings(args, "allowDefs"))
            {
                var def = Exact<ThingDef>(name);
                if (!eligible.Contains(def)) throw CommandException.BadArgs($"'{name}' is not an ingredient of {bill.recipe.defName}.");
                filter.SetAllow(def, true);
            }
            foreach (var name in Strings(args, "allowCategories"))
            {
                var category = Exact<ThingCategoryDef>(name);
                var members = eligible.Where(d => d.IsWithinCategory(category)).ToList();
                if (members.Count == 0) throw CommandException.BadArgs($"Category '{name}' has no ingredients allowed by this recipe.");
                foreach (var def in members) filter.SetAllow(def, true);
            }
            foreach (var name in Strings(args, "disallowDefs")) filter.SetAllow(Exact<ThingDef>(name), false);
            foreach (var name in Strings(args, "disallowCategories"))
            {
                var category = Exact<ThingCategoryDef>(name);
                foreach (var def in filter.AllowedThingDefs.Where(d => d.IsWithinCategory(category)).ToList()) filter.SetAllow(def, false);
            }
            if (args.Has("allowTainted")) filter.SetAllow(Exact<SpecialThingFilterDef>("AllowDeadmansApparel"), Boolean(args["allowTainted"], "allowTainted"));
            if (args.Has("allowClean")) filter.SetAllow(Exact<SpecialThingFilterDef>("AllowNonDeadmansApparel"), Boolean(args["allowClean"], "allowClean"));
            ApplyRanges(filter, args);
            // Existing recipe-specific special restrictions continue to apply independently.
            foreach (var def in filter.AllowedThingDefs.Where(d => !eligible.Contains(d)).ToList()) filter.SetAllow(def, false);
            return filter;
        }

        internal static void ApplyRanges(ThingFilter filter, JsonValue args)
        {
            if (args.Has("hitPoints"))
            {
                var range = args["hitPoints"];
                CheckFields(range, "min", "max");
                float min = Number(range["min"], "hitPoints.min", 0, 1), max = Number(range["max"], "hitPoints.max", 0, 1);
                if (min > max) throw CommandException.BadArgs("hitPoints.min must not exceed max.");
                filter.AllowedHitPointsPercents = new FloatRange(min, max);
            }
            if (args.Has("quality"))
            {
                var range = args["quality"];
                CheckFields(range, "min", "max");
                QualityCategory min, max;
                if (range["min"].Type != JsonType.String || range["max"].Type != JsonType.String ||
                    !Enum.GetNames(typeof(QualityCategory)).Contains(range["min"].AsString()) ||
                    !Enum.GetNames(typeof(QualityCategory)).Contains(range["max"].AsString()) ||
                    !Enum.TryParse(range["min"].AsString(), false, out min) || !Enum.IsDefined(typeof(QualityCategory), min) ||
                    !Enum.TryParse(range["max"].AsString(), false, out max) || !Enum.IsDefined(typeof(QualityCategory), max) || min > max)
                    throw CommandException.BadArgs("quality must have ordered min/max names from Awful through Legendary.");
                filter.AllowedQualityLevels = new QualityRange(min, max);
            }
        }

        internal static JsonValue Describe(Bill bill)
        {
            var filter = bill.ingredientFilter;
            var defs = JsonValue.NewArray();
            foreach (var def in filter.AllowedThingDefs.OrderBy(d => d.defName)) defs.Add(def.defName);
            var eligible = JsonValue.NewArray();
            foreach (var def in RecipeAllowed(bill).OrderBy(d => d.defName)) eligible.Add(def.defName);
            var special = JsonValue.NewArray();
            foreach (var def in DefDatabase<SpecialThingFilterDef>.AllDefsListForReading.OrderBy(d => d.defName))
                special.Add(JsonValue.NewObject().Set("defName", def.defName).Set("allowed", filter.Allows(def)));
            return JsonValue.NewObject()
                .Set("allowedDefs", defs)
                .Set("recipeAllowedDefs", eligible)
                .Set("specialFilters", special)
                .Set("allowTainted", filter.Allows(Exact<SpecialThingFilterDef>("AllowDeadmansApparel")))
                .Set("allowClean", filter.Allows(Exact<SpecialThingFilterDef>("AllowNonDeadmansApparel")))
                .Set("hitPoints", JsonValue.NewObject().Set("min", filter.AllowedHitPointsPercents.min).Set("max", filter.AllowedHitPointsPercents.max))
                .Set("quality", JsonValue.NewObject().Set("min", filter.AllowedQualityLevels.min.ToString()).Set("max", filter.AllowedQualityLevels.max.ToString()));
        }
    }
}
