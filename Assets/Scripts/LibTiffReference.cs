using UnityEngine;
using BitMiracle.LibTiff.Classic;

public static class LibTiffReference
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Preserve()
    {
        // Reference a LibTiff type to prevent stripping on IL2CPP builds
        System.Type t = typeof(Tiff);
    }
}
