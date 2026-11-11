using System;
using System.Linq;
using PokeChess.Client.UI;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

public static class ConnectDisruptorVisuals
{
    public static string Apply()
    {
        const string folder = "Assets/_Project/Sprites/Effects/Synergies/disruptor/";
        const string assetPath = "Assets/_Project/Resources/DisruptorVisuals.asset";
        var specs = new (string Field, string File, int Width, int Height, int Frames)[]
        {
            ("weakenEnter", "disruptor-weaken-enter", 64, 64, 6),
            ("weakenIcon", "disruptor-weaken-icon", 32, 32, 1),
            ("exposure", "disruptor-exposure-loop", 96, 96, 8),
            ("manaEnter", "disruptor-mana-lock-enter", 48, 48, 6),
            ("manaIcon", "disruptor-mana-lock-icon", 32, 32, 1),
            ("refresh", "disruptor-weaken-refresh", 32, 32, 6),
            ("exit", "disruptor-status-exit", 32, 32, 6),
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
            int columns = width / spec.Width;
            if (columns == 0 || width % spec.Width != 0 || height % spec.Height != 0 ||
                columns * (height / spec.Height) < spec.Frames)
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

        var art = AssetDatabase.LoadAssetAtPath<DisruptorVisuals>(assetPath);
        if (art == null)
        {
            art = ScriptableObject.CreateInstance<DisruptorVisuals>();
            AssetDatabase.CreateAsset(art, assetPath);
        }
        foreach (var item in work)
        {
            var previous = item.provider.GetSpriteRects();
            var slices = Enumerable.Range(0, item.spec.Frames).Select(index =>
            {
                var rect = new Rect(index % item.columns * item.spec.Width,
                    item.height - (index / item.columns + 1) * item.spec.Height, item.spec.Width, item.spec.Height);
                var name = item.spec.File + "_" + index.ToString("D2");
                var match = previous.FirstOrDefault(old => old.name == name && old.rect == rect)
                    ?? previous.FirstOrDefault(old => old.rect == rect);
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
            if (sprites.Length != item.spec.Frames || sprites.Any(sprite => sprite.rect.width != item.spec.Width || sprite.rect.height != item.spec.Height))
                throw new InvalidOperationException("Unexpected imported sprite count or size: " + item.path);
            typeof(DisruptorVisuals).GetField(item.spec.Field).SetValue(art, sprites);
        }
        EditorUtility.SetDirty(art);
        AssetDatabase.SaveAssetIfDirty(art);
        return $"Connected {specs.Length} disruptor sheets ({specs.Sum(spec => spec.Frames)} fixed-grid sprites).";
    }
}
