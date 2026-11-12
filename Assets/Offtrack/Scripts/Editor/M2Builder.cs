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
                || item.name.StartsWith("ContactPlate_", StringComparison.Ordinal)
                || item.name.StartsWith("ContactNumber_", StringComparison.Ordinal)).ToArray();
            var stage = Node("GameStage", frame, view.StagePosition, view.StageSize);
            stage.SetAsFirstSibling();
            var record = Node("RecordGroup", stage, view.RecordOrigin, config.View.ReferenceResolution);
            record.localScale = Vector3.one * view.RecordScale;
            foreach (var item in recordNodes) item.SetParent(record, false);

            Relocate(frame, "Timer", view.TimerPosition, view.TimerSize);
            var timer = frame.Find("Timer").GetComponent<Text>();
            timer.fontSize = view.TimerFontSize;
            // The countdown is the one number players read mid-run: bone and bold, like the prototype.
            timer.color = Palette.Bone; timer.fontStyle = FontStyle.Bold;
            // Right-aligned on the shell's 32 px right margin (x = +358), like the skip button.
            timer.alignment = TextAnchor.MiddleRight;
            Relocate(frame, "ProgressTrack", view.ProgressPosition);
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
            // GDD 八: the start prompt is exactly 「点我开始！！」; bold like every other action label.
            hint.text = "点我开始！！"; hint.fontSize = view.HintFontSize; hint.color = Palette.Red; hint.fontStyle = FontStyle.Bold;
            hint.alignment = TextAnchor.MiddleLeft; // starts 20 px right of the play square
            BuildInstruction(frame.Find("Idle"));
            // Result block: a red tracked tag over a bone line, the same pattern as the lottery dialog.
            var resultTitle = Relocate(frame, "Result/ResultTitle", view.CompleteTitlePosition).GetComponent<Text>();
            resultTitle.color = Palette.Red; resultTitle.fontSize = 22;
            Track(resultTitle, view.DisplayTracking * .75f);
            Relocate(frame, "Result/ResultBody", view.CompleteBodyPosition).GetComponent<Text>().color = Palette.Bone;
            frame.Find("Result/ReplayButton").gameObject.SetActive(false);

            Bold(Text("StatusTime", frame, new Vector2(-316, 818), new Vector2(84, 40), "9:41", 24, Palette.Bone, TextAnchor.MiddleLeft));
            BuildStatusIcons(frame);
            Shape("AdBadge", frame, new Vector2(-326, 764), new Vector2(64, 34), Palette.DarkRed);
            Text("AdCopy", frame, new Vector2(-326, 764), new Vector2(64, 34), "广告", 20, Palette.Bone);
            var skipFill = Shape("SkipButton", frame, view.SkipPosition, view.SkipSize, Palette.DarkRed);
            var skipLabel = Bold(Text("SkipCopy", skipFill.transform, Vector2.zero, view.SkipSize, "跳过 8", view.SkipFontSize, Palette.Bone));
            // Deep red in the header so each shell frame keeps one main-red focus (CTA, skip or scratches);
            // the boot screen, where the brand is the subject, keeps the red icon.
            AppIcon(frame, new Vector2(-318, 680), 80, Palette.DarkRed);
            Bold(Text("BrandName", frame, new Vector2(-108, 698), new Vector2(300, 46), "网易云音乐", 30, Palette.Bone, TextAnchor.MiddleLeft));
            // Bone, not deep red: deep red on ink is 2.6:1 and unreadable at window size. Hierarchy comes
            // from 30 bold vs 22 regular. Deep-red text is kept only where the GDD asks for dim (忍痛放弃).
            Text("BrandSubtitle", frame, new Vector2(-108, 658), new Vector2(300, 36), "黑胶 VIP · 开屏广告", 22, Palette.Bone, TextAnchor.MiddleLeft);

            var dialogPrefab = SaveDialogPrefab();
            var dialog = ((GameObject)PrefabUtility.InstantiatePrefab(dialogPrefab, frame)).GetComponent<DialogView>();
            var dialogRect = (RectTransform)dialog.transform;
            dialogRect.anchoredPosition = view.DialogPosition;

            var boot = Node("Boot", frame, Vector2.zero, config.View.ReferenceResolution);
            Shape("Background", boot, Vector2.zero, config.View.ReferenceResolution, Palette.Ink);
            AppIcon(boot, new Vector2(0, 86), 128, Palette.Red);
            Track(Bold(Text("Name", boot, new Vector2(0, -28), new Vector2(480, 60), "网易云音乐", 30, Palette.Bone)), view.DisplayTracking);
            Track(Text("Loading", boot, new Vector2(0, -86), new Vector2(480, 40), "正在加载…", 22, Palette.Bone), view.DisplayTracking / 2);
            // Same bar spec as the HUD progress: a thin deep-red track under a thicker fill.
            Shape("Track", boot, new Vector2(0, -142), new Vector2(view.BootProgressSize.x, view.OutlineWidth), Palette.DarkRed);
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
            // Opaque ink over the record band (not stretched with the panel), then the panel itself.
            // No translucent dimming and no fifth color.
            Shape("Backdrop", root, view.DialogBackdropPosition - view.DialogPosition, view.DialogBackdropSize, Palette.Ink);
            // DialogView resizes the panel per dialog, so the fill and the frame stretch with it.
            Stretch(Shape("Background", root, Vector2.zero, view.DialogSize, Palette.Ink).rectTransform);
            StretchedOutline("Outline", root, view.DialogOutlineWidth, Palette.DarkRed);
            var tag = Track(Text("Tag", root, new Vector2(0, 174), new Vector2(570, 40), "天降好运", 20, Palette.Red), view.DisplayTracking);
            var title = Bold(Text("Title", root, new Vector2(0, 105), new Vector2(600, 64), "", view.DialogTitleFontSize, Palette.Bone));
            var body = Text("Body", root, new Vector2(0, 24), new Vector2(600, 108), "", view.DialogBodyFontSize, Palette.Bone);
            body.lineSpacing = 1.25f;
            var secondary = Node("SecondaryButton", root, new Vector2(0, -38), view.DialogButtonSize);
            Shape("Fill", secondary, Vector2.zero, view.DialogButtonSize, Palette.Ink);
            Outline("Border", secondary, Vector2.zero, view.DialogButtonSize, Palette.DarkRed);
            // Deliberately dim (GDD 六: 「长得像背景」), but large enough to be found.
            var secondaryCopy = Track(Text("Copy", secondary, Vector2.zero, view.DialogButtonSize, "忍痛放弃", 24, Palette.DarkRed), view.DisplayTracking / 2);
            var primary = Node("PrimaryButton", root, new Vector2(0, -146), view.DialogButtonSize);
            Shape("Fill", primary, Vector2.zero, view.DialogButtonSize, Palette.Red);
            var primaryCopy = Track(Bold(Text("Copy", primary, Vector2.zero, view.DialogButtonSize, "开一把", view.DialogButtonFontSize, Palette.Bone)), view.DisplayTracking);
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
            camera.allowHDR = false; camera.allowMSAA = true;
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
            // Group right edge (battery terminal) lands on the +358 right margin.
            var group = Node("StatusIcons", parent, new Vector2(296, 818), new Vector2(120, 26));
            for (int i = 0; i < 4; i++)
                Shape("Signal" + i, group, new Vector2(-46 + i * 8, -7 + i * 2), new Vector2(6, 6 + i * 4), Palette.Bone);
            // Wi-Fi fan: two 90-degree arcs over a dot, all from one centre near the baseline.
            var wifiCentre = new Vector2(0, -8);
            foreach (var radius in new[] { 14f, 8.5f })
            {
                var arc = Shape("WiFiArc" + radius.ToString("0", System.Globalization.CultureInfo.InvariantCulture), group,
                    wifiCentre, Vector2.one * radius * 2, Palette.Bone, ShapeKind.Arc);
                arc.StrokeWidth = 3.5f; arc.ArcStart = 45; arc.ArcSweep = 90;
            }
            Shape("WiFiDot", group, wifiCentre + new Vector2(0, 1.5f), Vector2.one * 5, Palette.Bone, ShapeKind.Disc);
            Outline("Battery", group, new Vector2(42, 0), new Vector2(32, 16), Palette.Bone);
            Shape("BatteryFill", group, new Vector2(40, 0), new Vector2(22, 10), Palette.Bone);
            Shape("BatteryTerminal", group, new Vector2(61, 0), new Vector2(3, 6), Palette.Bone);
        }

        private static void AppIcon(Transform parent, Vector2 position, float size, Color fill)
        {
            var icon = Shape("AppIcon", parent, position, Vector2.one * size, fill).rectTransform;
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

        // How to play, shown with the idle call to action: two drawn keycaps (the arrow glyphs are
        // hairlines in the system font) and the sentence. Not in GDD 八; taken from the prototype.
        private static void BuildInstruction(Transform idle)
        {
            var row = view.InstructionPosition;
            var textLeft = row.x - view.InstructionSize.x / 2;
            for (int i = 0; i < 2; i++)
            {
                var key = Node("Key" + (i == 0 ? "Left" : "Right"), idle, new Vector2(textLeft - 60 + i * 36, row.y), new Vector2(28, 28));
                Outline("Cap", key, Vector2.zero, new Vector2(28, 28), Palette.Bone);
                var arrow = Shape("Arrow", key, Vector2.zero, new Vector2(10, 12), Palette.Bone, ShapeKind.Triangle);
                if (i == 0) arrow.rectTransform.localRotation = Quaternion.Euler(0, 0, 180);
            }
            Text("Instruction", idle, row, view.InstructionSize, "或点击左右两侧，在相邻音轨间跳跃。",
                view.InstructionFontSize, Palette.Bone, TextAnchor.MiddleLeft);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.anchoredPosition = Vector2.zero; rect.sizeDelta = Vector2.zero;
        }

        // A frame whose four edges are anchored to the parent's sides, so it follows any panel size.
        private static void StretchedOutline(string name, Transform parent, float width, Color color)
        {
            var node = Node(name, parent, Vector2.zero, Vector2.zero);
            Stretch(node);
            void Edge(string edge, Vector2 min, Vector2 max, Vector2 pivot, Vector2 size)
            {
                var rect = Shape(edge, node, Vector2.zero, size, color).rectTransform;
                rect.anchorMin = min; rect.anchorMax = max; rect.pivot = pivot;
                rect.anchoredPosition = Vector2.zero; rect.sizeDelta = size;
            }
            Edge("Top", new Vector2(0, 1), Vector2.one, new Vector2(.5f, 1), new Vector2(0, width));
            Edge("Bottom", Vector2.zero, new Vector2(1, 0), new Vector2(.5f, 0), new Vector2(0, width));
            Edge("Left", Vector2.zero, new Vector2(0, 1), new Vector2(0, .5f), new Vector2(width, 0));
            Edge("Right", new Vector2(1, 0), Vector2.one, new Vector2(1, .5f), new Vector2(width, 0));
        }

        private static Text Bold(Text text)
        {
            text.fontStyle = FontStyle.Bold;
            return text;
        }

        private static Text Track(Text text, float spacing)
        {
            text.gameObject.AddComponent<LetterSpacing>().Spacing = spacing;
            return text;
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
