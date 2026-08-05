using System;
using System.IO;
using System.Reflection;
using System.Windows.Media;
using Autodesk.Revit.UI;

namespace HCB.RevitAddin
{
    internal static class RibbonIconResolver
    {
        public static void ApplyTo(ButtonData buttonData, Type commandType, string? resourceDirectoryOverride = null)
        {
            string assemblyDir = Path.GetDirectoryName(commandType.Assembly.Location) ?? string.Empty;
            string primaryResourceDir = string.IsNullOrWhiteSpace(resourceDirectoryOverride)
                ? GetFeatureResourcesDir(commandType)
                : resourceDirectoryOverride;
            const string sharedResourcesDir = "Resources";

            Assembly assembly = commandType.Assembly;
            ImageSource? largeIcon = ResolveIcon(assemblyDir, assembly, primaryResourceDir, sharedResourcesDir, 32, "icon32.png", "icon.png");
            ImageSource? smallIcon = ResolveIcon(assemblyDir, assembly, primaryResourceDir, sharedResourcesDir, 16, "icon16.png", "icon32.png", "icon.png");

            if (largeIcon != null)
            {
                buttonData.LargeImage = largeIcon;
            }

            if (smallIcon != null)
            {
                buttonData.Image = smallIcon;
            }
        }

        private static string GetFeatureResourcesDir(Type commandType)
        {
            string? namespaceName = commandType.Namespace;
            if (string.IsNullOrWhiteSpace(namespaceName))
            {
                return "Resources";
            }

            string[] parts = namespaceName.Split('.');
            int featuresIndex = Array.IndexOf(parts, "Features");
            if (featuresIndex < 0 || featuresIndex >= parts.Length - 1)
            {
                return "Resources";
            }

            return Path.Combine("Features", parts[featuresIndex + 1], "Resources");
        }

        private static ImageSource? ResolveIcon(
            string assemblyDir,
            Assembly assembly,
            string primaryDir,
            string fallbackDir,
            int pixelWidth,
            params string[] fileNames)
        {
            foreach (string fileName in fileNames)
            {
                ImageSource? primaryIcon = LoadIcon(assemblyDir, assembly, Path.Combine(primaryDir, fileName), pixelWidth);
                if (primaryIcon != null)
                {
                    return primaryIcon;
                }

                ImageSource? fallbackIcon = LoadIcon(assemblyDir, assembly, Path.Combine(fallbackDir, fileName), pixelWidth);
                if (fallbackIcon != null)
                {
                    return fallbackIcon;
                }
            }

            return null;
        }

        private static ImageSource? LoadIcon(string assemblyDir, Assembly assembly, string relativePath, int pixelWidth)
        {
            string filePath = Path.Combine(assemblyDir, relativePath);
            if (File.Exists(filePath))
            {
                try
                {
                    return ImageLoader.LoadPng(filePath, pixelWidth);
                }
                catch (IOException)
                {
                }
                catch (InvalidOperationException)
                {
                }
                catch (NotSupportedException)
                {
                }
            }

            return ImageLoader.TryLoadEmbeddedPng(assembly, relativePath, pixelWidth);
        }
    }
}
