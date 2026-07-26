#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GameFactory.TemplateTools.Editor
{
    public static class GameFactoryUiCatalogExporter
    {
        private const string DEFAULT_PACK_ID = "gui-pro-casual-game";
        private const string DEFAULT_TEMPLATE_ID = "gui-pro-template";
        private const string DEFAULT_TEMPLATE_REVISION = "2026.07.26";
        private const string DEFAULT_OUTPUT_PATH = "Assets/GameFactory/Generated/ui-asset-catalog.json";
        private const string DEFAULT_TEMPLATE_MARKER_PATH = "ProjectSettings/GameFactoryTemplateBaseline.json";
        private const string DEFAULT_ASSET_ROOT = "Assets/Layer Lab/GUI Pro-CasualGame";
        private const string OUTPUT_PATH_ENV = "GAMEFACTORY_UI_CATALOG_OUTPUT";
        private const string ASSET_ROOT_ENV = "GAMEFACTORY_UI_CATALOG_ROOT";
        private const string PACK_ID_ENV = "GAMEFACTORY_UI_PACK_ID";
        private const string TEMPLATE_ID_ENV = "GAMEFACTORY_UI_TEMPLATE_ID";
        private const string TEMPLATE_REVISION_ENV = "GAMEFACTORY_UI_TEMPLATE_REVISION";
        private const string TEMPLATE_MARKER_ENV = "GAMEFACTORY_UI_TEMPLATE_MARKER";
        private const string MENU_EXPORT_CATALOG = "Tools/Game Factory/UI/Export GUI Pro Catalog";
        private const string MENU_EXPORT_TEMPLATE_MARKER = "Tools/Game Factory/UI/Export Template Baseline Marker";

        [MenuItem(MENU_EXPORT_CATALOG)]
        public static void ExportCatalogMenu()
        {
            ExportCatalogInteractive();
        }

        [MenuItem(MENU_EXPORT_TEMPLATE_MARKER)]
        public static void ExportTemplateMarkerMenu()
        {
            ExportTemplateMarkerInteractive();
        }

        public static void ExportCatalogInteractive()
        {
            string outputPath = EditorUtility.SaveFilePanel(
                "Export GUI Pro UI Catalog",
                Directory.GetCurrentDirectory(),
                Path.GetFileName(DEFAULT_OUTPUT_PATH),
                "json");
            if (string.IsNullOrWhiteSpace(outputPath))
                return;

            ExportCatalog(outputPath, DEFAULT_ASSET_ROOT, DEFAULT_PACK_ID, DEFAULT_TEMPLATE_ID, DEFAULT_TEMPLATE_REVISION, DEFAULT_TEMPLATE_MARKER_PATH);
            EditorUtility.DisplayDialog("Game Factory", $"UI catalog exported to:\n{outputPath}", "OK");
        }

        public static void ExportCatalogBatchmode()
        {
            string outputPath = ReadEnvironment(OUTPUT_PATH_ENV, DEFAULT_OUTPUT_PATH);
            string assetRoot = ReadEnvironment(ASSET_ROOT_ENV, DEFAULT_ASSET_ROOT);
            string packId = ReadEnvironment(PACK_ID_ENV, DEFAULT_PACK_ID);
            string templateId = ReadEnvironment(TEMPLATE_ID_ENV, DEFAULT_TEMPLATE_ID);
            string templateRevision = ReadEnvironment(TEMPLATE_REVISION_ENV, DEFAULT_TEMPLATE_REVISION);
            string templateMarkerPath = ReadEnvironment(TEMPLATE_MARKER_ENV, DEFAULT_TEMPLATE_MARKER_PATH);
            ExportCatalog(outputPath, assetRoot, packId, templateId, templateRevision, templateMarkerPath);
            Debug.Log($"Game Factory UI catalog exported to {outputPath}");
        }

        public static void ExportTemplateMarkerInteractive()
        {
            string outputPath = EditorUtility.SaveFilePanel(
                "Export Template Baseline Marker",
                Directory.GetCurrentDirectory(),
                Path.GetFileName(DEFAULT_TEMPLATE_MARKER_PATH),
                "json");
            if (string.IsNullOrWhiteSpace(outputPath))
                return;

            ExportTemplateMarker(outputPath, DEFAULT_PACK_ID, DEFAULT_TEMPLATE_ID, DEFAULT_TEMPLATE_REVISION);
            EditorUtility.DisplayDialog("Game Factory", $"Template marker exported to:\n{outputPath}", "OK");
        }

        public static void ExportTemplateMarkerBatchmode()
        {
            string outputPath = ReadEnvironment(TEMPLATE_MARKER_ENV, DEFAULT_TEMPLATE_MARKER_PATH);
            string packId = ReadEnvironment(PACK_ID_ENV, DEFAULT_PACK_ID);
            string templateId = ReadEnvironment(TEMPLATE_ID_ENV, DEFAULT_TEMPLATE_ID);
            string templateRevision = ReadEnvironment(TEMPLATE_REVISION_ENV, DEFAULT_TEMPLATE_REVISION);
            ExportTemplateMarker(outputPath, packId, templateId, templateRevision);
            Debug.Log($"Game Factory template marker exported to {outputPath}");
        }

        public static void ExportCatalog(
            string outputFilePath,
            string assetRoot,
            string packId,
            string templateId,
            string templateRevision,
            string templateMarkerPath)
        {
            if (string.IsNullOrWhiteSpace(outputFilePath))
                throw new ArgumentException("Output file path is required.", nameof(outputFilePath));
            if (string.IsNullOrWhiteSpace(assetRoot))
                throw new ArgumentException("Asset root is required.", nameof(assetRoot));

            string normalizedAssetRoot = NormalizeAssetPath(assetRoot);
            string[] assetPaths = AssetDatabase.GetAllAssetPaths()
                .Where(path => path.StartsWith(normalizedAssetRoot, StringComparison.OrdinalIgnoreCase))
                .Where(IsSupportedAssetPath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var assets = new List<AssetRecord>(assetPaths.Length);
            foreach (string assetPath in assetPaths)
            {
                AssetRecord record = BuildAssetRecord(assetPath, normalizedAssetRoot);
                if (record != null)
                    assets.Add(record);
            }

            var catalog = new CatalogExport
            {
                PackId = packId,
                TemplateId = templateId,
                TemplateRevision = templateRevision,
                UnityVersion = Application.unityVersion,
                ExportedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                AssetRoot = normalizedAssetRoot,
                TemplateMarkerPath = templateMarkerPath,
                CategoryCoverage = BuildCategoryCoverage(assets),
                StyleCoverage = BuildStyleCoverage(assets),
                Assets = assets
            };

            WriteJson(outputFilePath, catalog);
        }

        public static void ExportTemplateMarker(string outputFilePath, string packId, string templateId, string templateRevision)
        {
            var marker = new TemplateBaselineMarker
            {
                PackId = packId,
                TemplateId = templateId,
                TemplateRevision = templateRevision
            };
            WriteJson(outputFilePath, marker);
        }

        private static AssetRecord BuildAssetRecord(string assetPath, string assetRoot)
        {
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrWhiteSpace(guid))
                return null;

            Type mainAssetType = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
            UnityEngine.Object mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            string kind = ClassifyKind(assetPath, mainAssetType, mainAsset);

            var record = new AssetRecord
            {
                Guid = guid,
                Path = assetPath,
                RelativePath = MakeRelativePath(assetRoot, assetPath),
                Name = Path.GetFileNameWithoutExtension(assetPath),
                Kind = kind,
                Category = ClassifyCategory(assetPath, mainAsset),
                Categories = ClassifyCategories(assetPath, mainAsset),
                Roles = ClassifyRoles(assetPath, mainAsset),
                Style = ClassifyStyle(assetPath, mainAsset),
                Styles = ClassifyStyles(assetPath, mainAsset),
                Tags = ClassifyTags(assetPath, mainAsset),
                ProductionSafe = !IsDemoPath(assetPath),
                PreviewPath = FindPreviewPath(assetPath, kind),
                SourceFolder = Path.GetDirectoryName(assetPath)?.Replace("\\", "/") ?? string.Empty,
                FileSize = ReadFileSize(assetPath),
                DependencyGuids = AssetDatabase.GetDependencies(assetPath, false)
                    .Where(path => !string.Equals(path, assetPath, StringComparison.OrdinalIgnoreCase))
                    .Select(AssetDatabase.AssetPathToGUID)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };

            record.Audit = BuildAuditRecord(assetPath, mainAsset, kind, record.Category, record.Style);
            record.SliceData = BuildSliceData(assetPath, mainAsset, kind);
            record.Font = BuildFontRecord(assetPath, mainAsset, kind);
            record.Sprite = BuildSpriteRecord(assetPath, mainAsset, kind);
            record.Prefab = BuildPrefabRecord(assetPath, mainAsset, kind);

            return record;
        }

        private static AuditRecord BuildAuditRecord(string assetPath, UnityEngine.Object mainAsset, string kind, string category, string style)
        {
            var audit = new AuditRecord
            {
                KindConfidence = ConfidenceForKind(kind),
                CategoryConfidence = ConfidenceForCategory(category),
                StyleConfidence = string.IsNullOrWhiteSpace(style) ? "low" : "medium",
                DemoRelated = IsDemoPath(assetPath),
                MatchTerms = ExtractMatchTerms(assetPath, mainAsset),
                NamingTokens = ExtractPathTokens(assetPath),
                RiskFlags = BuildRiskFlags(assetPath, kind, category)
            };
            return audit;
        }

        private static SliceDataRecord BuildSliceData(string assetPath, UnityEngine.Object mainAsset, string kind)
        {
            if (!string.Equals(kind, "sprite", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(kind, "texture", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            Sprite sprite = mainAsset as Sprite;
            if (sprite == null)
            {
                sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            }
            if (sprite == null)
                return null;

            Vector4 border = sprite.border;
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            return new SliceDataRecord
            {
                IsSliced = border != Vector4.zero,
                Left = border.x,
                Bottom = border.y,
                Right = border.z,
                Top = border.w,
                MeshType = sprite.packed ? "packed" : "individual",
                PixelsPerUnit = sprite.pixelsPerUnit,
                SpritePivot = new FloatVector2(sprite.pivot.x, sprite.pivot.y),
                ImportMode = importer != null ? importer.spriteImportMode.ToString() : string.Empty
            };
        }

        private static FontRecord BuildFontRecord(string assetPath, UnityEngine.Object mainAsset, string kind)
        {
            if (!string.Equals(kind, "tmp_font", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(kind, "font", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var tmpFont = mainAsset as TMP_FontAsset;
            if (tmpFont != null)
            {
                return new FontRecord
                {
                    FontType = "tmp_font",
                    Family = tmpFont.faceInfo.familyName,
                    StyleName = tmpFont.faceInfo.styleName,
                    AtlasWidth = tmpFont.atlasWidth,
                    AtlasHeight = tmpFont.atlasHeight,
                    PointSize = tmpFont.faceInfo.pointSize
                };
            }

            var font = mainAsset as Font;
            if (font != null)
            {
                return new FontRecord
                {
                    FontType = "font",
                    Family = font.name,
                    StyleName = "regular",
                    AtlasWidth = 0,
                    AtlasHeight = 0,
                    PointSize = 0
                };
            }

            return null;
        }

        private static SpriteRecord BuildSpriteRecord(string assetPath, UnityEngine.Object mainAsset, string kind)
        {
            if (!string.Equals(kind, "sprite", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(kind, "texture", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            Sprite sprite = mainAsset as Sprite;
            if (sprite == null)
            {
                sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            }
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            Texture texture = sprite != null ? sprite.texture : mainAsset as Texture;
            return new SpriteRecord
            {
                Width = sprite != null ? sprite.rect.width : texture != null ? texture.width : 0f,
                Height = sprite != null ? sprite.rect.height : texture != null ? texture.height : 0f,
                PixelsPerUnit = sprite != null ? sprite.pixelsPerUnit : 0f,
                TextureFormat = texture != null ? texture.graphicsFormat.ToString() : string.Empty,
                ImportType = importer != null ? importer.textureType.ToString() : string.Empty,
                AlphaIsTransparency = importer != null && importer.alphaIsTransparency
            };
        }

        private static PrefabRecord BuildPrefabRecord(string assetPath, UnityEngine.Object mainAsset, string kind)
        {
            if (!string.Equals(kind, "prefab", StringComparison.OrdinalIgnoreCase))
                return null;

            GameObject prefab = mainAsset as GameObject;
            if (prefab == null)
                return null;

            Component[] components = prefab.GetComponentsInChildren<Component>(true);
            var componentTypeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var referencedAssetGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool hasButton = false;
            bool hasInput = false;
            bool hasText = false;
            bool hasImage = false;

            foreach (Component component in components)
            {
                if (component == null)
                    continue;

                string typeName = component.GetType().Name;
                componentTypeCounts[typeName] = componentTypeCounts.TryGetValue(typeName, out int count) ? count + 1 : 1;

                switch (component)
                {
                    case Button _:
                        hasButton = true;
                        categories.Add("button_primary");
                        categories.Add("button_secondary");
                        break;
                    case TMP_InputField _:
                    case InputField _:
                        hasInput = true;
                        categories.Add("input_field");
                        break;
                    case Text _:
                    case TMP_Text _:
                        hasText = true;
                        categories.Add("font");
                        break;
                    case Image image:
                        hasImage = true;
                        CollectGuidFromObject(image.sprite, referencedAssetGuids);
                        break;
                    case RawImage rawImage:
                        hasImage = true;
                        CollectGuidFromObject(rawImage.texture, referencedAssetGuids);
                        break;
                }

                CollectSerializedObjectGuids(component, referencedAssetGuids);
            }

            CollectSerializedObjectGuids(prefab, referencedAssetGuids);

            string primaryComponent = componentTypeCounts
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => pair.Key)
                .FirstOrDefault() ?? "GameObject";

            return new PrefabRecord
            {
                RootName = prefab.name,
                ChildCount = prefab.GetComponentsInChildren<Transform>(true).Length - 1,
                ComponentTypes = componentTypeCounts.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                ComponentTypeCounts = componentTypeCounts
                    .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pair => new ComponentCount { Component = pair.Key, Count = pair.Value })
                    .ToArray(),
                ReferencedAssetGuids = referencedAssetGuids.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                HasButton = hasButton,
                HasInputField = hasInput,
                HasText = hasText,
                HasImage = hasImage,
                SuggestedCategories = categories.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                PrimaryComponent = primaryComponent
            };
        }

        private static Dictionary<string, int> BuildCategoryCoverage(IEnumerable<AssetRecord> assets)
        {
            return assets
                .SelectMany(asset => asset.Categories ?? Array.Empty<string>())
                .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        }

        private static Dictionary<string, int> BuildStyleCoverage(IEnumerable<AssetRecord> assets)
        {
            return assets
                .SelectMany(asset => asset.Styles ?? Array.Empty<string>())
                .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        }

        private static string ClassifyKind(string assetPath, Type mainAssetType, UnityEngine.Object mainAsset)
        {
            if (typeof(GameObject).IsAssignableFrom(mainAssetType))
                return "prefab";
            if (typeof(TMP_FontAsset).IsAssignableFrom(mainAssetType))
                return "tmp_font";
            if (typeof(Font).IsAssignableFrom(mainAssetType))
                return "font";
            if (typeof(Sprite).IsAssignableFrom(mainAssetType))
                return "sprite";
            if (typeof(Texture).IsAssignableFrom(mainAssetType))
                return "texture";

            string extension = Path.GetExtension(assetPath).ToLowerInvariant();
            return extension switch
            {
                ".prefab" => "prefab",
                ".png" => "texture",
                ".jpg" => "texture",
                ".jpeg" => "texture",
                ".tga" => "texture",
                ".psd" => "texture",
                ".asset" when mainAsset is TMP_FontAsset => "tmp_font",
                ".ttf" => "font",
                ".otf" => "font",
                _ => string.IsNullOrWhiteSpace(extension) ? "unknown" : extension.TrimStart('.')
            };
        }

        private static string ClassifyCategory(string assetPath, UnityEngine.Object mainAsset)
        {
            string lower = assetPath.ToLowerInvariant();
            if (HasAny(lower, "background", "backdrop", "/bg_", "_bg", "scene_bg"))
                return "background";
            if (HasAny(lower, "inputfield", "input_field", "textfield", "text_field"))
                return "input_field";
            if (HasAny(lower, "icon", "picto", "symbol", "badgeicon"))
                return "icon";
            if (mainAsset is TMP_FontAsset || mainAsset is Font)
                return "font";
            if (HasAny(lower, "button", "btn"))
            {
                return HasAny(lower, "secondary", "ghost", "small", "gray", "grey", "dark") ? "button_secondary" : "button_primary";
            }
            if (HasAny(lower, "panel", "frame", "window", "popup", "dialog", "card"))
                return "panel";

            if (mainAsset is GameObject prefab)
            {
                if (prefab.GetComponentInChildren<Button>(true) != null)
                    return "button_primary";
                if (prefab.GetComponentInChildren<TMP_InputField>(true) != null || prefab.GetComponentInChildren<InputField>(true) != null)
                    return "input_field";
                if (prefab.GetComponentInChildren<Image>(true) != null)
                    return "panel";
            }

            if (mainAsset is Sprite sprite && sprite.border != Vector4.zero)
                return "panel";

            return "uncategorized";
        }

        private static string[] ClassifyCategories(string assetPath, UnityEngine.Object mainAsset)
        {
            var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ClassifyCategory(assetPath, mainAsset)
            };

            string lower = assetPath.ToLowerInvariant();
            if (HasAny(lower, "button", "btn"))
            {
                categories.Add("button_primary");
                categories.Add("button_secondary");
            }
            if (HasAny(lower, "popup", "dialog"))
                categories.Add("panel");
            if (HasAny(lower, "title", "label", "text"))
                categories.Add("font");
            if (HasAny(lower, "icon", "picto"))
                categories.Add("icon");

            return categories
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string ClassifyStyle(string assetPath, UnityEngine.Object mainAsset)
        {
            string lower = assetPath.ToLowerInvariant();
            foreach (string value in new[] { "blue", "green", "gold", "dark", "wood", "red", "orange", "yellow", "purple", "pink", "navy", "white", "black", "brown", "silver" })
            {
                if (lower.Contains(value))
                    return value;
            }

            if (mainAsset is Sprite sprite)
            {
                try
                {
                    Texture2D readable = AssetPreview.GetAssetPreview(sprite) ?? AssetPreview.GetMiniThumbnail(sprite) as Texture2D;
                    if (readable != null)
                    {
                        Color average = EstimateAverageColor(readable);
                        return ClassifyColorName(average);
                    }
                }
                catch
                {
                    // AssetPreview is best-effort only.
                }
            }

            return string.Empty;
        }

        private static string[] ClassifyStyles(string assetPath, UnityEngine.Object mainAsset)
        {
            var styles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string primary = ClassifyStyle(assetPath, mainAsset);
            if (!string.IsNullOrWhiteSpace(primary))
                styles.Add(primary);

            string lower = assetPath.ToLowerInvariant();
            foreach (string value in new[] { "casual", "fantasy", "metal", "wood", "stone", "cute", "glossy", "flat", "dark", "light" })
            {
                if (lower.Contains(value))
                    styles.Add(value);
            }

            return styles.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static string[] ClassifyTags(string assetPath, UnityEngine.Object mainAsset)
        {
            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string token in ExtractPathTokens(assetPath))
            {
                if (token.Length >= 3)
                    tags.Add(token);
            }

            if (mainAsset is GameObject prefab)
            {
                if (prefab.GetComponentInChildren<Button>(true) != null)
                    tags.Add("interactive");
                if (prefab.GetComponentInChildren<TMP_InputField>(true) != null || prefab.GetComponentInChildren<InputField>(true) != null)
                    tags.Add("form");
                if (prefab.GetComponentInChildren<Image>(true) != null)
                    tags.Add("ui");
            }

            return tags.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static string[] ClassifyRoles(string assetPath, UnityEngine.Object mainAsset)
        {
            var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string lower = assetPath.ToLowerInvariant();
            string category = ClassifyCategory(assetPath, mainAsset);

            switch (category)
            {
                case "background":
                    roles.Add("background");
                    break;
                case "panel":
                    roles.Add("panel");
                    break;
                case "button_primary":
                case "button_secondary":
                    roles.Add("button");
                    break;
                case "input_field":
                    roles.Add("input");
                    break;
                case "icon":
                    roles.Add("icon");
                    break;
                case "font":
                    roles.Add("label");
                    break;
            }

            if (HasAny(lower, "popup", "dialog", "modal"))
                roles.Add("popup");
            if (HasAny(lower, "toggle", "checkbox", "check_box", "radio"))
                roles.Add("toggle");
            if (HasAny(lower, "slider", "loadingbar", "progress", "healthbar"))
            {
                roles.Add("slider");
                roles.Add("progress");
            }
            if (HasAny(lower, "slot", "inventory", "equipment", "item"))
                roles.Add("slot");
            if (HasAny(lower, "tab", "category"))
                roles.Add("tab");

            if (mainAsset is GameObject prefab)
            {
                if (prefab.GetComponentInChildren<Button>(true) != null)
                    roles.Add("button");
                if (prefab.GetComponentInChildren<TMP_InputField>(true) != null || prefab.GetComponentInChildren<InputField>(true) != null)
                    roles.Add("input");
                if (prefab.GetComponentInChildren<Toggle>(true) != null)
                    roles.Add("toggle");
                if (prefab.GetComponentInChildren<Slider>(true) != null)
                {
                    roles.Add("slider");
                    roles.Add("progress");
                }
                if (prefab.GetComponentInChildren<TMP_Text>(true) != null || prefab.GetComponentInChildren<Text>(true) != null)
                    roles.Add("label");
                if (prefab.GetComponentInChildren<Image>(true) != null && !roles.Contains("button") && !roles.Contains("input"))
                    roles.Add("panel");
            }

            return roles.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static string[] ExtractMatchTerms(string assetPath, UnityEngine.Object mainAsset)
        {
            var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string token in ExtractPathTokens(assetPath))
            {
                switch (token)
                {
                    case "background":
                    case "panel":
                    case "button":
                    case "btn":
                    case "icon":
                    case "input":
                    case "field":
                    case "label":
                    case "title":
                    case "popup":
                    case "dialog":
                    case "blue":
                    case "green":
                    case "gold":
                    case "dark":
                    case "wood":
                        terms.Add(token);
                        break;
                }
            }

            if (mainAsset is GameObject prefab)
            {
                if (prefab.GetComponentInChildren<Button>(true) != null)
                    terms.Add("button");
                if (prefab.GetComponentInChildren<TMP_InputField>(true) != null || prefab.GetComponentInChildren<InputField>(true) != null)
                    terms.Add("input_field");
                if (prefab.GetComponentInChildren<Image>(true) != null)
                    terms.Add("image");
            }

            return terms.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static string[] BuildRiskFlags(string assetPath, string kind, string category)
        {
            var flags = new List<string>();
            if (IsDemoPath(assetPath))
                flags.Add("demo_path");
            if (string.Equals(category, "uncategorized", StringComparison.OrdinalIgnoreCase))
                flags.Add("uncategorized");
            if (string.Equals(kind, "unknown", StringComparison.OrdinalIgnoreCase))
                flags.Add("unknown_kind");
            return flags.ToArray();
        }

        private static string ConfidenceForKind(string kind)
        {
            return string.Equals(kind, "unknown", StringComparison.OrdinalIgnoreCase) ? "low" : "high";
        }

        private static string ConfidenceForCategory(string category)
        {
            return string.Equals(category, "uncategorized", StringComparison.OrdinalIgnoreCase) ? "low" : "medium";
        }

        private static string FindPreviewPath(string assetPath, string kind)
        {
            if (IsTextureKind(kind))
                return assetPath;

            string assetFileName = Path.GetFileNameWithoutExtension(assetPath);
            string directory = Path.GetDirectoryName(assetPath)?.Replace("\\", "/") ?? string.Empty;
            string root = directory;
            while (!string.IsNullOrWhiteSpace(root))
            {
                string previewCandidate = $"{root}/Preview/{assetFileName}.png";
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(previewCandidate) != null)
                    return previewCandidate;

                previewCandidate = $"{root}/Previews/{assetFileName}.png";
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(previewCandidate) != null)
                    return previewCandidate;

                int separator = root.LastIndexOf('/');
                if (separator < 0)
                    break;
                root = root.Substring(0, separator);
            }

            string[] dependencyPaths = AssetDatabase.GetDependencies(assetPath, false);
            foreach (string dependencyPath in dependencyPaths)
            {
                if (dependencyPath.Equals(assetPath, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (IsTextureKind(ClassifyKind(dependencyPath, AssetDatabase.GetMainAssetTypeAtPath(dependencyPath), AssetDatabase.LoadMainAssetAtPath(dependencyPath))) &&
                    !IsDemoPath(dependencyPath))
                {
                    return dependencyPath;
                }
            }

            return string.Empty;
        }

        private static bool IsSupportedAssetPath(string assetPath)
        {
            string extension = Path.GetExtension(assetPath).ToLowerInvariant();
            return extension == ".prefab" ||
                   extension == ".png" ||
                   extension == ".jpg" ||
                   extension == ".jpeg" ||
                   extension == ".psd" ||
                   extension == ".tga" ||
                   extension == ".asset" ||
                   extension == ".ttf" ||
                   extension == ".otf";
        }

        private static bool IsTextureKind(string kind)
        {
            return string.Equals(kind, "texture", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(kind, "sprite", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDemoPath(string assetPath)
        {
            string lower = assetPath.ToLowerInvariant();
            return lower.Contains("/preview/") ||
                   lower.Contains("/previews/") ||
                   lower.Contains("/demo/") ||
                   lower.Contains("/demos/") ||
                   lower.Contains("demoscene") ||
                   lower.Contains("/sample/") ||
                   lower.Contains("/samples/") ||
                   lower.Contains("/example/") ||
                   lower.Contains("/examples/");
        }

        private static long ReadFileSize(string assetPath)
        {
            string absolutePath = Path.GetFullPath(assetPath);
            return File.Exists(absolutePath) ? new FileInfo(absolutePath).Length : 0L;
        }

        private static string NormalizeAssetPath(string assetPath)
        {
            return assetPath.Replace("\\", "/").TrimEnd('/');
        }

        private static string MakeRelativePath(string root, string assetPath)
        {
            if (assetPath.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
                return assetPath.Substring(root.Length + 1);
            if (assetPath.Equals(root, StringComparison.OrdinalIgnoreCase))
                return string.Empty;
            return assetPath;
        }

        private static string[] ExtractPathTokens(string assetPath)
        {
            string normalized = assetPath.Replace("\\", "/").ToLowerInvariant();
            var builder = new StringBuilder(normalized.Length);
            foreach (char character in normalized)
            {
                builder.Append(char.IsLetterOrDigit(character) ? character : ' ');
            }

            return builder.ToString()
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static bool HasAny(string value, params string[] candidates)
        {
            return candidates.Any(candidate => value.Contains(candidate));
        }

        private static void CollectGuidFromObject(UnityEngine.Object value, ISet<string> target)
        {
            if (value == null)
                return;

            string path = AssetDatabase.GetAssetPath(value);
            if (string.IsNullOrWhiteSpace(path))
                return;

            string guid = AssetDatabase.AssetPathToGUID(path);
            if (!string.IsNullOrWhiteSpace(guid))
                target.Add(guid);
        }

        private static void CollectSerializedObjectGuids(UnityEngine.Object value, ISet<string> target)
        {
            if (value == null)
                return;

            SerializedObject serializedObject = new SerializedObject(value);
            SerializedProperty property = serializedObject.GetIterator();
            bool expanded = true;
            while (property.NextVisible(expanded))
            {
                expanded = false;
                if (property.propertyType != SerializedPropertyType.ObjectReference)
                    continue;

                UnityEngine.Object reference = property.objectReferenceValue;
                if (reference == null)
                    continue;

                CollectGuidFromObject(reference, target);
            }
        }

        private static Color EstimateAverageColor(Texture2D texture)
        {
            RenderTexture renderTexture = RenderTexture.GetTemporary(16, 16, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(texture, renderTexture);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = renderTexture;
            var sample = new Texture2D(16, 16, TextureFormat.RGBA32, false, true);
            sample.ReadPixels(new Rect(0, 0, 16, 16), 0, 0);
            sample.Apply(false, true);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(renderTexture);

            Color[] pixels = sample.GetPixels();
            float red = 0f;
            float green = 0f;
            float blue = 0f;
            for (int index = 0; index < pixels.Length; index++)
            {
                red += pixels[index].r;
                green += pixels[index].g;
                blue += pixels[index].b;
            }

            float count = pixels.Length > 0 ? pixels.Length : 1f;
            return new Color(red / count, green / count, blue / count);
        }

        private static string ClassifyColorName(Color color)
        {
            float max = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            if (max < 0.18f)
                return "dark";
            if (color.r > 0.55f && color.g > 0.48f && color.b < 0.35f)
                return "gold";
            if (color.b >= color.g && color.b >= color.r)
                return "blue";
            if (color.g >= color.b && color.g >= color.r)
                return "green";
            if (color.r > 0.35f && color.g > 0.22f && color.b < 0.18f)
                return "wood";
            return "mixed";
        }

        private static void WriteJson(string outputFilePath, object value)
        {
            string directory = Path.GetDirectoryName(outputFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string json = JsonUtility.ToJson(value, true);
            File.WriteAllText(outputFilePath, json.Replace("\n", Environment.NewLine), new UTF8Encoding(false));
            AssetDatabase.Refresh();
        }

        private static string ReadEnvironment(string key, string fallback)
        {
            string value = Environment.GetEnvironmentVariable(key);
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        [Serializable]
        private sealed class CatalogExport
        {
            public string PackId;
            public string TemplateId;
            public string TemplateRevision;
            public string UnityVersion;
            public string ExportedAt;
            public string AssetRoot;
            public string TemplateMarkerPath;
            public SerializableDictionary CategoryCoverage;
            public SerializableDictionary StyleCoverage;
            public List<AssetRecord> Assets;
        }

        [Serializable]
        private sealed class TemplateBaselineMarker
        {
            public string PackId;
            public string TemplateId;
            public string TemplateRevision;
        }

        [Serializable]
        private sealed class AssetRecord
        {
            public string Guid;
            public string Path;
            public string RelativePath;
            public string Name;
            public string Kind;
            public string Category;
            public string[] Categories;
            public string[] Roles;
            public string Style;
            public string[] Styles;
            public string[] Tags;
            public bool ProductionSafe;
            public string PreviewPath;
            public string SourceFolder;
            public long FileSize;
            public string[] DependencyGuids;
            public AuditRecord Audit;
            public SliceDataRecord SliceData;
            public FontRecord Font;
            public SpriteRecord Sprite;
            public PrefabRecord Prefab;
        }

        [Serializable]
        private sealed class AuditRecord
        {
            public string KindConfidence;
            public string CategoryConfidence;
            public string StyleConfidence;
            public bool DemoRelated;
            public string[] MatchTerms;
            public string[] NamingTokens;
            public string[] RiskFlags;
        }

        [Serializable]
        private sealed class SliceDataRecord
        {
            public bool IsSliced;
            public float Left;
            public float Bottom;
            public float Right;
            public float Top;
            public string MeshType;
            public float PixelsPerUnit;
            public FloatVector2 SpritePivot;
            public string ImportMode;
        }

        [Serializable]
        private sealed class FontRecord
        {
            public string FontType;
            public string Family;
            public string StyleName;
            public int AtlasWidth;
            public int AtlasHeight;
            public float PointSize;
        }

        [Serializable]
        private sealed class SpriteRecord
        {
            public float Width;
            public float Height;
            public float PixelsPerUnit;
            public string TextureFormat;
            public string ImportType;
            public bool AlphaIsTransparency;
        }

        [Serializable]
        private sealed class PrefabRecord
        {
            public string RootName;
            public int ChildCount;
            public string[] ComponentTypes;
            public ComponentCount[] ComponentTypeCounts;
            public string[] ReferencedAssetGuids;
            public bool HasButton;
            public bool HasInputField;
            public bool HasText;
            public bool HasImage;
            public string[] SuggestedCategories;
            public string PrimaryComponent;
        }

        [Serializable]
        private sealed class ComponentCount
        {
            public string Component;
            public int Count;
        }

        [Serializable]
        private sealed class FloatVector2
        {
            public float X;
            public float Y;

            public FloatVector2(float x, float y)
            {
                X = x;
                Y = y;
            }
        }

        [Serializable]
        private sealed class SerializableDictionary
        {
            public SerializableKeyValuePair[] Entries;

            public static implicit operator SerializableDictionary(Dictionary<string, int> values)
            {
                return new SerializableDictionary
                {
                    Entries = values
                        .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(pair => new SerializableKeyValuePair { Key = pair.Key, Value = pair.Value })
                        .ToArray()
                };
            }
        }

        [Serializable]
        private sealed class SerializableKeyValuePair
        {
            public string Key;
            public int Value;
        }
    }
}
#endif
