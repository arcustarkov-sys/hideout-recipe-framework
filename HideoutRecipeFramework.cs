using ArcusHideoutRecipeFramework;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Hideout;
using SPTarkov.Server.Core.Models.Enums.Hideout;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace HideoutRecipeFramework;

public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.hideoutrecipeframework.core";
    public override string Name { get; init; } = "Hideout Recipe Framework";
    public override string Author { get; init; } = "Arcus56";

    public override List<string>? Contributors { get; init; } = null;
    public override SemanticVersioning.Version Version { get; init; } = new("1.0.0");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.8");

    public override List<string>? Incompatibilities { get; init; } = null;
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; } = null;

    public override string? Url { get; init; } = null;
    public override bool? IsBundleMod { get; init; } = false;

    public override string License { get; init; } = "MIT";
}

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 1)]
public class HideoutRecipeFramework(
    ISptLogger<HideoutRecipeFramework> logger,
    DatabaseService databaseService
) : IOnLoad
{
    private const string LogPrefix = "[HRF]";

    public Task OnLoad()
    {
        var hideout = databaseService.GetHideout();
        var recipes = hideout.Production.Recipes;

        if (recipes == null)
        {
            logger.Error($"{LogPrefix} hideout.Production.Recipes is null");
            return Task.CompletedTask;
        }

        logger.Info($"{LogPrefix} Vanilla recipe count: {recipes.Count}");

        var loadedFiles = RecipeFileLoader.LoadAllRecipeJsonFiles();
        var existingObjectIds = new HashSet<string>(recipes.Select(r => r.Id.ToString()));
        var injected = 0;

     

        foreach (var file in loadedFiles)
        {
            if (file.Error != null || file.Node is not JsonObject obj)
            {
                logger.Error($"{LogPrefix} Invalid JSON: {file.FilePath} :: {file.Error}");
                continue;
            }

            var friendlyId = obj["_id"]?.GetValue<string>();
            var endProduct = obj["endProduct"]?.GetValue<string>();

            if (string.IsNullOrWhiteSpace(friendlyId) || string.IsNullOrWhiteSpace(endProduct))
            {
                logger.Error($"{LogPrefix} Missing _id or endProduct in {file.FilePath}");
                continue;
            }

            var objectId = ToObjectId24(friendlyId);
            if (existingObjectIds.Contains(objectId))
            {
                logger.Error($"{LogPrefix} Duplicate recipe id '{friendlyId}' (objectId={objectId})");
                continue;
            }

            var areaType = obj["areaType"]?.GetValue<int>() ?? 6;
            var requiredLevel = obj["requiredLevel"]?.GetValue<int>() ?? 1;
            var count = obj["count"]?.GetValue<int>() ?? 1;
            var time = obj["productionTime"]?.GetValue<double>() ?? 60;
            var needFuel = obj["needFuelForAllProductionTime"]?.GetValue<bool>() ?? false;

            var recipe = new HideoutProduction
            {
                Id = new MongoId(objectId),
                AreaType = (HideoutAreas)areaType,
                EndProduct = new MongoId(endProduct),

                Count = count,
                ProductionTime = time,
                NeedFuelForAllProductionTime = needFuel,

                Locked = false,
                Continuous = false,
                IsEncoded = false,
                IsCodeProduction = false,
                ProductionLimitCount = 0,

                Requirements = BuildRequirements(obj, areaType, requiredLevel)
            };

            recipes.Add(recipe);
            existingObjectIds.Add(objectId);
            injected++;

            logger.Success($"{LogPrefix} Injected: {friendlyId} -> {objectId}");
        }

        logger.Success($"{LogPrefix} Added {injected} recipes. Total now: {recipes.Count}");
        return Task.CompletedTask;
    }

    private static List<Requirement> BuildRequirements(JsonObject obj, int areaType, int requiredLevel)
    {
        var reqs = new List<Requirement>
        {
            new Requirement
            {
                Type = "Area",
                AreaType = areaType,
                RequiredLevel = requiredLevel
            }
        };

        if (obj["inputs"] is JsonArray inputs)
        {
            foreach (var n in inputs.OfType<JsonObject>())
            {
                var tpl = n["tpl"]?.GetValue<string>();
                var cnt = n["count"]?.GetValue<int>() ?? 1;

                if (string.IsNullOrWhiteSpace(tpl) || cnt <= 0)
                    continue;

                reqs.Add(new Requirement
                {
                    Type = "Item",
                    TemplateId = tpl,
                    Count = cnt,
                    IsFunctional = false,
                    IsEncoded = false,
                    IsSpawnedInSession = false
                });
            }
        }

        if (obj["tools"] is JsonArray tools)
        {
            foreach (var n in tools.OfType<JsonObject>())
            {
                var tpl = n["tpl"]?.GetValue<string>();
                var cnt = n["count"]?.GetValue<int>() ?? 1;

                if (string.IsNullOrWhiteSpace(tpl) || cnt <= 0)
                    continue;

                reqs.Add(new Requirement
                {
                    Type = "Tool",
                    TemplateId = tpl,
                    Count = cnt,
                    IsFunctional = true,
                    IsEncoded = false,
                    IsSpawnedInSession = false
                });
            }
        }

        if (obj["resources"] is JsonArray resources)
        {
            foreach (var n in resources.OfType<JsonObject>())
            {
                var res = n["resource"]?.GetValue<int>() ?? -1;
                var cnt = n["count"]?.GetValue<int>() ?? 1;

                if (res < 0 || cnt <= 0)
                    continue;

                reqs.Add(new Requirement
                {
                    Type = "Resource",
                    Resource = res,
                    Count = cnt
                });
            }
        }

        return reqs;
    }

    private static string ToObjectId24(string input)
    {
        if (IsValidObjectId24(input))
            return input.ToLowerInvariant();

        using var sha1 = SHA1.Create();
        var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash, 0, 12).ToLowerInvariant();
    }

    private static bool IsValidObjectId24(string s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Length != 24)
            return false;

        foreach (var c in s)
        {
            if (!Uri.IsHexDigit(c))
                return false;
        }

        return true;
    }
}