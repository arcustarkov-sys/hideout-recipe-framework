using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;

namespace SealgerHideoutRecipeFramework
{
    /// <summary>
    /// Result for one loaded file:
    /// - If Node != null, parse succeeded.
    /// - If Error != null, parse failed and you should NOT use Node.
    /// </summary>
    public sealed class LoadedRecipeFile
    {
        public string FilePath { get; init; } = "";
        public JsonNode? Node { get; init; }
        public string? Error { get; init; }
    }

    public static class RecipeFileLoader
    {
       
        public static string GetModDirectory()
        {
           
            string dllPath = Assembly.GetExecutingAssembly().Location;

           
            string? modDir = Path.GetDirectoryName(dllPath);

            if (string.IsNullOrWhiteSpace(modDir))
                throw new Exception("Could not determine mod directory from assembly location.");

            return modDir;
        }

        
        public static List<LoadedRecipeFile> LoadAllRecipeJsonFiles()
        {
            var results = new List<LoadedRecipeFile>();

            string modDir = GetModDirectory();
            string recipesDir = Path.Combine(modDir, "Recipes");

            // If the folder doesn't exist, just return empty (no recipes).
            if (!Directory.Exists(recipesDir))
            {
                results.Add(new LoadedRecipeFile
                {
                    FilePath = recipesDir,
                    Node = null,
                    Error = "Recipes directory not found. Create: <YourModFolder>/Recipes/"
                });
                return results;
            }

            // Get all JSON files in Recipes/ and any subfolders.
            string[] files = Directory.GetFiles(recipesDir, "*.json", SearchOption.AllDirectories);

            // If there are no files, that’s not an error — it just means user hasn’t added any.
            if (files.Length == 0)
                return results;

            foreach (string file in files)
            {
                try
                {
                    // 1) Read text from file
                    string text = File.ReadAllText(file);

                    // 2) Parse into JsonNode
                    JsonNode? node = JsonNode.Parse(text);

                    // If Parse returns null, it's not usable
                    if (node == null)
                    {
                        results.Add(new LoadedRecipeFile
                        {
                            FilePath = file,
                            Node = null,
                            Error = "Parsed JSON returned null (file may be empty)."
                        });
                        continue;
                    }

                    // Must be a JSON OBJECT for a recipe (not just a string/array/number)
                    if (node is not JsonObject)
                    {
                        results.Add(new LoadedRecipeFile
                        {
                            FilePath = file,
                            Node = null,
                            Error = "Top-level JSON must be an object { ... }, not an array [ ... ] or primitive."
                        });
                        continue;
                    }

                    // Success
                    results.Add(new LoadedRecipeFile
                    {
                        FilePath = file,
                        Node = node,
                        Error = null
                    });
                }
                catch (Exception ex)
                {
                    // Any exception here means the JSON was invalid or unreadable.
                    results.Add(new LoadedRecipeFile
                    {
                        FilePath = file,
                        Node = null,
                        Error = ex.Message
                    });
                }
            }

            return results;
        }
    }
}