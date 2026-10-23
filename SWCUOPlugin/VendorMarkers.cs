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
        public const string FileName = "SWCUOPlugin_Vendors.csv";

        [DataContract]
        private class PoiList
        {
            [DataMember(Name = "pois")] public List<Poi>? Pois { get; set; }
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
        public static void UpdateAsync(string url)
        {
            Task.Run(() =>
            {
                try
                {
                    Update(url);
                }
                catch (Exception ex)
                {
                    DebugLog.Write($"VendorMarkers: update failed: {ex}");
                }
            });
        }

        private static void Update(string url)
        {
            string dir = Path.Combine(ClientDirectory(), "Data", "Client");
            string path = Path.Combine(dir, FileName);

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            byte[] json;
            using (var client = new WebClient())
                json = client.DownloadData(url);

            List<Poi>? pois;
            using (var stream = new MemoryStream(json))
                pois = ((PoiList?)new DataContractJsonSerializer(typeof(PoiList)).ReadObject(stream))?.Pois;
            if (pois == null || pois.Count == 0)
            {
                DebugLog.Write("VendorMarkers: response contained no POIs, keeping existing file");
                return;
            }

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
                   .Append(subtype.Replace(',', ' ').ToLowerInvariant()).Append(',')
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

        // Mirrors CUOEnviroment.ExecutablePath: AppContext.BaseDirectory on .NET Framework, the working directory otherwise.
        private static string ClientDirectory()
            => RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework", StringComparison.Ordinal)
                ? AppContext.BaseDirectory
                : Environment.CurrentDirectory;
    }
}
