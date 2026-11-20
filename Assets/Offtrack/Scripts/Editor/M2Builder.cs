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
        // Original side-A remains the fallback and the third level's music.
        public const string MusicPath = Root + "/Audio/side-a.wav";
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
            PrepareCutsceneScore();
            BuildCutscene(true);
            BuildCutscene(false);
            BuildMontageScene();
            BuildTitle();
            SetJourneyScenes();
            WindowsExport.ApplyExportSettings();
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(OfftrackScenes.Path(OfftrackScenes.Title));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("M2 BuildAll 完成：CutsceneA → Main → CutsceneB，Dialog prefab，所有配置保持可编辑。");
        }

        [MenuItem("Offtrack/Build V2 Main Screen")]
        public static void BuildMainScreen()
        {
            // The cutscene workstream owns its two scenes. Rebuild only Main and its shared
            // record/dialog assets, then retain the complete journey in the build list.
            M1Builder.BuildAll();
            BuildMain();
            SetJourneyScenes();
            WindowsExport.ApplyExportSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("V2 Main screen built; cutscene scenes were not regenerated.");
        }

        private static void SetJourneyScenes() => EditorBuildSettings.scenes = OfftrackScenes.Journey
            .Select(scene => new EditorBuildSettingsScene(OfftrackScenes.Path(scene), true)).ToArray();

        private static void BuildMontageScene()
        {
            BuildStory(Offtrack.Core.StoryScene.Montage, OfftrackScenes.CutsceneC);
        }

        private static void BuildTitle()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            LoadAssets();
            var root = new GameObject("TitleCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var titleCamera = BuildStoryCamera();
            var titleCanvas = root.GetComponent<Canvas>();
            titleCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            titleCanvas.worldCamera = titleCamera;
            titleCanvas.planeDistance = 1;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(780, 1688);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var backdrop = Shape("Backdrop", root.transform, Vector2.zero, Vector2.zero, Palette.Ink);
            Stretch(backdrop.rectTransform);
            var frame = Node("ReferenceFrame", root.transform, Vector2.zero, scaler.referenceResolution);
            frame.gameObject.AddComponent<RectMask2D>();
            var record = Node("TitleRecord", frame, new Vector2(390, 0), new Vector2(828, 828));
            for (int ring = 0; ring < 9; ring++)
            {
                float diameter = 828 - ring * 64;
                Shape($"Groove_{ring}", record, Vector2.zero, Vector2.one * diameter, Palette.DarkRed40, ShapeKind.Disc);
                Shape($"Vinyl_{ring}", record, Vector2.zero, Vector2.one * (diameter - 3), Palette.Ink, ShapeKind.Disc);
            }
            Shape("Label", record, Vector2.zero, new Vector2(208, 208), Palette.DarkRed, ShapeKind.Disc);
            Shape("LabelMark", record, new Vector2(-55, 20), new Vector2(42, 5), Palette.Red);
            Track(Text("Wordmark", frame, new Vector2(-200, 743), new Vector2(280, 40), "OFFTRACK", 24, Palette.Bone, TextAnchor.MiddleLeft), 5);
            var button = Shape("StartButton", frame, new Vector2(-199, -155), new Vector2(224, 66), Palette.Red);
            Text("StartLabel", button.transform, Vector2.zero, button.rectTransform.sizeDelta, "开始游戏", 27, Palette.Ink);
            root.AddComponent<TitleController>().Configure(config, button.rectTransform, record);
            EditorSceneManager.SaveScene(scene, OfftrackScenes.Path(OfftrackScenes.Title));
        }

        private static void LoadAssets(bool upgradeMainScreen = false)
        {
            config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (config == null || material == null) throw new InvalidOperationException("Run the M1 asset builder first.");
            config.ApplyV3Migration();
            view = config.ShellView;
            if (upgradeMainScreen) view.UpgradeMainScreen();
            config.Shell.Validate();
            EditorUtility.SetDirty(config);
        }

        private static void BuildMain()
        {
            var scene = EditorSceneManager.OpenScene(OfftrackScenes.Path(OfftrackScenes.Main));
            LoadAssets(upgradeMainScreen: true);
            var camera = Camera.main;
            var canvas = UnityEngine.Object.FindAnyObjectByType<Canvas>();
            canvas.name = "PhoneShell";
            var frame = (RectTransform)canvas.transform.Find("ReferenceFrame");
            var flow = UnityEngine.Object.FindAnyObjectByType<GameFlow>();
            var hud = flow.GetComponent<HudView>();
            var recordNodes = frame.Cast<Transform>().Where(item => item.name == "Disc" || item.name == "Tonearm"
                || item.name.StartsWith("Contact_", StringComparison.Ordinal)
                || item.name.StartsWith("ApproachArc_", StringComparison.Ordinal)
                || item.name.StartsWith("ContactNumber_", StringComparison.Ordinal)).ToArray();
            var stage = Node("GameStage", frame, view.StagePosition, view.StageSize);
            stage.SetAsFirstSibling();
            Shape("Plinth", stage, Vector2.zero, view.StageSize, Palette.DarkRed15);
            StretchedOutline("PlinthFrame", stage, view.StageOutlineWidth, Palette.DarkRed40);
            var record = Node("RecordGroup", stage, view.RecordOrigin, config.View.ReferenceResolution);
            record.localScale = Vector3.one * view.RecordScale;
            Shape("ArmBase", record, config.View.ArmPivot, view.ArmBaseSize, Palette.DarkRed40);
            Outline("ArmBaseFrame", record, config.View.ArmPivot, view.ArmBaseSize, Palette.DarkRed60);
            foreach (var item in recordNodes) item.SetParent(record, false);
            BuildRecordFeedback(record, flow);
            BuildTurntable(stage, flow);

            foreach (string name in new[] { "Timer", "Progress", "ProgressTrack" })
                UnityEngine.Object.DestroyImmediate(frame.Find(name).gameObject);
            // Durability cells: a lost cell keeps only this dark outline (hollow, GDD 5.3).
            var hollows = new GameObject[config.Lives];
            for (int i = 0; i < config.Lives; i++)
            {
                var cell = Relocate(frame, "Life_" + i, view.LivesPosition + Vector2.right *
                    (i - (config.Lives - 1) / 2f) * (view.LifeSize.x + view.LifeGap), view.LifeSize);
                Outline("Hollow", cell, Vector2.zero, view.LifeSize, Palette.DarkRed);
                hollows[i] = cell.Find("Hollow").gameObject;
                hollows[i].SetActive(false);
            }

            UnityEngine.Object.DestroyImmediate(frame.Find("Result").gameObject);

            Bold(Text("StatusTime", frame, new Vector2(-316, 818), new Vector2(84, 32), "9:41", 20, Palette.Bone, TextAnchor.MiddleLeft));
            BuildStatusIcons(frame);
            Outline("AdBadge", frame, new Vector2(-326, 764), new Vector2(64, 34), Palette.DarkRed40);
            Text("AdCopy", frame, new Vector2(-326, 764), new Vector2(64, 34), "广告", 20, Palette.Bone40);
            var skipFill = Shape("SkipButton", frame, view.SkipPosition, view.SkipSize, Palette.DarkRed15);
            var skipLabel = Text("SkipCopy", skipFill.transform, Vector2.zero, view.SkipSize, "跳过 8", view.SkipFontSize, Palette.Bone40);
            AppIcon(frame, new Vector2(-326, 684), 64, Palette.Red);
            Bold(Text("BrandName", frame, new Vector2(-108, 698), new Vector2(300, 46), "网易云音乐", 30, Palette.Bone, TextAnchor.MiddleLeft));
            Text("BrandSubtitle", frame, new Vector2(-108, 658), new Vector2(300, 36), "黑胶 VIP · 开屏广告", 22, Palette.Bone60, TextAnchor.MiddleLeft);

            BuildMeter(frame, hud, hollows);
            Shape("HomeIndicator", frame, view.HomeIndicatorPosition, view.HomeIndicatorSize, Palette.Bone40);

            var dimmer = Shape("DialogDimmer", frame, Vector2.zero, config.View.ReferenceResolution, Palette.DialogDim);
            var dialogPrefab = SaveDialogPrefab();
            var dialog = ((GameObject)PrefabUtility.InstantiatePrefab(dialogPrefab, frame)).GetComponent<DialogView>();
            var dialogRect = (RectTransform)dialog.transform;
            dialogRect.anchoredPosition = view.DialogPosition;

            var boot = Node("Boot", frame, Vector2.zero, config.View.ReferenceResolution);
            Shape("Background", boot, Vector2.zero, config.View.ReferenceResolution, Palette.Ink);
            AppIcon(boot, new Vector2(0, 86), 128, Palette.Red);
            Track(Text("Name", boot, new Vector2(0, -28), new Vector2(480, 60), "网易云音乐", 30, Palette.Bone), view.DisplayTracking);
            Track(Text("Loading", boot, new Vector2(0, -86), new Vector2(480, 40), "正在加载…", 22, Palette.Bone40), view.DisplayTracking / 2);
            // Same bar spec as the HUD progress: a thin deep-red track under a thicker fill.
            Shape("Track", boot, new Vector2(0, -142), new Vector2(view.BootProgressSize.x, view.OutlineWidth), Palette.DarkRed15);
            var loading = Shape("Progress", boot, new Vector2(-view.BootProgressSize.x / 2, -142),
                new Vector2(0, view.BootProgressSize.y), Palette.Red).rectTransform;
            loading.pivot = new Vector2(0, .5f);

            var shell = flow.gameObject.AddComponent<AdShell>();
            shell.Configure(flow, camera, stage, skipFill.rectTransform, skipFill, skipLabel,
                boot.gameObject, loading, dialog, hud, dimmer.gameObject);
            flow.GetComponent<InputRouter>().ConfigureShell(shell, stage);
            // Per-level music (the judge's clock) and the generated needle effects; one listener.
            if (camera.GetComponent<AudioListener>() == null) camera.gameObject.AddComponent<AudioListener>();
            ImportLevelMusic();
            flow.gameObject.AddComponent<GameAudio>().Configure(flow, ImportMusic());
            foreach (var text in frame.GetComponentsInChildren<Text>(true)) ApplyMainBaseline(text);
            EditorSceneManager.SaveScene(scene, OfftrackScenes.Path(OfftrackScenes.Main));
        }

        // Decompressed on load so playback starts and seeks sample-accurately; Vorbis keeps the
        // player small. Rendered by Tools/render-side-a.py from the composer's four loops.
        public static AudioClip ImportMusic()
            => ImportMusicAtPath(MusicPath);

        private static void ImportLevelMusic()
        {
            for (int index = 0; index < 2 && index < config.Levels.Length; index++)
            {
                string path = $"{Root}/Audio/level-{index + 1}.wav";
                var level = config.Levels[index];
                if (level != null && level.Music == null && AssetDatabase.LoadAssetAtPath<AudioClip>(path) != null)
                    level.Music = ImportMusicAtPath(path);
            }
            EditorUtility.SetDirty(config);
        }

        private static AudioClip ImportMusicAtPath(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) throw new InvalidOperationException("Missing level music at " + path);
            var sample = importer.defaultSampleSettings;
            bool changed = sample.loadType != AudioClipLoadType.DecompressOnLoad
                || sample.compressionFormat != AudioCompressionFormat.Vorbis || Math.Abs(sample.quality - .8f) > 1e-3f
                || !sample.preloadAudioData || importer.forceToMono || importer.loadInBackground;
            if (changed)
            {
                sample.loadType = AudioClipLoadType.DecompressOnLoad;
                sample.compressionFormat = AudioCompressionFormat.Vorbis;
                sample.quality = .8f;
                // Decoded with the scene, so the first needle drop never waits for it.
                sample.preloadAudioData = true;
                importer.defaultSampleSettings = sample;
                importer.forceToMono = false;
                importer.loadInBackground = false;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        private static GameObject SaveDialogPrefab()
        {
            // The prefab is saved as a titled card with one line of body: the same stack of
            // padding, title, gap, body, gap, buttons, padding that DialogView.Layout applies to
            // every card at runtime, where the body's real height and line spacing are measured.
            float padding = (view.DialogWidth - view.DialogButtonSize.x) / 2;
            float line = view.DialogLineHeight;
            float titleGap = view.DialogTitleGapLines * line;
            var size = new Vector2(view.DialogWidth, padding * 2 + view.DialogTitleHeight + titleGap + line
                + view.DialogButtonGapLines * line + view.DialogButtonSize.y);
            var root = Node("Dialog", null, Vector2.zero, size);
            Stretch(Shape("Background", root, Vector2.zero, size, Palette.Ink).rectTransform);
            StretchedOutline("Outline", root, view.DialogOutlineWidth, Palette.Bone);
            float buttonY = -size.y / 2 + padding + view.DialogButtonSize.y / 2;
            float top = size.y / 2;
            var title = Track(Text("Title", root, new Vector2(0, top - padding - view.DialogTitleHeight / 2), new Vector2(view.DialogButtonSize.x, view.DialogTitleHeight), "", view.DialogTitleFontSize, Palette.DarkRed, TextAnchor.UpperLeft), view.DialogTitleTracking);
            var body = Text("Body", root, new Vector2(0, top - padding - view.DialogTitleHeight - titleGap - line / 2), new Vector2(view.DialogButtonSize.x, line), "", view.DialogBodyFontSize, Palette.Bone, TextAnchor.UpperLeft);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            var secondary = Node("SecondaryButton", root, new Vector2(0, buttonY), view.DialogButtonSize);
            Stretch(Shape("Fill", secondary, Vector2.zero, view.DialogButtonSize, Palette.Ink).rectTransform);
            StretchedOutline("Border", secondary, view.DialogOutlineWidth, Palette.Red);
            var divider = Shape("Divider", secondary, Vector2.zero, new Vector2(view.DialogOutlineWidth, view.DialogButtonSize.y), Palette.Bone).rectTransform;
            divider.anchorMin = divider.anchorMax = new Vector2(1, .5f);
            divider.anchoredPosition = new Vector2(-view.DialogOutlineWidth / 2, 0);
            var secondaryCopy = Track(Bold(Text("Copy", secondary, Vector2.zero, view.DialogButtonSize, "忍痛放弃", view.DialogButtonFontSize, Palette.Bone)), view.DisplayTracking);
            Stretch(secondaryCopy.rectTransform);
            var primary = Node("PrimaryButton", root, new Vector2(0, buttonY), view.DialogButtonSize);
            Stretch(Shape("Fill", primary, Vector2.zero, view.DialogButtonSize, Palette.Red).rectTransform);
            var primaryCopy = Track(Bold(Text("Copy", primary, Vector2.zero, view.DialogButtonSize, "开一把", view.DialogButtonFontSize, Palette.Bone)), view.DisplayTracking);
            Stretch(primaryCopy.rectTransform);
            root.gameObject.AddComponent<DialogView>().Configure(config, title, body, primary, secondary, primaryCopy, secondaryCopy);
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

        private static void BuildRecordFeedback(RectTransform record, GameFlow flow)
        {
            var settings = flow.Config.View;
            var layer = Node("RecordFeedback", record, Vector2.zero, settings.ReferenceResolution);
            layer.SetSiblingIndex(record.Find("Tonearm").GetSiblingIndex());
            var rings = new FlatShape[flow.Config.LaneCount];
            for (int lane = 0; lane < rings.Length; lane++)
            {
                rings[lane] = Shape("LaneFeedback_" + lane, layer, settings.DiscCenter,
                    Vector2.one * settings.DiscRadius * settings.LaneRadiusRatios[lane] * 2, Palette.Bone60, ShapeKind.Ring);
                rings[lane].StrokeWidth = settings.FeedbackRingWidth;
            }
            var debris = new FlatShape[4];
            for (int index = 0; index < debris.Length; index++)
                debris[index] = Shape("ContactDebris_" + index, layer, Vector2.zero, Vector2.zero, Palette.Danger, ShapeKind.Triangle);
            layer.gameObject.AddComponent<RecordFeedbackView>().Configure(flow, rings, debris);
        }

        private static void BuildTurntable(RectTransform stage, GameFlow flow)
        {
            var side = Mono(Text("SidePrint", stage, view.StagePrintLeft, view.StagePrintSize,
                "", view.StagePrintFontSize, Palette.Bone40, TextAnchor.MiddleLeft));
            Mono(Text("FormatPrint", stage, view.StagePrintRight, view.StagePrintSize,
                "33⅓ RPM · STEREO", view.StagePrintFontSize, Palette.Bone40, TextAnchor.MiddleRight));
            var status = Text("MachineStatus", stage, view.StageStatusPosition, view.StageStatusSize,
                "等待落针", view.StageStatusFontSize, Palette.Bone60, TextAnchor.MiddleLeft);
            Mono(Text("BeatLabel", stage, view.BeatLabelPosition, view.BeatLabelSize,
                "BEAT", view.StagePrintFontSize, Palette.Bone40, TextAnchor.MiddleLeft));
            var lights = new FlatShape[4];
            for (int i = 0; i < lights.Length; i++)
                lights[i] = Shape("BeatLight_" + i, stage, view.BeatLightOrigin + Vector2.right * i * view.BeatLightPitch,
                    view.BeatLightSize, Palette.DarkRed40);
            int tickCount = Mathf.FloorToInt(view.ScaleLength / view.ScalePitch);
            for (int i = 0; i <= tickCount; i++)
            {
                float height = i % view.ScaleMajorEvery == 0 ? view.ScaleMajorTickHeight : view.ScaleTickSize.y;
                Shape("ScaleTick_" + i, stage, view.ScaleOrigin + Vector2.right * i * view.ScalePitch,
                    new Vector2(view.ScaleTickSize.x, height), Palette.DarkRed60);
            }
            flow.gameObject.AddComponent<TurntableView>().Configure(flow, status, side, lights);
        }

        [MenuItem("Offtrack/Build V2 Cutscenes")]
        public static void BuildCutscenesOnly()
        {
            PrepareCutsceneScore();
            BuildCutscene(true);
            BuildCutscene(false);
            BuildMontageScene();
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(OfftrackScenes.Path(OfftrackScenes.CutsceneA));
            Debug.Log("Cutscenes generated: A 16 s, C 6 s, B 13 s; Main assets preserved.");
        }

        private static void PrepareCutsceneScore()
        {
            LoadAssets();
            config.Cutscenes.Timing = new Offtrack.Core.CutsceneTiming();
            MeasureCutsceneFormulaWidths(config.Cutscenes.Timing);
            foreach (Offtrack.Core.StorySound sound in Enum.GetValues(typeof(Offtrack.Core.StorySound)))
            {
                string path = StoryClipPath(sound);
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (importer == null) throw new InvalidOperationException("Generate the offline cutscene audio: " + path);
                var sample = importer.defaultSampleSettings;
                sample.compressionFormat = AudioCompressionFormat.PCM;
                sample.loadType = AudioClipLoadType.DecompressOnLoad;
                importer.defaultSampleSettings = sample;
                importer.forceToMono = true;
                importer.loadInBackground = false;
                importer.SaveAndReimport();
            }
            EditorUtility.SetDirty(config);
            // Scene replacement may unload/reload the asset: persist the authored score first.
            AssetDatabase.SaveAssetIfDirty(config);
        }

        private static void MeasureCutsceneFormulaWidths(Offtrack.Core.CutsceneTiming timing)
        {
            // Widths are world units at the authored type size. Measuring a drawn frame instead
            // would bake that frame's camera zoom into the reveal masks.
            var formulas = Offtrack.Core.CutsceneTimeline.Formulas;
            if (formulas.Count != timing.FormulaWidths.Length)
                throw new InvalidOperationException("Cutscene formula metrics do not match the authored writing rows.");
            for (int index = 0; index < formulas.Count; index++)
                timing.FormulaWidths[index] = MeasureFormulaWidth(formulas[index]);
        }

        public static float MeasureFormulaWidth(string text)
        {
            var font = FontProvider.GetSerif();
            int size = Offtrack.Core.CutsceneTimeline.FormulaFontSize;
            font.RequestCharactersInTexture(text, size, FontStyle.Italic);
            float cursor = 0, right = 0;
            foreach (var character in text)
            {
                if (!font.GetCharacterInfo(character, out var glyph, size, FontStyle.Italic))
                    throw new InvalidOperationException("Missing cutscene formula glyph: " + character);
                right = Mathf.Max(right, cursor + glyph.maxX);
                cursor += glyph.advance;
            }
            return right;
        }

        private static string StoryClipPath(Offtrack.Core.StorySound sound) => Root + "/Audio/Cutscene/" + sound + ".wav";

        private static void BuildCutscene(bool opening) => BuildStory(opening ? Offtrack.Core.StoryScene.Opening : Offtrack.Core.StoryScene.Closing,
            opening ? OfftrackScenes.CutsceneA : OfftrackScenes.CutsceneB);

        private static Camera BuildStoryCamera()
        {
            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0, 0, -10);
            var camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 8.44f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Palette.Ink;
            camera.allowHDR = false; camera.allowMSAA = true;
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false; cameraData.antialiasing = AntialiasingMode.None;
            return camera;
        }

        private static void BuildStory(Offtrack.Core.StoryScene story, string sceneName)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            LoadAssets();
            var camera = BuildStoryCamera();
            var root = new GameObject("CutsceneCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera;
            canvas.planeDistance = 1; canvas.referencePixelsPerUnit = 100;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(Offtrack.Core.PortraitScore.Width, Offtrack.Core.PortraitScore.Height);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            scaler.referencePixelsPerUnit = 100;
            var frame = Node("ReferenceFrame", root.transform, Vector2.zero, scaler.referenceResolution);
            var host = new GameObject("CutsceneController");
            var sound = host.AddComponent<CutsceneSound>();
            sound.Configure(Enum.GetValues(typeof(Offtrack.Core.StorySound)).Cast<Offtrack.Core.StorySound>()
                .Select(key => AssetDatabase.LoadAssetAtPath<AudioClip>(StoryClipPath(key))).ToArray());
            host.AddComponent<CutsceneController>().ConfigureStory(story, config, camera, frame, material, sound);
            EditorSceneManager.SaveScene(scene, OfftrackScenes.Path(sceneName));
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
            var icon = Shape("AppIcon", parent, position, Vector2.one * size, fill, ShapeKind.Disc).rectTransform;
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

        // The persistent needle meter (lane, needle state, cooldown refill), the static damage line
        // and the remaining-time box. The idle call to action occupies the same row before a run.
        private static void BuildMeter(RectTransform frame, HudView hud, GameObject[] hollows)
        {
            var remaining = Text("TimerRemaining", frame, view.TimerRemainingPosition, view.TimerRemainingSize,
                "还剩 36.0 秒", view.TimerRemainingFontSize, Palette.Bone60, TextAnchor.LowerLeft);
            // Bottom-aligned so the small remaining time shares the large value's baseline.
            var meter = Node("Meter", frame, Vector2.zero, config.View.ReferenceResolution);
            var dots = new FlatShape[config.LaneCount];
            for (int i = 0; i < dots.Length; i++)
                dots[i] = Shape("LaneDot_" + i, meter, view.LaneDotOrigin + Vector2.right * i * view.LaneDotPitch,
                    view.LaneDotSize, Palette.DarkRed40);
            remaining.color = Palette.Bone;
            remaining.fontStyle = FontStyle.Bold;
            remaining.alignment = TextAnchor.MiddleRight;
            meter.gameObject.SetActive(false);
            var notice = Text("DamageNotice", frame, view.DamageNoticePosition, view.DamageNoticeSize,
                HudView.LateJumpNotice, view.DamageNoticeFontSize, Palette.Bone60, TextAnchor.MiddleLeft);
            notice.gameObject.SetActive(false);
            hud.ConfigureMeter(remaining, meter.gameObject, dots, null, null, 0,
                notice, hollows, view.DamageNoticeSeconds);
        }

        private static Text Mono(Text text)
        {
            text.GetComponent<FontBinding>().Monospace = true;
            return text;
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
            ApplyMainBaseline(text);
            return text;
        }

        private static void ApplyMainBaseline(Text text)
        {
            var baseline = text.GetComponent<MainTextBaseline>();
            if (baseline == null) baseline = text.gameObject.AddComponent<MainTextBaseline>();
            baseline.OffsetEm = view.MainTextBaselineEm;
        }
    }
}
