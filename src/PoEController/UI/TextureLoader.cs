using System.IO;
using UnityEngine;

namespace PoEController.UI
{
    internal static class TextureLoader
    {
        /// <summary>Loads a PNG into a persistent texture, or returns null if missing/undecodable.</summary>
        public static Texture2D LoadPng(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            if (!tex.LoadImage(File.ReadAllBytes(path)))
            {
                Plugin.Log.LogWarning($"Could not decode {path}");
                Object.Destroy(tex);
                return null;
            }
            tex.name = "PoEController_" + Path.GetFileNameWithoutExtension(path);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            Object.DontDestroyOnLoad(tex);
            return tex;
        }
    }
}
