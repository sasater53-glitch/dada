using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CityBiz.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CityBiz.EditorTools
{
    /// <summary>
    /// Настройка проекта в один клик: URP, сцена ТРЦ «Галерея», запекание статики и Occlusion Culling.
    /// При первом открытии проекта сцена создаётся автоматически.
    /// </summary>
    [InitializeOnLoad]
    public static class CityBizEditor
    {
        public const string ScenePath = "Assets/CityBiz/Scenes/Galleria.unity";
        const string BakeFolder = "Assets/CityBiz/Generated";
        const string SessionKey = "CityBiz.FirstRunChecked";

        static CityBizEditor()
        {
            if (Application.isBatchMode) return;
            EditorApplication.delayCall += FirstRun;
        }

        static void FirstRun()
        {
            if (SessionState.GetBool(SessionKey, false)) return;
            SessionState.SetBool(SessionKey, true);
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (GraphicsSettings.defaultRenderPipeline == null) EnsureUrp(false);
            if (!File.Exists(ScenePath)) CreateScene();
            // Если открыта пустая безымянная сцена — сразу открываем ТЦ
            var active = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(active.path) && !active.isDirty) EditorSceneManager.OpenScene(ScenePath);
        }

        // ---------------- URP ----------------

        [MenuItem("CityBiz/Настроить URP", priority = 20)]
        static void EnsureUrpMenu() => EnsureUrp(true);

        static void EnsureUrp(bool verbose)
        {
            if (GraphicsSettings.defaultRenderPipeline != null)
            {
                if (verbose) Debug.Log("[CityBiz] URP уже настроен: " + GraphicsSettings.defaultRenderPipeline.name);
                return;
            }
            var urpType = Type.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset, Unity.RenderPipelines.Universal.Runtime");
            if (urpType == null)
            {
                Debug.LogWarning("[CityBiz] Пакет URP не найден — используется встроенный конвейер. Установите com.unity.render-pipelines.universal для лучшей картинки.");
                return;
            }
            try
            {
                Directory.CreateDirectory("Assets/CityBiz/Settings");
                const string assetPath = "Assets/CityBiz/Settings/CityBiz_URP.asset";
                var asset = (RenderPipelineAsset)urpType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { null });
                AssetDatabase.CreateAsset(asset, assetPath);
                // Публичный API URP: создаёт и сохраняет Universal Renderer с данными постобработки
                var load = urpType.GetMethod("LoadBuiltinRendererData", BindingFlags.Public | BindingFlags.Instance);
                if (load != null)
                {
                    var rendererType = load.GetParameters()[0].ParameterType;
                    load.Invoke(asset, new[] { Enum.Parse(rendererType, "UniversalRenderer") });
                }
                // Внутри ТЦ тени нужны вблизи; точечных источников на объект — до 8 (бюджет света держит их число низким)
                SetProp(asset, "shadowDistance", 70f);
                SetProp(asset, "supportsHDR", true);
                SetProp(asset, "msaaSampleCount", 4);
                SetProp(asset, "maxAdditionalLightsCount", 8);
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
                GraphicsSettings.defaultRenderPipeline = asset;
                QualitySettings.renderPipeline = asset;
                if (PlayerSettings.colorSpace != ColorSpace.Linear) PlayerSettings.colorSpace = ColorSpace.Linear;
                Debug.Log("[CityBiz] Создан и назначен URP Asset: " + assetPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CityBiz] Не удалось автоматически настроить URP: " + e.Message + ". Создайте URP Asset вручную (Assets → Create → Rendering → URP Asset) и назначьте его в Project Settings → Graphics.");
            }
        }

        static void SetProp(object target, string name, object value)
        {
            var p = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.CanWrite) p.SetValue(target, value);
        }

        // ---------------- Сцена ----------------

        [MenuItem("CityBiz/Открыть ТРЦ «Галерея»", priority = 0)]
        static void OpenScene()
        {
            if (!File.Exists(ScenePath)) CreateScene();
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        static void CreateScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var go = new GameObject("CityGame");
            go.AddComponent<CityGame>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == ScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[CityBiz] Создана сцена " + ScenePath + ". Нажмите Play.");
        }

        // ---------------- Запекание ----------------

        [MenuItem("CityBiz/Запечь ТЦ (статика + Occlusion Culling)", priority = 40)]
        static void Bake()
        {
            if (!File.Exists(ScenePath)) CreateScene();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(ScenePath);
            RemoveBaked();

            MaterialLibrary.Reset();
            AssetCatalog.Reset();
            var world = MallRegistry.CreateWorld();
            var builder = new MallBuilder(MaterialLibrary.Instance, world);
            var root = new GameObject("Malls").transform;
            var malls = new List<MallInstance>();
            try
            {
                foreach (var bp in MallRegistry.All)
                {
                    var inst = builder.Build(bp, root);
                    inst.Baked = true;
                    malls.Add(inst);
                }

                SaveGeneratedAssets(root.gameObject, malls);
                // Меши теперь ассеты — компоненты-владельцы больше не нужны
                foreach (var owner in root.GetComponentsInChildren<GeneratedMesh>(true)) UnityEngine.Object.DestroyImmediate(owner);
                foreach (var m in malls) MarkStatic(m.transform.Find("Static"));
            }
            finally
            {
                // Скрытые шаблоны моделей не должны остаться в сцене
                AssetCatalog.DestroyTemplates();
            }

            StaticOcclusionCulling.smallestOccluder = 3f;
            StaticOcclusionCulling.smallestHole = 0.25f;
            StaticOcclusionCulling.backfaceThreshold = 100f;
            bool ok = StaticOcclusionCulling.Compute();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            MaterialLibrary.Reset();
            Debug.Log(ok ? "[CityBiz] ТЦ запечён: статика помечена, Occlusion Culling рассчитан." : "[CityBiz] ТЦ сохранён, но расчёт Occlusion Culling не удался.");
        }

        [MenuItem("CityBiz/Удалить запечённый ТЦ", priority = 41)]
        static void RemoveBakedMenu()
        {
            RemoveBaked();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        static void RemoveBaked()
        {
#if UNITY_6000_6_OR_NEWER
            var existing = UnityEngine.Object.FindObjectsByType<MallInstance>();
#else
            var existing = UnityEngine.Object.FindObjectsByType<MallInstance>(FindObjectsSortMode.None);
#endif
            foreach (var m in existing)
            {
                if (m == null) continue;
                var top = m.transform.parent != null ? m.transform.parent.gameObject : m.gameObject;
                UnityEngine.Object.DestroyImmediate(top);
            }
            StaticOcclusionCulling.Clear();
            if (AssetDatabase.IsValidFolder(BakeFolder)) AssetDatabase.DeleteAsset(BakeFolder);
        }

        /// <summary>Процедурные меши, материалы и текстуры сохраняются как под-ассеты одного файла.</summary>
        static void SaveGeneratedAssets(GameObject root, List<MallInstance> malls)
        {
            Directory.CreateDirectory(BakeFolder);
            var container = ScriptableObject.CreateInstance<GeneratedMallAssets>();
            string path = BakeFolder + "/MallAssets.asset";
            AssetDatabase.CreateAsset(container, path);
            var added = new HashSet<UnityEngine.Object>();

            void Add(UnityEngine.Object o)
            {
                if (o == null || added.Contains(o) || EditorUtility.IsPersistent(o)) return;
                added.Add(o);
                if (o is Material m)
                    foreach (int id in m.GetTexturePropertyNameIDs()) Add(m.GetTexture(id));
                AssetDatabase.AddObjectToAsset(o, container);
            }

            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true)) Add(mf.sharedMesh);
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials) Add(m);
            foreach (var ps in root.GetComponentsInChildren<ParticleSystemRenderer>(true)) Add(ps.sharedMaterial);
            foreach (var mall in malls)
            {
                var inst = mall.GetComponent<InstancedRenderer>();
                if (inst != null)
                    foreach (var b in inst.Batches) { Add(b.Mesh); Add(b.Material); }
                foreach (var e in mall.Materials) Add(e.Material);
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>Двери, вывески, эскалаторы, стенды, лифт и частицы меняются во время игры — их не помечаем статикой.</summary>
        static bool IsDynamic(Transform t) =>
            t.GetComponent<StorefrontView>() != null || t.GetComponent<AutoDoor>() != null || t.GetComponent<Escalator>() != null ||
            t.GetComponent<TextMesh>() != null || t.GetComponent<MallDirectoryPoint>() != null || t.GetComponent<ElevatorPanel>() != null ||
            t.GetComponent<ParticleSystem>() != null || t.GetComponent<SignMirror>() != null;

        static void MarkStatic(Transform t)
        {
            if (t == null || IsDynamic(t)) return;
            var flags = StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic;
            // Стекло не должно перекрывать видимость при расчёте Occlusion Culling
            var mr = t.GetComponent<MeshRenderer>();
            if (mr != null && mr.sharedMaterial != null && mr.sharedMaterial.renderQueue >= (int)RenderQueue.Transparent)
                flags &= ~StaticEditorFlags.OccluderStatic;
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
            foreach (Transform child in t) MarkStatic(child);
        }
    }
}
