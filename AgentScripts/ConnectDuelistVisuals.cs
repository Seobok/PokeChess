using System;
using System.Linq;
using PokeChess.Client.UI;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

public static class ConnectDuelistVisuals
{
    public static string Apply()
    {
        const string folder = "Assets/_Project/Sprites/Effects/Synergies/duelist/";
        const string assetPath = "Assets/_Project/Resources/DuelistVisuals.asset";
        var specs = new (string Field, string File, int Width, int Height, int Frames)[]
        {
            ("stackMeter", "duelist-stack-meter", 64, 32, 7),
            ("stackGain", "duelist-stack-gain", 32, 32, 6),
            ("stackMax", "duelist-stack-max", 64, 32, 8),
            ("chaseStart", "duelist-chase-start", 64, 64, 8),
            ("chaseTrail", "duelist-chase-trail", 64, 64, 6),
            ("empoweredReady", "duelist-empowered-ready", 32, 32, 8),
            ("empoweredHit", "duelist-empowered-hit", 64, 64, 6),
            ("empoweredExpire", "duelist-empowered-expire", 32, 32, 6),
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

        var art = AssetDatabase.LoadAssetAtPath<DuelistVisuals>(assetPath);
        if (art == null)
        {
            art = ScriptableObject.CreateInstance<DuelistVisuals>();
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
            typeof(DuelistVisuals).GetField(item.spec.Field).SetValue(art, sprites);
        }
        EditorUtility.SetDirty(art);
        AssetDatabase.SaveAssetIfDirty(art);
        return $"Connected {specs.Length} duelist sheets ({specs.Sum(spec => spec.Frames)} fixed-grid sprites).";
    }
}
