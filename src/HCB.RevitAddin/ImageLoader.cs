using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HCB.RevitAddin
{
    internal static class ImageLoader
    {
        private const string MissingResourceName = "<missing>";

        private static readonly ConcurrentDictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, string> ResourceNameCache = new(StringComparer.OrdinalIgnoreCase);

        public static ImageSource LoadPng(string path, int pixelWidth)
        {
            string cacheKey = $"{Path.GetFullPath(path)}|{pixelWidth}";
            return Cache.GetOrAdd(cacheKey, _ =>
            {
                BitmapImage image = new();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(Path.GetFullPath(path), UriKind.Absolute);
                image.DecodePixelWidth = pixelWidth;
                image.EndInit();
                image.Freeze();
                return image;
            });
        }

        public static ImageSource? TryLoadEmbeddedPng(Assembly assembly, string relativePath, int pixelWidth)
        {
            string? resourceName = ResolveResourceName(assembly, relativePath);
            if (resourceName == null)
            {
                return null;
            }

            string cacheKey = $"{assembly.FullName}|{resourceName}|{pixelWidth}";
            if (Cache.TryGetValue(cacheKey, out ImageSource? cachedImage))
            {
                return cachedImage;
            }

            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                return null;
            }

            BitmapImage image = new();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.DecodePixelWidth = pixelWidth;
            image.EndInit();
            image.Freeze();
            return Cache.GetOrAdd(cacheKey, image);
        }

        private static string? ResolveResourceName(Assembly assembly, string relativePath)
        {
            string normalizedPath = relativePath
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);
            string dottedPath = normalizedPath.Replace(Path.DirectorySeparatorChar, '.');
            string cacheKey = $"{assembly.FullName}|{dottedPath}";

            string resourceName = ResourceNameCache.GetOrAdd(cacheKey, _ =>
            {
                string expectedName = $"{assembly.GetName().Name}.{dottedPath}";
                string? exactMatch = Array.Find(
                    assembly.GetManifestResourceNames(),
                    name => string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase));
                if (exactMatch != null)
                {
                    return exactMatch;
                }

                string suffix = $".{dottedPath}";
                return Array.Find(
                    assembly.GetManifestResourceNames(),
                    name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    ?? MissingResourceName;
            });

            return string.Equals(resourceName, MissingResourceName, StringComparison.Ordinal)
                ? null
                : resourceName;
        }
    }
}
