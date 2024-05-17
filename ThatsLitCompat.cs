using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace ThatsLit
{
    public static class ThatsLitCompat
    {

        static ThatsLitCompat()
        {
            ScopeTemplates = new Dictionary<string, ScopeTemplate>();
            Scopes = new Dictionary<string, Scope>();
            GoggleTemplates = new Dictionary<string, GoggleTemplate>();
            Goggles = new Dictionary<string, Goggle>();
            DeviceTemplates = new Dictionary<string, DeviceTemplate>();
            Devices = new Dictionary<string, Device>();
            Loaded = new List<CompatFile>();
        }

         public static Dictionary<string, ScopeTemplate> ScopeTemplates { get; private set; }
         public static Dictionary<string, Scope> Scopes { get; private set; }
         public static Dictionary<string, GoggleTemplate> GoggleTemplates { get; private set; }
         public static Dictionary<string, Goggle> Goggles { get; private set; }
         public static Dictionary<string, DeviceTemplate> DeviceTemplates { get; private set; }
         public static Dictionary<string, Device> Devices { get; private set; }
         static List<CompatFile> Loaded;


        public static Task LoadCompatFiles ()
        {
            return Task.Run(() => {
                IEnumerable<string> paths = Directory.EnumerateFiles(BepInEx.Paths.PluginPath, "**thatslitcompat.json", SearchOption.AllDirectories);
                foreach (var path in paths)
                {
                    CompatFile file = Newtonsoft.Json.JsonConvert.DeserializeObject<CompatFile>(File.ReadAllText(path));
                    if (file == null)
                    {
                        var message = $"Invalid thatslitcompat file: { file }";
                        NotificationManagerClass.DisplayWarningNotification(message);
                        Logger.LogWarning(message);
                        continue;
                    }
                    file.FilePath = path;
                    Loaded.Add(file);
                }
                Loaded.Sort();
                foreach (var f in Loaded)
                {
                    Logger.LogWarning(f.FilePath);
                    foreach (var c in f.scopeTemplates)
                    {
                        ScopeTemplates[c.name] = c;
                    }
                    foreach (var c in f.goggleTemplates)
                    {
                        GoggleTemplates[c.name] = c;
                    }
                    foreach (var c in f.deviceTemplates)
                    {
                        DeviceTemplates[c.name] = c;
                    }
                    foreach (var c in f.scopes)
                    {
                        Scopes[c.id] = c;
                    }
                    foreach (var c in f.goggles)
                    {
                        Goggles[c.id] = c;
                    }
                    foreach (var c in f.devices)
                    {
                        Devices[c.id] = c;
                    }
                }
            }).ContinueWith(t => {
                if (t.IsFaulted) Logger.LogError(t.Exception.Flatten());
            });
        }

        public static ScopeTemplate GetScopeTemplate (string id)
        {
            if (!ThatsLitCompat.Scopes.TryGetValue(id, out var scope)) return null;
            if (!ThatsLitCompat.ScopeTemplates.TryGetValue(scope.template, out var template)) return null;
            return template;
        }

        public static GoggleTemplate GetGoggleTemplate (string id)
        {
            if (!ThatsLitCompat.Goggles.TryGetValue(id, out var goggle)) return null;
            if (!ThatsLitCompat.GoggleTemplates.TryGetValue(goggle.template, out var template)) return null;
            return template;
        }
        public static DeviceTemplate GetDeviceTemplate (string id)
        {
            if (!ThatsLitCompat.Devices.TryGetValue(id, out var device)) return null;
            if (!ThatsLitCompat.DeviceTemplates.TryGetValue(device.template, out var template)) return null;
            return template;
        }

        [System.Serializable]
        public class Device
        {
            public string id { get; set; }
            public string _comment { get; set; }
            public string template { get; set; }
        }

        [System.Serializable]
        public class DeviceTemplate
        {
            public string name { get; set; }
            public DeviceMode[] modes { get; set; }
            public string _comment { get; set; }
        }

        [System.Serializable]
        public class Goggle
        {
            public string id { get; set; }
            public string template { get; set; }
            public string _comment { get; set; }
        }

        [System.Serializable]
        public class GoggleTemplate
        {
            public string name { get; set; }
            public NightVision nightVision { get; set; }
            public Thermal thermal { get; set; }
            public string _comment { get; set; }
        }

        [System.Serializable]
        public struct DeviceMode
        {
            public float light { get; set; }
            public float laser { get; set; }
            public float irLight { get; set; }
            public float irLaser { get; set; }
        }

        [System.Serializable]
        public class NightVision
        {
            public float visibilityBonusScale { get; set; }
            public int horizontalFOV { get; set; }
            public int verticalFOV { get; set; }
            public float nullification { get; set; }
            public float nullificationDarker { get; set; }
            public float nullificationExtremeDark { get; set; }
        }

        [System.Serializable]
        public class CompatFile: IComparable<CompatFile>
        {
            public string FilePath { get; internal set; }
            public int priority { get; set; }
            public ScopeTemplate[] scopeTemplates { get; set; }
            public Scope[] scopes { get; set; }
            public GoggleTemplate[] goggleTemplates { get; set; }
            public Goggle[] goggles { get; set; }
            public DeviceTemplate[] deviceTemplates { get; set; }
            public Device[] devices { get; set; }

            public int CompareTo(CompatFile other)
            {
                return priority - other.priority;
            }
        }

        [System.Serializable]
        public class Scope
        {
            public string id { get; set; }
            public string _comment { get; set; }
            public string template { get; set; }
        }

        [System.Serializable]
        public class ScopeTemplate
        {
            public string name { get; set; }
            public NightVision nightVision { get; set; }
            public Thermal thermal { get; set; }
            public string _comment { get; set; }
        }

        [System.Serializable]
        public class Thermal
        {
            public int effectiveDistance { get; set; }
            public int horizontalFOV { get; set; }
            public int verticalFOV { get; set; }
        }


    }
}