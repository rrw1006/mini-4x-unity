using UnityEditor;
using UnityEngine;

// Forces crisp, unfiltered import settings for the 16x16 pixel-art sprites under Resources/GameArt.
public class PixelArtImporter : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').Contains("/Resources/GameArt/")) return;

        var importer = (TextureImporter)assetImporter;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.spritePixelsPerUnit = 16;
    }
}
