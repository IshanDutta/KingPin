#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace FS_Core
{
    public static class AIStateMachineGenerator
    {
        const string BaseAssetResourcePath = "AI State Machines/Base AI State Machine";
        const string DefaultGeneratedFolder = "Assets/Generated/AI State Machines";

        // Builds a fresh StateMachineAsset by cloning the Base AI State Machine and applying
        // a patch for each selected module. Returns the cloned asset, ready to be assigned
        // to an AIController. Caller is responsible for ensuring the destination directory exists.
        public static StateMachineAsset GenerateForModules(IEnumerable<string> selectedSystemNames, string assetPath)
        {
            var baseAsset = Resources.Load<StateMachineAsset>(BaseAssetResourcePath);
            if (baseAsset == null)
            {
                Debug.LogError($"AIStateMachineGenerator: could not load base asset at Resources/{BaseAssetResourcePath}");
                return null;
            }

            var dir = assetPath.Substring(0, assetPath.LastIndexOf('/'));
            EnsureFolderExists(dir);
            assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);

            var sourcePath = AssetDatabase.GetAssetPath(baseAsset);
            if (!AssetDatabase.CopyAsset(sourcePath, assetPath))
            {
                Debug.LogError($"AIStateMachineGenerator: failed to copy {sourcePath} → {assetPath}");
                return null;
            }
            AssetDatabase.ImportAsset(assetPath);
            var clone = AssetDatabase.LoadAssetAtPath<StateMachineAsset>(assetPath);
            if (clone == null)
            {
                Debug.LogError($"AIStateMachineGenerator: failed to load cloned asset at {assetPath}");
                return null;
            }

            var ctx = new PatchContext(clone);
            var selectedSet = new HashSet<string>(selectedSystemNames ?? Enumerable.Empty<string>());
            var patches = DiscoverPatches()
                .Where(p => selectedSet.Contains(p.attr.SystemName))
                .OrderBy(p => p.attr.Order)
                .ToList();

            foreach (var p in patches)
            {
                try
                {
                    p.instance.Apply(ctx);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"AIStateMachineGenerator: patch '{p.attr.SystemName}' threw: {ex}");
                }
            }

            EditorUtility.SetDirty(clone);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(assetPath);
            return AssetDatabase.LoadAssetAtPath<StateMachineAsset>(assetPath);
        }

        public static string DefaultAssetPathFor(string characterName)
        {
            var safe = string.IsNullOrEmpty(characterName) ? "AI" : characterName;
            return $"{DefaultGeneratedFolder}/{safe} StateMachine.asset";
        }

        // ----- internals -----

        struct DiscoveredPatch
        {
            public AIStateMachinePatchAttribute attr;
            public IAIStateMachinePatch instance;
        }

        static IEnumerable<DiscoveredPatch> DiscoverPatches()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }

                foreach (var t in types)
                {
                    if (t == null || t.IsAbstract || t.IsInterface) continue;
                    if (!typeof(IAIStateMachinePatch).IsAssignableFrom(t)) continue;
                    var attr = t.GetCustomAttribute<AIStateMachinePatchAttribute>();
                    if (attr == null) continue;

                    IAIStateMachinePatch instance = null;
                    try { instance = (IAIStateMachinePatch)Activator.CreateInstance(t); }
                    catch (Exception ex) { Debug.LogError($"AIStateMachineGenerator: failed to instantiate {t.FullName}: {ex.Message}"); continue; }

                    yield return new DiscoveredPatch { attr = attr, instance = instance };
                }
            }
        }

        static void EnsureFolderExists(string assetFolderPath)
        {
            if (string.IsNullOrEmpty(assetFolderPath))
                return;

            assetFolderPath = assetFolderPath.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(assetFolderPath))
                return;

            var parts = assetFolderPath.Split('/');
            var current = parts[0]; // typically "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
#endif
