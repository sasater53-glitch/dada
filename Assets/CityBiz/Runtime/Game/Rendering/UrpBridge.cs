using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityBiz.Game
{
    /// <summary>
    /// Доступ к функциям URP через рефлексию: код компилируется и в проекте без URP,
    /// а при наличии URP включает постобработку (bloom ночных огней, тонмаппинг, цветокоррекцию).
    /// </summary>
    public sealed class UrpBridge
    {
        const string UrpAsm = "Unity.RenderPipelines.Universal.Runtime";
        const string CoreAsm = "Unity.RenderPipelines.Core.Runtime";

        object _bloom, _colorAdjustments, _vignette;

        public static bool IsUrpActive => GraphicsSettings.currentRenderPipeline != null &&
                                          GraphicsSettings.currentRenderPipeline.GetType().Name.Contains("Universal");

        static Type Find(string fullName, string asm) => Type.GetType(fullName + ", " + asm);

        public static void ConfigureCamera(Camera cam)
        {
            if (!IsUrpActive || cam == null) return;
            var type = Find("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData", UrpAsm);
            if (type == null) return;
            var data = cam.GetComponent(type) ?? cam.gameObject.AddComponent(type);
            SetProperty(data, "renderPostProcessing", true);
            SetEnumProperty(data, "antialiasing", 1); // FXAA — дёшево и сглаживает тонкие детали фасадов
            SetProperty(data, "renderShadows", true);
        }

        public static UrpBridge CreateGlobalVolume(Transform parent)
        {
            if (!IsUrpActive) return null;
            var volumeType = Find("UnityEngine.Rendering.Volume", CoreAsm);
            var profileType = Find("UnityEngine.Rendering.VolumeProfile", CoreAsm);
            if (volumeType == null || profileType == null) return null;
            try
            {
                var go = new GameObject("Post Processing");
                go.transform.SetParent(parent, false);
                var volume = go.AddComponent(volumeType);
                SetProperty(volume, "isGlobal", true);
                var profile = ScriptableObject.CreateInstance(profileType);
                SetProperty(volume, "sharedProfile", profile);

                var bridge = new UrpBridge();
                var add = profileType.GetMethod("Add", new[] { typeof(Type), typeof(bool) });
                object AddComponent(string name) =>
                    Find("UnityEngine.Rendering.Universal." + name, UrpAsm) is Type t ? add.Invoke(profile, new object[] { t, true }) : null;

                var tonemap = AddComponent("Tonemapping");
                SetParam(tonemap, "mode", 2, true); // ACES
                bridge._bloom = AddComponent("Bloom");
                SetParam(bridge._bloom, "threshold", 1.0f);
                SetParam(bridge._bloom, "intensity", 0.6f);
                SetParam(bridge._bloom, "scatter", 0.65f);
                bridge._colorAdjustments = AddComponent("ColorAdjustments");
                bridge._vignette = AddComponent("Vignette");
                SetParam(bridge._vignette, "intensity", 0.22f);
                SetParam(bridge._vignette, "smoothness", 0.4f);
                return bridge;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CityBiz] Постобработка URP недоступна: " + e.Message);
                return null;
            }
        }

        public void Apply(float exposure, float saturation, float contrast, Color filter, float bloom)
        {
            SetParam(_colorAdjustments, "postExposure", exposure);
            SetParam(_colorAdjustments, "saturation", saturation);
            SetParam(_colorAdjustments, "contrast", contrast);
            SetParam(_colorAdjustments, "colorFilter", filter);
            SetParam(_bloom, "intensity", bloom);
        }

        static void SetParam(object component, string field, object value, bool isEnum = false)
        {
            if (component == null) return;
            var f = component.GetType().GetField(field, BindingFlags.Public | BindingFlags.Instance);
            var param = f?.GetValue(component);
            if (param == null) return;
            var valueProp = param.GetType().GetProperty("value", BindingFlags.Public | BindingFlags.Instance);
            if (valueProp == null) return;
            var v = isEnum ? Enum.ToObject(valueProp.PropertyType, value) : value;
            valueProp.SetValue(param, v);
            param.GetType().GetProperty("overrideState")?.SetValue(param, true);
        }

        static void SetProperty(object target, string name, object value)
        {
            var p = target?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.CanWrite) p.SetValue(target, value);
        }

        static void SetEnumProperty(object target, string name, int value)
        {
            var p = target?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.CanWrite && p.PropertyType.IsEnum) p.SetValue(target, Enum.ToObject(p.PropertyType, value));
        }
    }
}
