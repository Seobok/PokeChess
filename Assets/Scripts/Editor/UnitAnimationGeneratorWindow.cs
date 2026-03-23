using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using PokeChess.Autobattler;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace PokeChess.Editor
{
    public sealed class UnitAnimationGeneratorWindow : EditorWindow
    {
        private const string TextureRootPath = "Assets/Textures/Unit";
        private const string OutputRootPath = "Assets/Animation/GeneratedUnits";
        private const string PrefabRootPath = "Assets/Prefabs/Unit";
        private const float ClipFrameRate = 60f;
        private const string GeneratedEventFunctionName = "OnGeneratedAnimationEvent";

        private static readonly DirectionInfo[] RuntimeDirections =
        {
            new("D", 0, UnitAnimationDirection.Down),
            new("LD", 7, UnitAnimationDirection.LeftDown),
            new("L", 6, UnitAnimationDirection.Left),
            new("LU", 5, UnitAnimationDirection.LeftUp),
            new("U", 4, UnitAnimationDirection.Up),
            new("RU", 3, UnitAnimationDirection.RightUp),
            new("R", 2, UnitAnimationDirection.Right),
            new("RD", 1, UnitAnimationDirection.RightDown)
        };

        private static readonly HashSet<string> SupportedAnimationNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Attack", "Charge", "Hurt", "Idle", "Shoot", "Strike", "Walk"
        };

        private static readonly string[] AttackCandidates =
        {
            "Attack", "Strike"
        };

        private static readonly string[] SkillCandidates =
        {
            "Shoot", "Charge", "Attack", "Strike"
        };

        private static readonly string[] HitCandidates =
        {
            "Hurt"
        };

        private Vector2 _scroll;
        [SerializeField] private bool assignControllerToPrefab = true;
        [SerializeField] private bool attachAnimationEventRelay = true;
        [SerializeField] private bool overwriteExistingAssets = true;
        [SerializeField] private bool verboseLogging;

        [MenuItem("Tools/PokeChess/Animation Generator")]
        private static void OpenWindow()
        {
            GetWindow<UnitAnimationGeneratorWindow>("Unit Animations");
        }

        [MenuItem("Tools/PokeChess/Generate All Unit Animations")]
        private static void GenerateAllFromMenu()
        {
            GenerateAllUnits(true, true, true, false);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Unit Animation Generator", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "AnimData.xml and *-Anim.png files are converted into directional AnimationClips, an AnimatorController, and optionally assigned to the matching unit prefab.",
                MessageType.Info);

            using var scroll = new EditorGUILayout.ScrollViewScope(_scroll);
            _scroll = scroll.scrollPosition;

            assignControllerToPrefab = EditorGUILayout.ToggleLeft("Assign generated controller to matching prefab", assignControllerToPrefab);
            attachAnimationEventRelay = EditorGUILayout.ToggleLeft("Ensure UnitAnimationEventRelay exists on Animator GameObject", attachAnimationEventRelay);
            overwriteExistingAssets = EditorGUILayout.ToggleLeft("Overwrite existing generated assets", overwriteExistingAssets);
            verboseLogging = EditorGUILayout.ToggleLeft("Verbose logging", verboseLogging);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Textures", TextureRootPath);
            EditorGUILayout.LabelField("Output", OutputRootPath);
            EditorGUILayout.LabelField("Prefabs", PrefabRootPath);

            EditorGUILayout.Space(12f);

            if (GUILayout.Button("Generate All Units", GUILayout.Height(32f)))
            {
                GenerateAllUnits(assignControllerToPrefab, attachAnimationEventRelay, overwriteExistingAssets, verboseLogging);
            }

            if (GUILayout.Button("Generate Selected Unit Folders", GUILayout.Height(28f)))
            {
                GenerateSelectedUnits(assignControllerToPrefab, attachAnimationEventRelay, overwriteExistingAssets, verboseLogging);
            }
        }

        private static void GenerateAllUnits(bool assignPrefab, bool attachRelay, bool overwrite, bool verbose)
        {
            string[] unitFolderGuids = AssetDatabase.FindAssets(string.Empty, new[] { TextureRootPath });
            var unitFolders = new HashSet<string>();
            foreach (string guid in unitFolderGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path))
                {
                    string parent = Path.GetDirectoryName(path)?.Replace("\\", "/");
                    if (string.Equals(parent, TextureRootPath, StringComparison.OrdinalIgnoreCase))
                        unitFolders.Add(path);
                }
            }

            GenerateUnitFolders(unitFolders.OrderBy(path => path, StringComparer.OrdinalIgnoreCase), assignPrefab, attachRelay, overwrite, verbose);
        }

        private static void GenerateSelectedUnits(bool assignPrefab, bool attachRelay, bool overwrite, bool verbose)
        {
            var selectedFolders = new List<string>();
            foreach (UnityEngine.Object selected in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(selected);
                if (!AssetDatabase.IsValidFolder(path))
                    continue;

                string parent = Path.GetDirectoryName(path)?.Replace("\\", "/");
                if (string.Equals(parent, TextureRootPath, StringComparison.OrdinalIgnoreCase))
                    selectedFolders.Add(path);
            }

            if (selectedFolders.Count == 0)
            {
                EditorUtility.DisplayDialog("Unit Animation Generator", "Select one or more folders directly under Assets/Textures/Unit first.", "OK");
                return;
            }

            GenerateUnitFolders(selectedFolders, assignPrefab, attachRelay, overwrite, verbose);
        }

        private static void GenerateUnitFolders(IEnumerable<string> unitFolders, bool assignPrefab, bool attachRelay, bool overwrite, bool verbose)
        {
            EnsureFolder(OutputRootPath);

            int successCount = 0;
            int failureCount = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string unitFolder in unitFolders)
                {
                    try
                    {
                        GenerateUnit(unitFolder, assignPrefab, attachRelay, overwrite, verbose);
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        failureCount++;
                        Debug.LogError($"[UnitAnimationGenerator] Failed to generate '{unitFolder}'.\n{ex}");
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            EditorUtility.DisplayDialog(
                "Unit Animation Generator",
                $"Generation complete.\nSuccess: {successCount}\nFailed: {failureCount}",
                "OK");
        }

        private static void GenerateUnit(string unitFolderPath, bool assignPrefab, bool attachRelay, bool overwrite, bool verbose)
        {
            string unitName = Path.GetFileName(unitFolderPath);
            string animDataPath = $"{unitFolderPath}/AnimData.xml";
            if (!File.Exists(animDataPath))
                throw new FileNotFoundException($"AnimData.xml not found for {unitName}.", animDataPath);

            var unitDefinition = ParseAnimData(unitName, animDataPath);
            var generatedClips = GenerateClips(unitDefinition, unitFolderPath, overwrite, verbose);
            if (generatedClips.Count == 0)
            {
                if (verbose)
                    Debug.LogWarning($"[UnitAnimationGenerator] {unitName} had no supported directional animations.");
                return;
            }

            AnimatorController controller = GenerateController(unitDefinition, unitName, generatedClips, overwrite, verbose);

            if (assignPrefab)
                AssignControllerToPrefab(unitName, controller, attachRelay, verbose);

            if (verbose)
                Debug.Log($"[UnitAnimationGenerator] Generated assets for {unitName}");
        }

        private static UnitAnimDefinition ParseAnimData(string unitName, string animDataPath)
        {
            XDocument document = XDocument.Load(animDataPath);
            XElement root = document.Root ?? throw new InvalidDataException($"AnimData root missing in {animDataPath}");
            XElement animsElement = root.Element("Anims") ?? throw new InvalidDataException($"Anims node missing in {animDataPath}");

            var definitions = new Dictionary<string, AnimClipDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (XElement animElement in animsElement.Elements("Anim"))
            {
                string name = ReadRequiredString(animElement, "Name");
                if (!SupportedAnimationNames.Contains(name))
                    continue;

                int index = ReadInt(animElement, "Index", -1);
                string copyOf = ReadOptionalString(animElement, "CopyOf");

                var durations = animElement.Element("Durations")?
                    .Elements("Duration")
                    .Select(duration => int.Parse(duration.Value, CultureInfo.InvariantCulture))
                    .ToList() ?? new List<int>();

                definitions[name] = new AnimClipDefinition
                {
                    Name = name,
                    Index = index,
                    CopyOf = copyOf,
                    FrameWidth = ReadInt(animElement, "FrameWidth", 0),
                    FrameHeight = ReadInt(animElement, "FrameHeight", 0),
                    RushFrame = ReadNullableInt(animElement, "RushFrame"),
                    HitFrame = ReadNullableInt(animElement, "HitFrame"),
                    ReturnFrame = ReadNullableInt(animElement, "ReturnFrame"),
                    Durations = durations
                };
            }

            return new UnitAnimDefinition(unitName, definitions);
        }

        private static Dictionary<string, Dictionary<string, AnimationClip>> GenerateClips(
            UnitAnimDefinition unitDefinition,
            string unitFolderPath,
            bool overwrite,
            bool verbose)
        {
            string unitOutputFolder = $"{OutputRootPath}/{unitDefinition.UnitName}";
            string clipOutputFolder = $"{unitOutputFolder}/Clips";
            EnsureFolder(unitOutputFolder);
            EnsureFolder(clipOutputFolder);

            var generated = new Dictionary<string, Dictionary<string, AnimationClip>>(StringComparer.OrdinalIgnoreCase);

            foreach (AnimClipDefinition definition in unitDefinition.Definitions.Values.OrderBy(item => item.Index))
            {
                AnimClipDefinition resolved = unitDefinition.Resolve(definition.Name);
                if (resolved.Durations.Count == 0)
                {
                    if (verbose)
                        Debug.LogWarning($"[UnitAnimationGenerator] {unitDefinition.UnitName}/{definition.Name} has no durations and was skipped.");
                    continue;
                }

                string textureAnimName = string.IsNullOrWhiteSpace(definition.CopyOf) ? definition.Name : resolved.Name;
                string texturePath = $"{unitFolderPath}/{textureAnimName}-Anim.png";
                Sprite[] sprites = LoadSprites(texturePath);
                if (sprites.Length == 0)
                {
                    if (verbose)
                        Debug.LogWarning($"[UnitAnimationGenerator] No sprites found for {unitDefinition.UnitName}/{definition.Name} at {texturePath}");
                    continue;
                }

                int framesPerDirection = resolved.Durations.Count;
                int expectedSpriteCount = framesPerDirection * RuntimeDirections.Length;
                if (sprites.Length < expectedSpriteCount)
                    throw new InvalidDataException($"{unitDefinition.UnitName}/{definition.Name} expected at least {expectedSpriteCount} sprites but found {sprites.Length}.");

                var clipBySuffix = new Dictionary<string, AnimationClip>(StringComparer.OrdinalIgnoreCase);
                foreach (DirectionInfo direction in RuntimeDirections)
                {
                    Sprite[] directionalSprites = new Sprite[framesPerDirection];
                    int startIndex = direction.SheetDirectionIndex * framesPerDirection;
                    Array.Copy(sprites, startIndex, directionalSprites, 0, framesPerDirection);

                    string clipPath = $"{clipOutputFolder}/{definition.Name}_{direction.Suffix}.anim";
                    if (overwrite && File.Exists(clipPath))
                        AssetDatabase.DeleteAsset(clipPath);

                    AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                    if (clip == null)
                    {
                        clip = new AnimationClip
                        {
                            frameRate = ClipFrameRate,
                            name = $"{definition.Name}_{direction.Suffix}"
                        };
                        AssetDatabase.CreateAsset(clip, clipPath);
                    }

                    BuildClip(clip, directionalSprites, resolved, IsLoopingAnimation(definition.Name));
                    EditorUtility.SetDirty(clip);
                    clipBySuffix[direction.Suffix] = clip;
                }

                generated[definition.Name] = clipBySuffix;
            }

            return generated;
        }

        private static AnimatorController GenerateController(
            UnitAnimDefinition unitDefinition,
            string unitName,
            Dictionary<string, Dictionary<string, AnimationClip>> generatedClips,
            bool overwrite,
            bool verbose)
        {
            string controllerPath = $"{OutputRootPath}/{unitName}/{unitName}.controller";
            if (overwrite && File.Exists(controllerPath))
                AssetDatabase.DeleteAsset(controllerPath);

            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

            controller.parameters = Array.Empty<AnimatorControllerParameter>();
            controller.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Skill", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("IsDead", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Direction", AnimatorControllerParameterType.Int);

            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            ClearStateMachine(stateMachine);

            string attackSource = ChooseFirstAvailable(generatedClips, AttackCandidates) ?? "Attack";
            string skillSource = ChooseFirstAvailable(generatedClips, SkillCandidates) ?? attackSource;
            string hitSource = ChooseFirstAvailable(generatedClips, HitCandidates) ?? "Hurt";
            string deadSource = hitSource;
            string idleSource = generatedClips.ContainsKey("Idle") ? "Idle" : ChooseFirstAvailable(generatedClips, "Walk") ?? attackSource;
            string walkSource = generatedClips.ContainsKey("Walk") ? "Walk" : idleSource;

            var states = new Dictionary<string, AnimatorState>(StringComparer.OrdinalIgnoreCase);
            foreach (DirectionInfo direction in RuntimeDirections)
            {
                AnimationClip idleClip = ResolveDirectionalClip(generatedClips, idleSource, direction.Suffix)
                    ?? ResolveDirectionalClip(generatedClips, walkSource, direction.Suffix);
                AnimationClip walkClip = ResolveDirectionalClip(generatedClips, walkSource, direction.Suffix)
                    ?? idleClip;
                AnimationClip attackClip = ResolveDirectionalClip(generatedClips, attackSource, direction.Suffix)
                    ?? walkClip;
                AnimationClip skillClip = ResolveDirectionalClip(generatedClips, skillSource, direction.Suffix)
                    ?? attackClip;
                AnimationClip hitClip = ResolveDirectionalClip(generatedClips, hitSource, direction.Suffix)
                    ?? idleClip;
                AnimationClip deadClip = ResolveDirectionalClip(generatedClips, deadSource, direction.Suffix)
                    ?? hitClip;

                states[$"Idle_{direction.Suffix}"] = AddState(stateMachine, $"Idle_{direction.Suffix}", idleClip, new Vector3(-500f, direction.RuntimeDirection * 70f, 0f));
                states[$"Walk_{direction.Suffix}"] = AddState(stateMachine, $"Walk_{direction.Suffix}", walkClip, new Vector3(-250f, direction.RuntimeDirection * 70f, 0f));
                states[$"Attack_{direction.Suffix}"] = AddState(stateMachine, $"Attack_{direction.Suffix}", attackClip, new Vector3(50f, direction.RuntimeDirection * 70f, 0f));
                states[$"Skill_{direction.Suffix}"] = AddState(stateMachine, $"Skill_{direction.Suffix}", skillClip, new Vector3(300f, direction.RuntimeDirection * 70f, 0f));
                states[$"Hit_{direction.Suffix}"] = AddState(stateMachine, $"Hit_{direction.Suffix}", hitClip, new Vector3(550f, direction.RuntimeDirection * 70f, 0f));
                states[$"Dead_{direction.Suffix}"] = AddState(stateMachine, $"Dead_{direction.Suffix}", deadClip, new Vector3(800f, direction.RuntimeDirection * 70f, 0f), true);
            }

            stateMachine.defaultState = states["Idle_D"];

            foreach (DirectionInfo fromDirection in RuntimeDirections)
            {
                foreach (DirectionInfo toDirection in RuntimeDirections)
                {
                    AddLocomotionTransition(states[$"Idle_{fromDirection.Suffix}"], states[$"Idle_{toDirection.Suffix}"], false, toDirection.RuntimeDirection);
                    AddLocomotionTransition(states[$"Idle_{fromDirection.Suffix}"], states[$"Walk_{toDirection.Suffix}"], true, toDirection.RuntimeDirection);
                    AddLocomotionTransition(states[$"Walk_{fromDirection.Suffix}"], states[$"Idle_{toDirection.Suffix}"], false, toDirection.RuntimeDirection);
                    AddLocomotionTransition(states[$"Walk_{fromDirection.Suffix}"], states[$"Walk_{toDirection.Suffix}"], true, toDirection.RuntimeDirection);
                }
            }

            foreach (DirectionInfo direction in RuntimeDirections)
            {
                AddActionTransition(stateMachine, states[$"Attack_{direction.Suffix}"], "Attack", direction.RuntimeDirection);
                AddActionTransition(stateMachine, states[$"Skill_{direction.Suffix}"], "Skill", direction.RuntimeDirection);
                AddActionTransition(stateMachine, states[$"Hit_{direction.Suffix}"], "Hit", direction.RuntimeDirection);
                AddDeadTransition(stateMachine, states[$"Dead_{direction.Suffix}"], direction.RuntimeDirection);

                AddActionExitTransitions(states[$"Attack_{direction.Suffix}"], states);
                AddActionExitTransitions(states[$"Skill_{direction.Suffix}"], states);
                AddActionExitTransitions(states[$"Hit_{direction.Suffix}"], states);
            }

            if (verbose)
            {
                Debug.Log(
                    $"[UnitAnimationGenerator] {unitName} controller sources: Idle={idleSource}, Walk={walkSource}, Attack={attackSource}, Skill={skillSource}, Hit={hitSource}, Dead={deadSource}");
            }

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void AssignControllerToPrefab(string unitName, RuntimeAnimatorController controller, bool attachRelay, bool verbose)
        {
            string prefabPath = $"{PrefabRootPath}/{unitName}.prefab";
            if (!File.Exists(prefabPath))
            {
                Debug.LogWarning($"[UnitAnimationGenerator] Matching prefab not found for {unitName}: {prefabPath}");
                return;
            }

            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Animator animator = prefabRoot.GetComponentInChildren<Animator>(true);
                if (animator == null)
                    throw new MissingComponentException($"Animator not found in prefab {prefabPath}");

                animator.runtimeAnimatorController = controller;
                if (attachRelay)
                {
                    if (animator.gameObject.GetComponent<UnitAnimationEventRelay>() == null)
                        animator.gameObject.AddComponent<UnitAnimationEventRelay>();
                }

                UnitController unitController = prefabRoot.GetComponent<UnitController>();
                if (unitController != null)
                {
                    SerializedObject serializedController = new SerializedObject(unitController);
                    serializedController.FindProperty("animator").objectReferenceValue = animator;
                    serializedController.ApplyModifiedPropertiesWithoutUndo();
                }

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);

                if (verbose)
                    Debug.Log($"[UnitAnimationGenerator] Assigned controller to prefab {prefabPath}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static void BuildClip(AnimationClip clip, IReadOnlyList<Sprite> sprites, AnimClipDefinition definition, bool loop)
        {
            var binding = new EditorCurveBinding
            {
                type = typeof(SpriteRenderer),
                path = string.Empty,
                propertyName = "m_Sprite"
            };

            var keyframes = new ObjectReferenceKeyframe[sprites.Count];
            float time = 0f;
            for (int i = 0; i < sprites.Count; i++)
            {
                keyframes[i] = new ObjectReferenceKeyframe
                {
                    time = time,
                    value = sprites[i]
                };

                int duration = Mathf.Max(1, definition.Durations[i]);
                time += duration / ClipFrameRate;
            }

            AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);
            AnimationUtility.SetAnimationEvents(clip, BuildAnimationEvents(definition));
            SetLoop(clip, loop);
        }

        private static AnimationEvent[] BuildAnimationEvents(AnimClipDefinition definition)
        {
            var events = new List<AnimationEvent>();
            AddClipEvent(events, definition.RushFrame, definition.Durations, UnitAnimationClipEventType.RushFrame);
            AddClipEvent(events, definition.HitFrame, definition.Durations, UnitAnimationClipEventType.HitFrame);
            AddClipEvent(events, definition.ReturnFrame, definition.Durations, UnitAnimationClipEventType.ReturnFrame);
            return events.ToArray();
        }

        private static void AddClipEvent(
            List<AnimationEvent> events,
            int? frameIndex,
            IReadOnlyList<int> durations,
            UnitAnimationClipEventType eventType)
        {
            if (!frameIndex.HasValue || durations.Count == 0)
                return;

            int clampedIndex = Mathf.Clamp(frameIndex.Value, 0, durations.Count - 1);
            float time = 0f;
            for (int i = 0; i < clampedIndex; i++)
            {
                time += Mathf.Max(1, durations[i]) / ClipFrameRate;
            }

            events.Add(new AnimationEvent
            {
                functionName = GeneratedEventFunctionName,
                stringParameter = eventType.ToString(),
                time = time
            });
        }

        private static void SetLoop(AnimationClip clip, bool loop)
        {
            var serializedClip = new SerializedObject(clip);
            SerializedProperty settings = serializedClip.FindProperty("m_AnimationClipSettings");
            if (settings != null)
            {
                settings.FindPropertyRelative("m_LoopTime").boolValue = loop;
                settings.FindPropertyRelative("m_LoopBlend").boolValue = false;
                serializedClip.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static bool IsLoopingAnimation(string animationName)
        {
            return string.Equals(animationName, "Idle", StringComparison.OrdinalIgnoreCase)
                || string.Equals(animationName, "Walk", StringComparison.OrdinalIgnoreCase)
                || string.Equals(animationName, "Charge", StringComparison.OrdinalIgnoreCase);
        }

        private static string ChooseFirstAvailable(Dictionary<string, Dictionary<string, AnimationClip>> clips, params string[] candidates)
        {
            foreach (string candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate) && clips.ContainsKey(candidate))
                    return candidate;
            }

            return clips.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        }

        private static AnimationClip ResolveDirectionalClip(
            Dictionary<string, Dictionary<string, AnimationClip>> clips,
            string animationName,
            string suffix)
        {
            if (string.IsNullOrWhiteSpace(animationName))
                return null;

            return clips.TryGetValue(animationName, out Dictionary<string, AnimationClip> bySuffix)
                && bySuffix.TryGetValue(suffix, out AnimationClip clip)
                ? clip
                : null;
        }

        private static AnimatorState AddState(
            AnimatorStateMachine stateMachine,
            string stateName,
            Motion motion,
            Vector3 position,
            bool isDead = false)
        {
            AnimatorState state = stateMachine.AddState(stateName, position);
            state.motion = motion;
            state.writeDefaultValues = false;
            state.speed = 1f;
            if (isDead)
                state.tag = "Dead";
            return state;
        }

        private static void AddLocomotionTransition(AnimatorState from, AnimatorState to, bool isMoving, int direction)
        {
            if (from == to)
                return;

            AnimatorStateTransition transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0.05f;
            transition.canTransitionToSelf = false;
            transition.AddCondition(isMoving ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, "IsMoving");
            transition.AddCondition(AnimatorConditionMode.Equals, direction, "Direction");
        }

        private static void AddActionTransition(AnimatorStateMachine stateMachine, AnimatorState toState, string triggerName, int direction)
        {
            AnimatorStateTransition transition = stateMachine.AddAnyStateTransition(toState);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0.03f;
            transition.canTransitionToSelf = false;
            transition.AddCondition(AnimatorConditionMode.If, 0f, triggerName);
            transition.AddCondition(AnimatorConditionMode.Equals, direction, "Direction");
            transition.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsDead");
        }

        private static void AddDeadTransition(AnimatorStateMachine stateMachine, AnimatorState toState, int direction)
        {
            AnimatorStateTransition transition = stateMachine.AddAnyStateTransition(toState);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0.02f;
            transition.canTransitionToSelf = false;
            transition.AddCondition(AnimatorConditionMode.If, 0f, "IsDead");
            transition.AddCondition(AnimatorConditionMode.Equals, direction, "Direction");
        }

        private static void AddActionExitTransitions(AnimatorState fromState, IReadOnlyDictionary<string, AnimatorState> states)
        {
            foreach (DirectionInfo targetDirection in RuntimeDirections)
            {
                AddExitTransition(fromState, states[$"Idle_{targetDirection.Suffix}"], false, targetDirection.RuntimeDirection);
                AddExitTransition(fromState, states[$"Walk_{targetDirection.Suffix}"], true, targetDirection.RuntimeDirection);
            }
        }

        private static void AddExitTransition(AnimatorState fromState, AnimatorState toState, bool isMoving, int direction)
        {
            AnimatorStateTransition transition = fromState.AddTransition(toState);
            transition.hasExitTime = true;
            transition.exitTime = 1f;
            transition.hasFixedDuration = true;
            transition.duration = 0.03f;
            transition.canTransitionToSelf = false;
            transition.AddCondition(isMoving ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, "IsMoving");
            transition.AddCondition(AnimatorConditionMode.Equals, direction, "Direction");
            transition.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsDead");
        }

        private static void ClearStateMachine(AnimatorStateMachine stateMachine)
        {
            foreach (ChildAnimatorState child in stateMachine.states.ToArray())
                stateMachine.RemoveState(child.state);

            foreach (ChildAnimatorStateMachine childStateMachine in stateMachine.stateMachines.ToArray())
                stateMachine.RemoveStateMachine(childStateMachine.stateMachine);

            foreach (AnimatorTransition transition in stateMachine.entryTransitions.ToArray())
                stateMachine.RemoveEntryTransition(transition);

            foreach (AnimatorStateTransition transition in stateMachine.anyStateTransitions.ToArray())
                stateMachine.RemoveAnyStateTransition(transition);
        }

        private static Sprite[] LoadSprites(string assetPath)
        {
            UnityEngine.Object[] loaded = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            return loaded
                .OfType<Sprite>()
                .OrderBy(sprite => ParseSpriteIndex(sprite.name))
                .ToArray();
        }

        private static int ParseSpriteIndex(string spriteName)
        {
            int underscoreIndex = spriteName.LastIndexOf('_');
            if (underscoreIndex < 0 || underscoreIndex >= spriteName.Length - 1)
                return int.MaxValue;

            string indexToken = spriteName.Substring(underscoreIndex + 1);
            return int.TryParse(indexToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : int.MaxValue;
        }

        private static string ReadRequiredString(XElement element, string childName)
        {
            return element.Element(childName)?.Value
                ?? throw new InvalidDataException($"{childName} is missing in {element}");
        }

        private static string ReadOptionalString(XElement element, string childName)
        {
            return element.Element(childName)?.Value;
        }

        private static int ReadInt(XElement element, string childName, int fallback)
        {
            string raw = element.Element(childName)?.Value;
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : fallback;
        }

        private static int? ReadNullableInt(XElement element, string childName)
        {
            string raw = element.Element(childName)?.Value;
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : null;
        }

        private static void EnsureFolder(string assetFolderPath)
        {
            string[] segments = assetFolderPath.Split('/');
            string current = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string next = $"{current}/{segments[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, segments[i]);

                current = next;
            }
        }

        private readonly struct DirectionInfo
        {
            public DirectionInfo(string suffix, int sheetDirectionIndex, UnitAnimationDirection runtimeDirection)
            {
                Suffix = suffix;
                SheetDirectionIndex = sheetDirectionIndex;
                RuntimeDirection = (int)runtimeDirection;
            }

            public string Suffix { get; }
            public int SheetDirectionIndex { get; }
            public int RuntimeDirection { get; }
        }

        private sealed class UnitAnimDefinition
        {
            public UnitAnimDefinition(string unitName, Dictionary<string, AnimClipDefinition> definitions)
            {
                UnitName = unitName;
                Definitions = definitions;
            }

            public string UnitName { get; }
            public Dictionary<string, AnimClipDefinition> Definitions { get; }

            public AnimClipDefinition Resolve(string name)
            {
                if (!Definitions.TryGetValue(name, out AnimClipDefinition definition))
                    throw new KeyNotFoundException($"Animation '{name}' is missing.");

                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while (!string.IsNullOrWhiteSpace(definition.CopyOf))
                {
                    if (!visited.Add(definition.Name))
                        throw new InvalidDataException($"Circular CopyOf chain detected in {UnitName}: {definition.Name}");

                    if (!Definitions.TryGetValue(definition.CopyOf, out definition))
                        throw new KeyNotFoundException($"CopyOf animation '{definition.CopyOf}' is missing in {UnitName}.");
                }

                return definition;
            }
        }

        private sealed class AnimClipDefinition
        {
            public string Name { get; set; }
            public int Index { get; set; }
            public string CopyOf { get; set; }
            public int FrameWidth { get; set; }
            public int FrameHeight { get; set; }
            public int? RushFrame { get; set; }
            public int? HitFrame { get; set; }
            public int? ReturnFrame { get; set; }
            public List<int> Durations { get; set; } = new List<int>();
        }
    }
}
