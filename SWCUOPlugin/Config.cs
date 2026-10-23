using System;
using System.Collections.Generic;
using System.IO;

namespace Assistant
{
    /// <summary>
    /// Plugin settings from <c>SWCUOPlugin.ini</c> next to the plugin DLL (simple <c>key=value</c> lines,
    /// <c>#</c> starts a comment). The file is created with defaults if it doesn't exist.
    /// </summary>
    public class Config
    {
        public const string FileName = "SWCUOPlugin.ini";

        /// <summary>Write received speech, emotes and system messages to <see cref="JournalFolder"/>.</summary>
        public bool JournalEnabled { get; private set; } = true;

        /// <summary>Absolute path. Relative paths in the ini are resolved against the plugin directory.</summary>
        public string JournalFolder { get; private set; } = "";

        /// <summary>Download the vendor POIs at startup and write them as a ClassicUO world map marker file.</summary>
        public bool VendorMarkersEnabled { get; private set; } = true;

        /// <summary>Endpoint returning the vendor POIs as JSON.</summary>
        public string VendorMarkersUrl { get; private set; } = VendorMarkers.DefaultUrl;

        /// <summary>Base URL the icon paths of the POI response are resolved against.</summary>
        public string VendorMarkersIconBaseUrl { get; private set; } = VendorMarkers.DefaultIconBaseUrl;

        private const string DefaultContent =
            "# SWCUOPlugin settings\n" +
            "\n" +
            "# Write received speech, emotes and system messages to a text file per character and day.\n" +
            "JournalEnabled=true\n" +
            "# Absolute path, or relative to the plugin folder. Environment variables like %USERPROFILE% are expanded.\n" +
            "JournalFolder=Journal\n" +
            "\n" +
            "# Download the vendor POIs at startup and write them to Data/Client/SWCUOPlugin_Vendors.csv (world map markers).\n" +
            "VendorMarkersEnabled=true\n" +
            "# Where the vendor POIs are downloaded from.\n" +
            "VendorMarkersUrl=" + VendorMarkers.DefaultUrl + "\n" +
            "# Base URL for the icons (saved to Data/Client/MapIcons as sw_<type>.png).\n" +
            "VendorMarkersIconBaseUrl=" + VendorMarkers.DefaultIconBaseUrl + "\n";

        public static Config Load()
        {
            string dir = Path.GetDirectoryName(typeof(Config).Assembly.Location) ?? ".";
            string path = Path.Combine(dir, FileName);
            var config = new Config { JournalFolder = Path.Combine(dir, "Journal") };

            try
            {
                if (!File.Exists(path))
                {
                    File.WriteAllText(path, DefaultContent.Replace("\n", Environment.NewLine));
                    DebugLog.Write($"Config: created default {path}");
                }

                var values = Parse(File.ReadAllLines(path));

                if (values.TryGetValue("JournalEnabled", out string? enabled) && bool.TryParse(enabled, out bool b))
                    config.JournalEnabled = b;

                if (values.TryGetValue("VendorMarkersEnabled", out string? markers) && bool.TryParse(markers, out bool m))
                    config.VendorMarkersEnabled = m;

                if (values.TryGetValue("VendorMarkersUrl", out string? url) && url.Length > 0)
                    config.VendorMarkersUrl = url;

                if (values.TryGetValue("VendorMarkersIconBaseUrl", out string? iconUrl) && iconUrl.Length > 0)
                    config.VendorMarkersIconBaseUrl = iconUrl;

                if (values.TryGetValue("JournalFolder", out string? folder) && folder.Length > 0)
                    config.JournalFolder = Path.GetFullPath(Path.Combine(dir, Environment.ExpandEnvironmentVariables(folder)));
            }
            catch (Exception ex)
            {
                DebugLog.Write($"Config: could not read {path}, using defaults: {ex.Message}");
            }

            DebugLog.Write($"Config: JournalEnabled={config.JournalEnabled}, JournalFolder={config.JournalFolder}");
            return config;
        }

        private static Dictionary<string, string> Parse(string[] lines)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                    continue;

                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;

                values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim().Trim('"');
            }
            return values;
        }
    }
}
