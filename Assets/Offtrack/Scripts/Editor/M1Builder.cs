using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Offtrack.Core;
using Offtrack.Runtime;
using Offtrack.Runtime.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Offtrack.Editor
{
    public static class M1Builder
    {
        private const string Root = "Assets/Offtrack";
        private const string ConfigPath = Root + "/Config/GameConfig.asset";
        private const string ChartPath = Root + "/Charts/side-a.json";
        private const string MaterialPath = Root + "/Art/Generated/FlatPaletteUI.mat";
        private static Material material;
        private static ViewSettings view;

        [Serializable] private sealed class BeatSource { public BeatEntry[] beats = Array.Empty<BeatEntry>(); }
        [Serializable] private sealed class BeatEntry { public double beat = 0; public int lane = 0; }

        [MenuItem("Offtrack/Build M1 Scene")]
        public static void BuildAll()
        {
            EnsureFolder(Root + "/Config");
            EnsureFolder(Root + "/Charts");
            EnsureFolder(Root + "/Prefabs");
            EnsureFolder(Root + "/Art/Generated");
            EnsureFolder("Assets/Scenes");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<GameConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            config.ToRules().Validate();
            view = config.View;
            var shader = Shader.Find("Offtrack/FlatPaletteUI");
            if (shader == null) throw new InvalidOperationException("找不到四色 UI Shader。");
            material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "FlatPaletteUI" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else material.shader = shader;

            GenerateChart(config);
            var discPrefab = SaveDiscPrefab(config);
            var armPrefab = SaveTonearmPrefab();
            var scratchPrefab = SaveScratchPrefab();
            AssetDatabase.SaveAssets();
            BuildScene(config, discPrefab, armPrefab, scratchPrefab);
            config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            PlayerSettings.defaultScreenWidth = 585;
            PlayerSettings.defaultScreenHeight = 1266;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            // 平涂 UI 无光照；Gamma 让顶点色与策划十六进制色值直接对应。
            PlayerSettings.colorSpace = ColorSpace.Gamma;
            QualitySettings.antiAliasing = 0;
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Main.unity", true) };
            EditorUtility.SetDirty(config);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("M1 BuildAll 完成：Main、3 个预制体、配置与 22 条 JSON 谱面（幂等入口）。");
        }

        [MenuItem("Offtrack/Generate M1 Chart")]
        public static void GenerateChartMenu()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config == null) throw new InvalidOperationException("请先运行 Build M1 Scene。");
            GenerateChart(config);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static void GenerateChart(GameConfig config)
        {
            var input = JsonUtility.FromJson<BeatSource>(File.ReadAllText(Root + "/Charts/side-a.beats.json", Encoding.UTF8));
            var beats = input.beats.Select(item => new BeatNote(item.beat, item.lane)).ToArray();
            var chart = ChartTools.FromBeats(config.Bpm, beats, config.LaneCount);
            File.WriteAllText(ChartPath, ChartService.ToJson(chart) + Environment.NewLine, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(ChartPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static GameObject SaveDiscPrefab(GameConfig config)
        {
            var disc = Node("Disc", null, Vector2.zero, Vector2.one * view.DiscRadius * 2);
            Shape("DiscSurface", disc, Vector2.zero, disc.sizeDelta, ShapeKind.Disc, Palette.Ink);
            Shape("DiscEdge", disc, Vector2.zero, disc.sizeDelta, ShapeKind.Ring, Palette.DarkRed, view.OutlineWidth);
            for (var i = 0; i < view.GrooveCount; i++)
            {
                var ratio = Mathf.Lerp(view.GrooveInnerRatio, view.GrooveOuterRatio, (float)i / (view.GrooveCount - 1));
                Shape("Groove_" + i.ToString("00"), disc, Vector2.zero, Vector2.one * view.DiscRadius * ratio * 2,
                    ShapeKind.Ring, Palette.DarkRed, view.GrooveWidth);
            }
            for (var i = 0; i < config.LaneCount; i++)
                Shape("Lane_" + i, disc, Vector2.zero, Vector2.one * view.DiscRadius * view.LaneRadiusRatios[i] * 2,
                    ShapeKind.Ring, Palette.DarkRed, view.LaneWidth);
            var labelSize = Vector2.one * view.DiscRadius * view.LabelRadiusRatio * 2;
            Shape("Label", disc, Vector2.zero, labelSize, ShapeKind.Disc, Palette.DarkRed);
            Shape("LabelInner", disc, Vector2.zero, labelSize * view.LabelInnerRatio, ShapeKind.Ring, Palette.Ink, view.GrooveWidth);
            Text("Title", disc, view.LabelTitlePosition, new Vector2(labelSize.x, view.LabelFontSize * 2),
                "OFFTRACK", view.LabelFontSize, Palette.Ink);
            Text("Detail", disc, view.LabelDetailPosition, new Vector2(labelSize.x, view.LabelDetailFontSize * 2),
                "SIDE A / " + config.Bpm.ToString("0.##", CultureInfo.InvariantCulture) + " BPM", view.LabelDetailFontSize, Palette.Ink);
            Shape("LabelRule", disc, Vector2.zero, view.LabelRuleSize, ShapeKind.Rectangle, Palette.Ink);
            Shape("Spindle", disc, Vector2.zero, Vector2.one * view.DiscRadius * view.SpindleRatio * 2,
                ShapeKind.Disc, Palette.Ink);
            Node("Scratches", disc, Vector2.zero, disc.sizeDelta).gameObject.AddComponent<ScratchPool>();
            disc.gameObject.AddComponent<DiscController>();
            return SavePrefab(disc.gameObject, Root + "/Prefabs/Disc.prefab");
        }

        private static GameObject SaveTonearmPrefab()
        {
            var arm = Node("Tonearm", null, Vector2.zero, Vector2.zero);
            arm.pivot = Vector2.one / 2;
            var line = Shape("Arm", arm, Vector2.zero, new Vector2(view.DiscRadius, view.ArmWidth), ShapeKind.Rectangle, Palette.Bone);
            line.rectTransform.pivot = new Vector2(0, .5f);
            Shape("Pivot", arm, Vector2.zero, Vector2.one * view.PivotRadius * 2, ShapeKind.Disc, Palette.Bone);
            Shape("Head", arm, Vector2.zero, view.HeadSize, ShapeKind.Rectangle, Palette.Bone);
            Shape("Tip", arm, Vector2.zero, Vector2.one * view.TipRadius * 2, ShapeKind.Disc, Palette.Bone);
            arm.gameObject.AddComponent<TonearmView>();
            return SavePrefab(arm.gameObject, Root + "/Prefabs/Tonearm.prefab");
        }

        private static GameObject SaveScratchPrefab()
        {
            var scratch = Node("Scratch", null, Vector2.zero, view.ScratchSize);
            Shape("Cut", scratch, Vector2.zero, view.ScratchSize, ShapeKind.Rectangle, Palette.Red);
            var cross = Shape("Cross", scratch, Vector2.zero, view.ScratchCrossSize, ShapeKind.Rectangle, Palette.Red);
            cross.rectTransform.localRotation = Quaternion.Euler(0, 0, view.ScratchCrossAngle);
            return SavePrefab(scratch.gameObject, Root + "/Prefabs/Scratch.prefab");
        }

        private static GameObject SavePrefab(GameObject temporary, string path)
        {
            var asset = PrefabUtility.SaveAsPrefabAsset(temporary, path);
            UnityEngine.Object.DestroyImmediate(temporary);
            return asset;
        }

        private static void BuildScene(GameConfig config, GameObject discPrefab, GameObject armPrefab, GameObject scratchPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // NewScene 会卸载未被场景引用的资源；局部托管引用不能保活这些 Unity 对象。
            config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            discPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Disc.prefab");
            armPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Tonearm.prefab");
            scratchPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Scratch.prefab");
            material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            view = config.View;
            var cameraObject = new GameObject("Main Camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0, 0, -10);
            var camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = view.ReferenceResolution.y / view.PixelsPerUnit / 2;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Palette.Ink;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false;
            cameraData.antialiasing = AntialiasingMode.None;

            var canvasObject = new GameObject("Stage", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1;
            canvas.pixelPerfect = true;
            canvas.referencePixelsPerUnit = view.PixelsPerUnit;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = view.ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            scaler.referencePixelsPerUnit = view.PixelsPerUnit;
            var frame = Node("ReferenceFrame", canvasObject.transform, Vector2.zero, view.ReferenceResolution);

            var flowObject = new GameObject("GameFlow");
            var flow = flowObject.AddComponent<GameFlow>();
            flow.Configure(config, AssetDatabase.LoadAssetAtPath<TextAsset>(ChartPath));
            var discObject = (GameObject)PrefabUtility.InstantiatePrefab(discPrefab, frame);
            var disc = (RectTransform)discObject.transform;
            disc.anchoredPosition = view.DiscCenter;
            discObject.GetComponent<DiscController>().Configure(flow, disc);
            var pool = disc.Find("Scratches").GetComponent<ScratchPool>();
            var items = new GameObject[config.ScratchPoolSize];
            for (var i = 0; i < items.Length; i++)
            {
                items[i] = (GameObject)PrefabUtility.InstantiatePrefab(scratchPrefab, pool.transform);
                items[i].name = "Scratch_" + i.ToString("00");
                items[i].SetActive(false);
            }
            pool.Configure(flow, items);
            for (var i = 0; i < config.LaneCount; i++)
            {
                var position = view.DiscCenter + Vector2.up * view.DiscRadius * view.LaneRadiusRatios[i];
                Shape("Contact_" + i, frame, position, view.ContactSize, ShapeKind.Rectangle, Palette.DarkRed);
                Text("ContactNumber_" + i, frame, position + Vector2.left * view.ContactNumberOffset,
                    new Vector2(view.ContactFontSize * 2, view.ContactFontSize * 2),
                    (i + 1).ToString("00"), view.ContactFontSize, Palette.DarkRed);
            }
            var armObject = (GameObject)PrefabUtility.InstantiatePrefab(armPrefab, frame);
            var arm = (RectTransform)armObject.transform;
            arm.anchoredPosition = view.ArmPivot;
            armObject.GetComponent<TonearmView>().Configure(flow, (RectTransform)arm.Find("Arm"),
                (RectTransform)arm.Find("Head"), arm.Find("Tip").GetComponent<FlatShape>());
            var hud = BuildHud(flow, frame, config);
            flowObject.AddComponent<Judge>().Configure(flow);
            flowObject.AddComponent<InputRouter>().Configure(flow, camera, hud.StartButton);
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Main.unity");
        }

        private static HudView BuildHud(GameFlow flow, RectTransform frame, GameConfig config)
        {
            var timer = Text("Timer", frame, view.TimerPosition, new Vector2(view.ProgressSize.x, view.TimerFontSize * 2),
                "00.0 / " + config.Duration.ToString("0.##", CultureInfo.InvariantCulture) + "s", view.TimerFontSize, Palette.DarkRed);
            // 一根细框 + 实心进度，不做半透明背景。
            var top = view.ProgressPosition + Vector2.up * view.OutlineWidth;
            var bottom = view.ProgressPosition - Vector2.up * view.OutlineWidth;
            Shape("ProgressTop", frame, top, new Vector2(view.ProgressSize.x, view.GrooveWidth), ShapeKind.Rectangle, Palette.DarkRed);
            Shape("ProgressBottom", frame, bottom, new Vector2(view.ProgressSize.x, view.GrooveWidth), ShapeKind.Rectangle, Palette.DarkRed);
            var fill = Shape("Progress", frame, view.ProgressPosition - Vector2.right * view.ProgressSize.x / 2,
                new Vector2(0, view.ProgressSize.y), ShapeKind.Rectangle, Palette.DarkRed).rectTransform;
            fill.pivot = new Vector2(0, .5f);
            var cells = new FlatShape[config.Lives];
            var step = view.LifeSize.x + view.LifeGap;
            for (var i = 0; i < cells.Length; i++)
                cells[i] = Shape("Life_" + i, frame, view.LivesPosition + Vector2.right * (i - (cells.Length - 1) / 2f) * step,
                    view.LifeSize, ShapeKind.Rectangle, Palette.Bone);

            var idle = Node("Idle", frame, Vector2.zero, view.ReferenceResolution);
            var play = Shape("PlayButton", idle, view.PlayPosition, Vector2.one * view.PlayRadius * 2,
                ShapeKind.Ring, Palette.DarkRed, view.OutlineWidth).rectTransform;
            Shape("PlayIcon", play, Vector2.zero, view.PlayTriangleSize, ShapeKind.Triangle, Palette.Bone);
            Text("StartCopy", idle, view.IdleTextPosition, new Vector2(view.ProgressSize.x, view.BodyFontSize * 2),
                "点我开始！！", view.BodyFontSize, Palette.DarkRed);

            var result = Node("Result", frame, Vector2.zero, view.ReferenceResolution);
            var title = Text("ResultTitle", result, view.ResultTitlePosition, new Vector2(view.ProgressSize.x, view.ResultFontSize * 2),
                "THE NEEDLE NEEDS A BREAK", view.ResultFontSize, Palette.DarkRed);
            var body = Text("ResultBody", result, view.ResultBodyPosition, new Vector2(view.ProgressSize.x, view.BodyFontSize * 2),
                "音乐暂时停了。", view.BodyFontSize, Palette.DarkRed);
            var replay = Node("ReplayButton", result, view.ReplayPosition, view.ReplaySize);
            Shape("Top", replay, Vector2.up * view.ReplaySize.y / 2, new Vector2(view.ReplaySize.x, view.OutlineWidth), ShapeKind.Rectangle, Palette.DarkRed);
            Shape("Bottom", replay, Vector2.down * view.ReplaySize.y / 2, new Vector2(view.ReplaySize.x, view.OutlineWidth), ShapeKind.Rectangle, Palette.DarkRed);
            Shape("Left", replay, Vector2.left * view.ReplaySize.x / 2, new Vector2(view.OutlineWidth, view.ReplaySize.y), ShapeKind.Rectangle, Palette.DarkRed);
            Shape("Right", replay, Vector2.right * view.ReplaySize.x / 2, new Vector2(view.OutlineWidth, view.ReplaySize.y), ShapeKind.Rectangle, Palette.DarkRed);
            Text("ReplayCopy", replay, Vector2.zero, view.ReplaySize, "再来一把！", view.BodyFontSize, Palette.DarkRed);
            result.gameObject.SetActive(false);
            var hud = flow.gameObject.AddComponent<HudView>();
            hud.Configure(flow, timer, fill, cells, idle.gameObject, result.gameObject, title, body, play, replay);
            return hud;
        }

        private static RectTransform Node(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = Vector2.one / 2;
            rect.pivot = Vector2.one / 2;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static FlatShape Shape(string name, Transform parent, Vector2 position, Vector2 size, ShapeKind kind, Color color, float stroke = 0)
        {
            var shape = Node(name, parent, position, size).gameObject.AddComponent<FlatShape>();
            shape.Kind = kind;
            shape.color = color;
            shape.material = material;
            shape.raycastTarget = false;
            shape.Segments = view.CircleSegments;
            shape.StrokeWidth = stroke;
            shape.SetAllDirty();
            return shape;
        }

        private static Text Text(string name, Transform parent, Vector2 position, Vector2 size, string copy, int fontSize, Color color)
        {
            var text = Node(name, parent, position, size).gameObject.AddComponent<Text>();
            text.font = FontProvider.Fallback;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.material = material;
            text.text = copy;
            text.gameObject.AddComponent<FontBinding>();
            return text;
        }

        private static void EnsureFolder(string path)
        {
            var segments = path.Split('/');
            var parent = segments[0];
            for (var i = 1; i < segments.Length; i++)
            {
                var next = parent + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(parent, segments[i]);
                parent = next;
            }
        }
    }
}
