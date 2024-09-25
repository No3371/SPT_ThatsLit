using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace ThatsLit
{
    public static class ThatsLitCompat
    {
        public const int MIN_COMPATIBLE_PROTOCOL = 1;

        static ThatsLitCompat()
        {
            ScopeTemplates   = new Dictionary<string, ScopeTemplate>();
            Scopes           = new Dictionary<string, Scope>();
            GoggleTemplates  = new Dictionary<string, GoggleTemplate>();
            Goggles          = new Dictionary<string, Goggle>();
            DeviceTemplates  = new Dictionary<string, DeviceTemplate>();
            Devices          = new Dictionary<string, Device>();
            ExtraDevices     = new Dictionary<string, Device>();
        }

         public static Dictionary<string, ScopeTemplate> ScopeTemplates    { get; private set; }
         public static Dictionary<string, Scope> Scopes                    { get; private set; }
         public static Dictionary<string, GoggleTemplate> GoggleTemplates  { get; private set; }
         public static Dictionary<string, Goggle> Goggles                  { get; private set; }
         public static Dictionary<string, DeviceTemplate> DeviceTemplates  { get; private set; }
         public static Dictionary<string, Device> Devices                  { get; private set; }
         public static Dictionary<string, Device> ExtraDevices             { get; private set; }


        static Task running;
        public static Task LoadCompatFiles ()
        {
            if (running != null && running.IsCompleted == false) return running;
            ScopeTemplates.Clear();
            Scopes.Clear();
            GoggleTemplates.Clear();
            Goggles.Clear();
            DeviceTemplates.Clear();
            Devices.Clear();
            ExtraDevices.Clear();
            running = Task.Run(() => {
                List<CompatFile> loaded = new List<CompatFile>();
                IEnumerable<string> paths = Directory.EnumerateFiles(BepInEx.Paths.PluginPath, "**thatslitcompat.json", SearchOption.AllDirectories);
                foreach (var path in paths)
                {
                    CompatFile file = Newtonsoft.Json.JsonConvert.DeserializeObject<CompatFile>(File.ReadAllText(path));
                    if (file == null)
                    {
                        var message = $"[That's Lit] Invalid thatslitcompat file: { file }";
                        NotificationManagerClass.DisplayWarningNotification(message);
                        Logger.LogError(message);

                        continue;
                    }
                    if (file.protocol < MIN_COMPATIBLE_PROTOCOL)
                    {
                        var message = $"[That's Lit] Incompatible outdated thatslitcompat file: { file }, minimum compatible protocol: { MIN_COMPATIBLE_PROTOCOL }";
                        NotificationManagerClass.DisplayWarningNotification(message);
                        Logger.LogError(message);
                        continue;
                    }
                    file.FilePath = path;
                    loaded.Add(file);
                }
                loaded.Sort();
                foreach (var f in loaded)
                {
                    if (ThatsLitPlugin.DebugCompat.Value)
                        Logger.LogWarning($"[That's Lit Debug] Loading compat file: { f.FilePath }");

                    if (f.scopeTemplates != null)
                    foreach (var c in f.scopeTemplates)
                    {
                        ScopeTemplates[c.name] = c;
                        if (ThatsLitPlugin.DebugCompat.Value)
                            Logger.LogWarning($"[That's Lit Debug] Scope Template: { c.name }");
                    }

                    if (f.goggleTemplates != null)
                    foreach (var c in f.goggleTemplates)
                    {
                        GoggleTemplates[c.name] = c;
                        if (ThatsLitPlugin.DebugCompat.Value)
                            Logger.LogWarning($"[That's Lit Debug] Goggle Template: { c.name }");
                    }

                    if (f.deviceTemplates != null)
                    foreach (var c in f.deviceTemplates)
                    {
                        if (c.modes == null || c.modes.Length == 0)
                        {
                            Logger.LogError($"[That's Lit] Device Template: { c.name } have 0 mode defined");
                            // continue; // keep it for references
                        }
                        DeviceTemplates[c.name] = c;
                        if (ThatsLitPlugin.DebugCompat.Value)
                            Logger.LogWarning($"[That's Lit Debug] Device Template: { c.name }");
                    }

                    if (f.scopes != null)
                    foreach (var c in f.scopes)
                    {
                        Scopes[c.id] = c;
                        if (ThatsLitPlugin.DebugCompat.Value)
                            Logger.LogWarning($"[That's Lit Debug] Scope: { c.id }");
                    }

                    if (f.goggles != null)
                    foreach (var c in f.goggles)
                    {
                        Goggles[c.id] = c;
                        if (ThatsLitPlugin.DebugCompat.Value)
                            Logger.LogWarning($"[That's Lit Debug] Goggle: { c.id }");
                    }

                    if (f.devices != null)
                    foreach (var c in f.devices)
                    {
                        if (c.TemplateInstance == null)
                        {
                            Logger.LogError($"[That's Lit] Device: { c.id } template is invalid: { c.template }");
                            continue;
                        }
                        if (c.TemplateInstance.modes == null || c.TemplateInstance.modes.Length == 0)
                        {
                            Logger.LogError($"[That's Lit] Device: { c.id } have 0 mode defined");
                            continue;
                        }

                        Devices[c.id] = c;
                        if (ThatsLitPlugin.DebugCompat.Value)
                            Logger.LogWarning($"[That's Lit Debug] Device: { c.id }");
                    }

                    if (f.extraDevices != null)
                    foreach (var c in f.extraDevices)
                    {
                        if (c.TemplateInstance == null)
                        {
                            Logger.LogError($"[That's Lit] Device: { c.id } template is invalid: { c.template }");
                            continue;
                        }
                        if (c.TemplateInstance.modes == null || c.TemplateInstance.modes.Length == 0)
                        {
                            Logger.LogError($"[That's Lit] Device: { c.id } have 0 mode defined");
                            continue;
                        }
                        ExtraDevices[c.id] = c;
                        if (ThatsLitPlugin.DebugCompat.Value)
                            Logger.LogWarning($"[That's Lit Debug] Extra Device: { c.id }");
                    }
                }
            }).ContinueWith(t => {
                running = null;
                if (t.IsFaulted) Logger.LogError(t.Exception.Flatten());
            });

            return running;
        }

        [System.Serializable]
        public class Device
        {
            public string id { get; set; }
            public string template { get; set; }
            private DeviceTemplate templateInstance;
            public DeviceTemplate TemplateInstance
            {
                get
                {
                    if (templateInstance == null)
                    {
                        ThatsLitCompat.DeviceTemplates.TryGetValue(template, out templateInstance);
                    }
                    return templateInstance;
                }
            }
        }

        [System.Serializable]
        public class DeviceTemplate
        {
            public string name { get; set; }
            public DeviceMode[] modes { get; set; }
            public DeviceMode SafeGetMode (int mode, bool fallbackLast = true)
            {
                if (modes == null || modes.Length == 0) return default;
                if (modes.Length <= mode)
                    if (fallbackLast)
                        if (modes.Length <= 0)
                            return default;
                        else
                            mode = modes.Length - 1;
                    else return default;
                return modes[mode];
            }
        }

        [System.Serializable]
        public class Goggle
        {
            public string id { get; set; }
            public string template { get; set; }
            private GoggleTemplate templateInstance;
            public GoggleTemplate TemplateInstance
            {
                get
                {
                    if (templateInstance == null)
                    {
                        ThatsLitCompat.GoggleTemplates.TryGetValue(template, out templateInstance);
                    }
                    return templateInstance;
                }
            }
        }

        [System.Serializable]
        public class GoggleTemplate
        {
            public string name { get; set; }
            public NightVision nightVision { get; set; }
            public Thermal thermal { get; set; }
        }

        [System.Serializable]
        public struct DeviceMode
        {
            public float light { get; set; }
            public float laser { get; set; }
            public float irLight { get; set; }
            public float irLaser { get; set; }
            public static DeviceMode MergeMax (DeviceMode a, DeviceMode b)
            {
                a.light = a.light > b.light ? a.light : b.light;
                a.laser = a.laser > b.laser ? a.laser : b.laser;
                a.irLight = a.irLight > b.irLight ? a.irLight : b.irLight;
                a.irLaser = a.irLaser > b.irLaser ? a.irLaser : b.irLaser;
                return a;
            }
        }

        [System.Serializable]
        public class NightVision
        {
            public int horizontalFOV { get; set; }
            public int verticalFOV { get; set; }
            public float nullification { get; set; }
            public float nullificationDarker { get; set; }
            public float nullificationExtremeDark { get; set; }
        }

        [System.Serializable]
        public class CompatFile: IComparable<CompatFile>
        {
            public int protocol { get; set; }
            public string FilePath { get; internal set; }
            public int priority { get; set; } = 999;
            public ScopeTemplate[] scopeTemplates { get; set; }
            public Scope[] scopes { get; set; }
            public GoggleTemplate[] goggleTemplates { get; set; }
            public Goggle[] goggles { get; set; }
            public DeviceTemplate[] deviceTemplates { get; set; }
            public Device[] devices { get; set; }
            public Device[] extraDevices { get; set; }

            public int CompareTo(CompatFile other)
            {
                return priority - other.priority;
            }
        }

        [System.Serializable]
        public class Scope
        {

            public string id { get; set; }
            public string template { get; set; }
            private ScopeTemplate templateInstance;
            public ScopeTemplate TemplateInstance
            {
                get
                {
                    if (templateInstance == null)
                    {
                        ThatsLitCompat.ScopeTemplates.TryGetValue(template, out templateInstance);
                    }
                    return templateInstance;
                }
            }
        }

        [System.Serializable]
        public class ScopeTemplate
        {
            public string name { get; set; }
            public NightVision nightVision { get; set; }
            public Thermal thermal { get; set; }
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