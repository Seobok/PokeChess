using System;
using System.Linq;
using PokeChess.Client.UI;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

public static class ConnectAdversityVisuals
{
    public static string Apply()
    {
        const string folder = "Assets/_Project/Sprites/Effects/Synergies/adversity/";
        const string assetPath = "Assets/_Project/Resources/AdversityVisuals.asset";
        var specs = new (string Field, string File, int Cell, int Frames)[]
        {
            ("resolveIcon", "adversity-resolve-icon", 32, 1),
            ("lossSpark", "adversity-loss-spark", 64, 8),
            ("resolveCollect", "adversity-resolve-collect", 64, 8),
            ("cashout", "adversity-cashout", 96, 12),
            ("gold", "adversity-gold-reward", 32, 1),
            ("equipment", "adversity-equipment-fragment", 32, 1),
            ("emblem", "adversity-emblem-fragment", 32, 1),
            ("choiceReady", "adversity-choice-ready", 128, 8),
            ("choiceConfirm", "adversity-choice-confirm", 128, 8),
            ("rewardCollect", "adversity-reward-collect", 64, 8),
        };
        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var work = specs.Select(spec =>
        {
            var path = folder + spec.File + ".png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || importer.spriteImportMode != SpriteImportMode.Multiple)
                throw new InvalidOperationException("Expected a multiple sprite importer: " + path);
            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            int columns = width / spec.Cell;
            if (columns == 0 || width % spec.Cell != 0 || height % spec.Cell != 0 ||
                columns * (height / spec.Cell) < spec.Frames)
                throw new InvalidOperationException("Sheet cannot fit the requested grid: " + path);
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            if (provider == null) throw new InvalidOperationException("Missing sprite data provider: " + path);
            provider.InitSpriteEditorDataProvider();
            var capability = provider.GetDataProvider<ISpriteFrameEditCapability>();
            var ids = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            if (capability == null || ids == null ||
                !capability.GetEditCapability().HasCapability(EEditCapability.CreateAndDeleteSprite))
                throw new InvalidOperationException("Sprite slicing capability unavailable: " + path);
            return (spec, path, importer, provider, ids, columns, height);
        }).ToArray();

        var art = AssetDatabase.LoadAssetAtPath<AdversityVisuals>(assetPath);
        if (art == null)
        {
            art = ScriptableObject.CreateInstance<AdversityVisuals>();
            AssetDatabase.CreateAsset(art, assetPath);
        }
        foreach (var item in work)
        {
            var previous = item.provider.GetSpriteRects();
            var slices = Enumerable.Range(0, item.spec.Frames).Select(index =>
            {
                var rect = new Rect(index % item.columns * item.spec.Cell,
                    item.height - (index / item.columns + 1) * item.spec.Cell, item.spec.Cell, item.spec.Cell);
                var name = item.spec.File + "_" + index.ToString("D2");
                var match = previous.FirstOrDefault(old => old.name == name && old.rect == rect);
                return new SpriteRect { name = name, rect = rect, alignment = SpriteAlignment.Center,
                    pivot = new Vector2(.5f, .5f), spriteID = match?.spriteID ?? GUID.Generate() };
            }).ToArray();
            item.provider.SetSpriteRects(slices);
            item.ids.SetNameFileIdPairs(slices.Select(slice => new SpriteNameFileIdPair(slice.name, slice.spriteID)));
            item.provider.Apply();
            item.importer.filterMode = FilterMode.Point;
            item.importer.mipmapEnabled = false;
            item.importer.textureCompression = TextureImporterCompression.Uncompressed;
            var settings = new TextureImporterSettings();
            item.importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            item.importer.SetTextureSettings(settings);
            item.importer.SaveAndReimport();

            var sprites = AssetDatabase.LoadAllAssetsAtPath(item.path).OfType<Sprite>().OrderBy(sprite => sprite.name).ToArray();
            if (sprites.Length != item.spec.Frames || sprites.Any(sprite => sprite.rect.width != item.spec.Cell || sprite.rect.height != item.spec.Cell))
                throw new InvalidOperationException("Unexpected imported sprite count or size: " + item.path);
            typeof(AdversityVisuals).GetField(item.spec.Field).SetValue(art, sprites);
        }
        EditorUtility.SetDirty(art);
        AssetDatabase.SaveAssetIfDirty(art);
        return $"Connected {specs.Length} adversity sheets ({specs.Sum(spec => spec.Frames)} fixed-grid sprites).";
    }
}
