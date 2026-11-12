using System;
using System.Linq;
using Offtrack.Runtime;
using Offtrack.Runtime.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Offtrack.Editor
{
    public static class M2Builder
    {
        private const string Root = "Assets/Offtrack";
        private const string ConfigPath = Root + "/Config/GameConfig.asset";
        private const string MaterialPath = Root + "/Art/Generated/FlatPaletteUI.mat";
        private const string DialogPath = Root + "/Prefabs/Dialog.prefab";
        private static Material material;
        private static GameConfig config;
        private static ShellViewSettings view;

        [MenuItem("Offtrack/Build M2 Journey")]
        public static void BuildAll()
        {
            // Regenerate the original record assets through their original Editor API builder.
            // M2 then composes the shell without changing any gameplay rule or fixture.
            M1Builder.BuildAll();
            BuildMain();
            BuildCutscene(true);
            BuildCutscene(false);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene("Assets/Scenes/CutsceneA.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/Main.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/CutsceneB.unity", true)
            };
            WindowsExport.ApplyExportSettings();
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene("Assets/Scenes/CutsceneA.unity");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("M2 BuildAll 完成：CutsceneA → Main → CutsceneB，Dialog prefab，所有配置保持可编辑。");
        }

        private static void LoadAssets()
        {
            config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (config == null || material == null) throw new InvalidOperationException("Run the M1 asset builder first.");
            view = config.ShellView;
            config.Shell.Validate();
            EditorUtility.SetDirty(config);
        }

        private static void BuildMain()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            LoadAssets();
            var camera = Camera.main;
            var canvas = UnityEngine.Object.FindAnyObjectByType<Canvas>();
            canvas.name = "PhoneShell";
            var frame = (RectTransform)canvas.transform.Find("ReferenceFrame");
            var flow = UnityEngine.Object.FindAnyObjectByType<GameFlow>();
            var hud = flow.GetComponent<HudView>();
            var recordNodes = frame.Cast<Transform>().Where(item => item.name == "Disc" || item.name == "Tonearm"
                || item.name.StartsWith("Contact_", StringComparison.Ordinal)
                || item.name.StartsWith("ContactNumber_", StringComparison.Ordinal)).ToArray();
            var stage = Node("GameStage", frame, view.StagePosition, view.StageSize);
            stage.SetAsFirstSibling();
            var record = Node("RecordGroup", stage, view.RecordOrigin, config.View.ReferenceResolution);
            record.localScale = Vector3.one * view.RecordScale;
            foreach (var item in recordNodes) item.SetParent(record, false);

            Relocate(frame, "Timer", view.TimerPosition, view.TimerSize);
            frame.Find("Timer").GetComponent<Text>().fontSize = view.TimerFontSize;
            Relocate(frame, "ProgressTop", view.ProgressPosition + Vector2.up * config.View.OutlineWidth);
            Relocate(frame, "ProgressBottom", view.ProgressPosition - Vector2.up * config.View.OutlineWidth);
            Relocate(frame, "Progress", view.ProgressPosition - Vector2.right * config.View.ProgressSize.x / 2);
            for (int i = 0; i < config.Lives; i++)
                Relocate(frame, "Life_" + i, view.LivesPosition + Vector2.right *
                    (i - (config.Lives - 1) / 2f) * (view.LifeSize.x + view.LifeGap), view.LifeSize);

            var play = Relocate(frame, "Idle/PlayButton", view.PlayPosition, view.PlaySize);
            // The prototype's play button is square; border, fill and icon are opaque palette colors.
            var oldRing = play.GetComponent<FlatShape>();
            UnityEngine.Object.DestroyImmediate(oldRing);
            Outline("Border", play, Vector2.zero, view.PlaySize, Palette.Red);
            play.Find("PlayIcon").GetComponent<FlatShape>().color = Palette.Red;
            var hint = Relocate(frame, "Idle/StartCopy", view.HintPosition, view.HintSize).GetComponent<Text>();
            hint.text = "点我开始！！ ↖"; hint.fontSize = view.HintFontSize; hint.color = Palette.Red;
            Relocate(frame, "Result/ResultTitle", view.CompleteTitlePosition);
            Relocate(frame, "Result/ResultBody", view.CompleteBodyPosition);
            frame.Find("Result/ReplayButton").gameObject.SetActive(false);

            Text("StatusTime", frame, new Vector2(-300, 818), new Vector2(84, 40), "9:41", 22, Palette.Bone);
            BuildStatusIcons(frame);
            Shape("AdBadge", frame, new Vector2(-328, 764), new Vector2(60, 30), Palette.DarkRed);
            Text("AdCopy", frame, new Vector2(-328, 764), new Vector2(60, 30), "广告", 18, Palette.Bone);
            var skipFill = Shape("SkipButton", frame, view.SkipPosition, view.SkipSize, Palette.DarkRed);
            var skipLabel = Text("SkipCopy", skipFill.transform, Vector2.zero, view.SkipSize, "跳过 8", view.SkipFontSize, Palette.Bone);
            AppIcon(frame, new Vector2(-318, 680), 80);
            Text("BrandName", frame, new Vector2(-108, 696), new Vector2(300, 46), "网易云音乐", 30, Palette.Bone, TextAnchor.MiddleLeft);
            Text("BrandSubtitle", frame, new Vector2(-108, 660), new Vector2(300, 36), "黑胶 VIP · 开屏广告", 20, Palette.DarkRed, TextAnchor.MiddleLeft);

            var dialogPrefab = SaveDialogPrefab();
            var dialog = ((GameObject)PrefabUtility.InstantiatePrefab(dialogPrefab, frame)).GetComponent<DialogView>();
            var dialogRect = (RectTransform)dialog.transform;
            dialogRect.anchoredPosition = view.DialogPosition;

            var boot = Node("Boot", frame, Vector2.zero, config.View.ReferenceResolution);
            Shape("Background", boot, Vector2.zero, config.View.ReferenceResolution, Palette.Ink);
            AppIcon(boot, new Vector2(0, 86), 128);
            Text("Name", boot, new Vector2(0, -28), new Vector2(480, 60), "网易云音乐", 30, Palette.Bone);
            Text("Loading", boot, new Vector2(0, -86), new Vector2(480, 40), "正在加载…", 20, Palette.DarkRed);
            Shape("Track", boot, new Vector2(0, -142), view.BootProgressSize, Palette.DarkRed);
            var loading = Shape("Progress", boot, new Vector2(-view.BootProgressSize.x / 2, -142),
                new Vector2(0, view.BootProgressSize.y), Palette.Red).rectTransform;
            loading.pivot = new Vector2(0, .5f);

            var shell = flow.gameObject.AddComponent<AdShell>();
            shell.Configure(flow, camera, stage, skipFill.rectTransform, skipFill, skipLabel, play,
                boot.gameObject, loading, dialog, hud);
            flow.GetComponent<InputRouter>().ConfigureShell(shell, stage);
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Main.unity");
        }

        private static GameObject SaveDialogPrefab()
        {
            var root = Node("Dialog", null, Vector2.zero, view.DialogSize);
            // An opaque dialog frame alone: no translucent dimming and no fifth color.
            Shape("Background", root, Vector2.zero, view.DialogSize, Palette.Ink);
            Outline("Outline", root, Vector2.zero, view.DialogSize, Palette.DarkRed);
            var tag = Text("Tag", root, new Vector2(0, 174), new Vector2(570, 40), "天降好运", 18, Palette.Red);
            var title = Text("Title", root, new Vector2(0, 105), new Vector2(600, 64), "", view.DialogTitleFontSize, Palette.Bone);
            var body = Text("Body", root, new Vector2(0, 24), new Vector2(600, 108), "", view.DialogBodyFontSize, Palette.Bone);
            var secondary = Node("SecondaryButton", root, new Vector2(0, -38), view.DialogButtonSize);
            Shape("Fill", secondary, Vector2.zero, view.DialogButtonSize, Palette.Ink);
            Outline("Border", secondary, Vector2.zero, view.DialogButtonSize, Palette.DarkRed);
            var secondaryCopy = Text("Copy", secondary, Vector2.zero, view.DialogButtonSize, "忍痛放弃", 22, Palette.DarkRed);
            var primary = Node("PrimaryButton", root, new Vector2(0, -146), view.DialogButtonSize);
            Shape("Fill", primary, Vector2.zero, view.DialogButtonSize, Palette.Red);
            var primaryCopy = Text("Copy", primary, Vector2.zero, view.DialogButtonSize, "开一把", view.DialogButtonFontSize, Palette.Bone);
            root.gameObject.AddComponent<DialogView>().Configure(config, tag, title, body, primary, secondary, primaryCopy, secondaryCopy);
            // SaveAsPrefabAsset matches names to preserve local IDs. Give every descendant a
            // unique name, including the repeated border edges and the two button fills/copies.
            QualifyNames(root, root.name);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root.gameObject, DialogPath);
            UnityEngine.Object.DestroyImmediate(root.gameObject);
            return prefab;
        }

        private static void QualifyNames(Transform parent, string prefix)
        {
            foreach (Transform child in parent)
            {
                child.name = prefix + "_" + child.name;
                QualifyNames(child, child.name);
            }
        }

        private static void BuildCutscene(bool opening)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            LoadAssets();
            var cameraObject = new GameObject("Main Camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0, 0, -10);
            var camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 4.5f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Palette.Ink;
            camera.allowHDR = false; camera.allowMSAA = false;
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false; cameraData.antialiasing = AntialiasingMode.None;
            var root = new GameObject("CutsceneCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera;
            canvas.planeDistance = 1; canvas.referencePixelsPerUnit = 100;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            scaler.referencePixelsPerUnit = 100;
            var frame = Node("ReferenceFrame", root.transform, Vector2.zero, new Vector2(1600, 900));
            new GameObject("CutsceneController").AddComponent<CutsceneController>().Configure(opening, config, camera, frame);
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/" + (opening ? "CutsceneA" : "CutsceneB") + ".unity");
        }

        private static void BuildStatusIcons(Transform parent)
        {
            var group = Node("StatusIcons", parent, new Vector2(278, 818), new Vector2(120, 26));
            for (int i = 0; i < 4; i++)
                Shape("Signal" + i, group, new Vector2(-44 + i * 7, -7 + i * 2), new Vector2(5, 6 + i * 4), Palette.Bone);
            var wifi = Shape("WiFi", group, new Vector2(0, 0), new Vector2(20, 26), Palette.Bone, ShapeKind.Triangle);
            wifi.rectTransform.localRotation = Quaternion.Euler(0, 0, -90);
            Outline("Battery", group, new Vector2(42, 0), new Vector2(32, 16), Palette.Bone);
            Shape("BatteryFill", group, new Vector2(40, 0), new Vector2(22, 10), Palette.Bone);
            Shape("BatteryTerminal", group, new Vector2(61, 0), new Vector2(3, 6), Palette.Bone);
        }

        private static void AppIcon(Transform parent, Vector2 position, float size)
        {
            var icon = Shape("AppIcon", parent, position, Vector2.one * size, Palette.Red).rectTransform;
            // Flat note constructed in the original SVG's 24 x 24 coordinate system.
            float scale = size / 48;
            var note = Node("Note", icon, Vector2.zero, new Vector2(24, 24));
            note.localScale = Vector3.one * scale;
            Shape("LeftStem", note, new Vector2(-2.4f, 0), new Vector2(2.4f, 10.8f), Palette.Bone);
            Shape("RightStem", note, new Vector2(5.2f, 3), new Vector2(2.4f, 10.4f), Palette.Bone);
            var bar = Shape("Beam", note, new Vector2(1.4f, 5.6f), new Vector2(9.5f, 2.4f), Palette.Bone);
            bar.rectTransform.localRotation = Quaternion.Euler(0, 0, 12);
            Shape("LeftNote", note, new Vector2(-4.8f, -5.4f), Vector2.one * 4.8f, Palette.Bone, ShapeKind.Disc);
            Shape("RightNote", note, new Vector2(2.8f, -1.4f), Vector2.one * 4.8f, Palette.Bone, ShapeKind.Disc);
        }

        private static RectTransform Relocate(Transform parent, string name, Vector2 position, Vector2? size = null)
        {
            var rect = (RectTransform)parent.Find(name);
            rect.anchoredPosition = position;
            if (size.HasValue) rect.sizeDelta = size.Value;
            return rect;
        }
        private static RectTransform Node(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one / 2;
            rect.anchoredPosition = position; rect.sizeDelta = size;
            return rect;
        }
        private static FlatShape Shape(string name, Transform parent, Vector2 position, Vector2 size, Color color, ShapeKind kind = ShapeKind.Rectangle)
        {
            var graphic = Node(name, parent, position, size).gameObject.AddComponent<FlatShape>();
            graphic.Kind = kind; graphic.color = color; graphic.material = material;
            graphic.raycastTarget = false; graphic.Segments = config.View.CircleSegments;
            return graphic;
        }
        private static void Outline(string name, Transform parent, Vector2 position, Vector2 size, Color color)
        {
            var node = Node(name, parent, position, size);
            float width = view.OutlineWidth;
            Shape("Top", node, new Vector2(0, size.y / 2 - width / 2), new Vector2(size.x, width), color);
            Shape("Bottom", node, new Vector2(0, -size.y / 2 + width / 2), new Vector2(size.x, width), color);
            Shape("Left", node, new Vector2(-size.x / 2 + width / 2, 0), new Vector2(width, size.y), color);
            Shape("Right", node, new Vector2(size.x / 2 - width / 2, 0), new Vector2(width, size.y), color);
        }
        private static Text Text(string name, Transform parent, Vector2 position, Vector2 size,
            string copy, int fontSize, Color color, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var text = Node(name, parent, position, size).gameObject.AddComponent<Text>();
            text.font = FontProvider.Fallback; text.fontSize = fontSize; text.color = color;
            text.alignment = alignment; text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow; text.raycastTarget = false;
            text.material = material; text.text = copy; text.gameObject.AddComponent<FontBinding>();
            return text;
        }
    }
}
