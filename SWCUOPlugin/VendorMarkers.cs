using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;

namespace Assistant
{
    /// <summary>
    /// Downloads the vendor POIs and writes them as a ClassicUO world map marker file
    /// (<c>Data/Client/*.csv</c>, lines of <c>x,y,mapindex,name,iconname,color,zoom</c>).
    /// ClassicUO reads the file whenever the world map is opened or its markers are reloaded.
    /// </summary>
    public static class VendorMarkers
    {
        public const string DefaultUrl = "https://alte-schattenwelt.de/api/pois/vendors";
        public const string DefaultIconBaseUrl = "https://alte-schattenwelt.de/map/";
        // The source icons are too large for the world map.
        private const double IconScale = 0.5;

        public const string FileName = "SWCUOPlugin_Vendors.csv";

        [DataContract]
        private class PoiList
        {
            [DataMember(Name = "pois")]  public List<Poi>? Pois { get; set; }
            [DataMember(Name = "types")] public Dictionary<string, PoiType>? Types { get; set; }
        }

        [DataContract]
        private class PoiType
        {
            [DataMember(Name = "subtypes")] public Dictionary<string, Subtype>? Subtypes { get; set; }
        }

        [DataContract]
        private class Subtype
        {
            [DataMember(Name = "icon")] public string? Icon { get; set; }
        }

        [DataContract]
        private class Poi
        {
            [DataMember(Name = "name")]     public string? Name { get; set; }
            [DataMember(Name = "mapId")]    public int MapId { get; set; }
            [DataMember(Name = "subtype")]  public string? Subtype { get; set; }
            [DataMember(Name = "position")] public Position? Position { get; set; }
        }

        [DataContract]
        private class Position
        {
            [DataMember(Name = "x")] public int X { get; set; }
            [DataMember(Name = "y")] public int Y { get; set; }
        }

        /// <summary>Starts the download in the background so client startup isn't blocked.</summary>
        public static void UpdateAsync(string url, string iconBaseUrl)
        {
            Task.Run(() =>
            {
                try
                {
                    Update(url, iconBaseUrl);
                }
                catch (Exception ex)
                {
                    DebugLog.Write($"VendorMarkers: update failed: {ex}");
                }
            });
        }

        private static void Update(string url, string iconBaseUrl)
        {
            string dir = Path.Combine(ClientDirectory(), "Data", "Client");
            string path = Path.Combine(dir, FileName);

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            byte[] json;
            using (var client = new WebClient())
                json = client.DownloadData(url);

            PoiList? list;
            var settings = new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true };
            using (var stream = new MemoryStream(json))
                list = (PoiList?)new DataContractJsonSerializer(typeof(PoiList), settings).ReadObject(stream);
            List<Poi>? pois = list?.Pois;
            if (pois == null || pois.Count == 0)
            {
                DebugLog.Write("VendorMarkers: response contained no POIs, keeping existing file");
                return;
            }

            Dictionary<string, string> icons = DownloadIcons(list!, pois, iconBaseUrl, Path.Combine(dir, "MapIcons"));

            var csv = new StringBuilder();
            int count = 0;
            foreach (Poi poi in pois)
            {
                if (poi.Position == null || string.IsNullOrWhiteSpace(poi.Name))
                    continue;

                string subtype = poi.Subtype ?? "";
                string label = subtype.Length > 0 ? $"{poi.Name} ({subtype})" : poi.Name!;
                // Commas separate the fields, line breaks would split the record.
                label = label.Replace(',', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();

                csv.Append(poi.Position.X.ToString(CultureInfo.InvariantCulture)).Append(',')
                   .Append(poi.Position.Y.ToString(CultureInfo.InvariantCulture)).Append(',')
                   .Append(poi.MapId.ToString(CultureInfo.InvariantCulture)).Append(',')
                   .Append(label).Append(',')
                   .Append(icons.TryGetValue(subtype, out string? icon) ? icon : "").Append(',')
                   .Append("yellow").Append(",3\n");
                count++;
            }

            Directory.CreateDirectory(dir);
            // Write to a temp file first so ClassicUO never reads a half-written marker file.
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, csv.ToString().Replace("\n", Environment.NewLine), new UTF8Encoding(false));
            if (File.Exists(path))
                File.Delete(path);
            File.Move(tmp, path);
            DebugLog.Write($"VendorMarkers: wrote {count} markers to {path}");
        }

        /// <summary>
        /// Downloads the icon of every subtype in use, converts the GIFs to PNG and saves them as
        /// <c>Data/Client/MapIcons/sw_&lt;name&gt;.png</c> scaled to 50%, refreshed on every start; ClassicUO only loads .png/.jpg/.cur/.ico there).
        /// Returns subtype -> ClassicUO icon name.
        /// </summary>
        private static Dictionary<string, string> DownloadIcons(PoiList list, List<Poi> pois, string iconBaseUrl, string iconDir)
        {
            var result = new Dictionary<string, string>();
            var paths = new Dictionary<string, string>();
            if (list.Types != null)
                foreach (var type in list.Types.Values)
                    if (type.Subtypes != null)
                        foreach (var sub in type.Subtypes)
                            if (!string.IsNullOrEmpty(sub.Value.Icon))
                                paths[sub.Key] = sub.Value.Icon!;

            Directory.CreateDirectory(iconDir);
            var baseUri = new Uri(iconBaseUrl.EndsWith("/") ? iconBaseUrl : iconBaseUrl + "/");
            using (var client = new WebClient())
            {
                foreach (Poi poi in pois)
                {
                    string? subtype = poi.Subtype;
                    if (string.IsNullOrEmpty(subtype) || result.ContainsKey(subtype!) || !paths.TryGetValue(subtype!, out string? iconPath))
                        continue;

                    // Marker icon names are lowercased by ClassicUO; keep the file name safe and separate from the user's own icons.
                    var name = new StringBuilder("sw_");
                    foreach (char c in subtype!.ToLowerInvariant())
                        name.Append(char.IsLetterOrDigit(c) ? c : '_');
                    string file = Path.Combine(iconDir, name + ".png");

                    try
                    {
                        byte[] data = client.DownloadData(new Uri(baseUri, iconPath));
                        File.WriteAllBytes(file, GifToPng.Convert(data, IconScale));
                        result[subtype] = name.ToString();
                    }
                    catch (Exception ex)
                    {
                        DebugLog.Write($"VendorMarkers: icon {iconPath} failed: {ex.Message}");
                    }
                }
            }
            DebugLog.Write($"VendorMarkers: {result.Count} icons available in {iconDir}");
            return result;
        }

        // Mirrors CUOEnviroment.ExecutablePath: AppContext.BaseDirectory on .NET Framework, the working directory otherwise.
        private static string ClientDirectory()
            => RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework", StringComparison.Ordinal)
                ? AppContext.BaseDirectory
                : Environment.CurrentDirectory;
    }
}
