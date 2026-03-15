#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace GGemCo2DAiBtEditor
{
    internal static class BtDebugExportWriter
    {
        public static void WriteJson(string path, BtDebugExportData data)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("Path is null or empty.", nameof(path));
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }
    }
}
#endif
