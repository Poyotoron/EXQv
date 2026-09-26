using System.Collections.Generic;
using System.Reflection;
using Maaaaa.EXQv;
using QvPen.UdonScript;
using QvPen.Udon.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.UI;
using VRC.SDKBase;

namespace Maaaaa.EXQv.Editor
{
    [CustomEditor(typeof(GrabQvManager))]
    public class GrabQvManagerEditor : UnityEditor.Editor
    {
        private SerializedProperty targetedPens;

        private void OnEnable()
        {
            targetedPens = serializedObject.FindProperty("targetedPens");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            EditorGUILayout.Space();
            if (GUILayout.Button(GrabQvStrings.AddScenePens))
                GrabQvPenPickerWindow.Open((GrabQvManager)target);
            bool changed = serializedObject.ApplyModifiedProperties();

            GrabQvManager manager = (GrabQvManager)target;
            changed |= manager.RefreshTargetReferences();
            changed |= manager.RefreshHandleAutoHold();
            changed |= GrabQvEraseButtonEditorUtility.RefreshExistingButtons(manager);
            if (changed)
                GrabQvButtonEditorUtility.CopyProxyToUdon(manager);
            int bodyManagerCount = GrabQvBodyLinkEditorUtility.Refresh(manager);
            DrawPenWarnings(manager);
            DrawBodyQvInfo(manager, bodyManagerCount);
            DrawButtonWarnings(manager);
            DrawQvPenVersionWarning();
        }

        private static void DrawPenWarnings(GrabQvManager manager)
        {
            QvPen_PenManager[] pens = manager.TargetedPens;
            if (pens == null || pens.Length == 0)
            {
                EditorGUILayout.HelpBox(GrabQvStrings.MissingPens, MessageType.Warning);
                return;
            }

            bool hasNull = false;
            bool missingLateSync = false;
            for (int i = 0; i < pens.Length; i++)
            {
                if (pens[i] == null)
                    hasNull = true;
                if (manager.TargetLateSyncs == null || i >= manager.TargetLateSyncs.Length || manager.TargetLateSyncs[i] == null)
                    missingLateSync = true;
            }
            if (hasNull)
                EditorGUILayout.HelpBox(GrabQvStrings.NullPen, MessageType.Warning);
            if (missingLateSync)
                EditorGUILayout.HelpBox(GrabQvStrings.MissingLateSync, MessageType.Warning);
        }

        private static void DrawBodyQvInfo(GrabQvManager manager, int bodyManagerCount)
        {
            if (bodyManagerCount > 1)
            {
                EditorGUILayout.HelpBox(GrabQvStrings.MultipleBodyQvManagers, MessageType.Warning);
                return;
            }
            BodyQvManager bodyManager = manager.BodyQvManager;
            if (bodyManager == null)
                return;
            QvPen_PenManager[] pens = manager.TargetedPens;
            for (int i = 0; pens != null && i < pens.Length; i++)
            {
                if (pens[i] == null)
                    continue;
                QvPen_PenManager[] bodyPens = bodyManager.TargetedPens;
                for (int j = 0; bodyPens != null && j < bodyPens.Length; j++)
                {
                    if (bodyPens[j] == pens[i])
                        EditorGUILayout.HelpBox(GrabQvStrings.BodyQvCombined + "\n" + pens[i].name,
                            MessageType.Info);
                }
            }
        }

        private static void DrawButtonWarnings(GrabQvManager manager)
        {
            bool missing = GrabQvButtonEditorUtility.HasMissingButtons(manager);
            bool extra = GrabQvButtonEditorUtility.HasExtraButtons(manager);
            bool missingErase = GrabQvEraseButtonEditorUtility.HasMissingButtons(manager);
            bool extraErase = GrabQvEraseButtonEditorUtility.HasExtraButtons(manager);
            if (missing)
            {
                EditorGUILayout.HelpBox(GrabQvStrings.MissingSplitButtons, MessageType.Warning);
                if (GUILayout.Button(GrabQvStrings.CreateSplitButtons))
                    GrabQvButtonEditorUtility.CreateMissingButtons(manager);
            }
            if (extra)
            {
                EditorGUILayout.HelpBox(GrabQvStrings.ExtraSplitButtons, MessageType.Warning);
                if (GUILayout.Button(GrabQvStrings.RemoveExtraSplitButtons))
                    GrabQvButtonEditorUtility.RemoveExtraButtons(manager);
            }
            if (missingErase)
            {
                EditorGUILayout.HelpBox(GrabQvStrings.MissingEraseButtons, MessageType.Warning);
                if (GUILayout.Button(GrabQvStrings.ReplaceEraseButtons))
                    GrabQvEraseButtonEditorUtility.CreateMissingButtons(manager);
            }
            if (extraErase)
            {
                EditorGUILayout.HelpBox(GrabQvStrings.ExtraEraseButtons, MessageType.Warning);
                if (GUILayout.Button(GrabQvStrings.RestoreEraseButtons))
                    GrabQvEraseButtonEditorUtility.RemoveExtraButtons(manager);
            }
        }

        private static void DrawQvPenVersionWarning()
        {
            UnityEditor.PackageManager.PackageInfo package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/net.ureishi.qvpen/package.json");
            string version = package == null ? "不明" : package.version;
            if (version != "3.3.15")
                EditorGUILayout.HelpBox(string.Format(GrabQvStrings.QvPenVersion, version), MessageType.Warning);
        }
    }

    internal static class GrabQvEraseButtonEditorUtility
    {
        public static bool HasMissingButtons(GrabQvManager manager)
        {
            QvPen_PenManager[] pens = manager.TargetedPens;
            for (int i = 0; pens != null && i < pens.Length; i++)
            {
                if (pens[i] == null)
                    continue;
                GrabQvEraseButton[] buttons = pens[i].GetComponentsInChildren<GrabQvEraseButton>(true);
                if (!HasButton(buttons, manager, pens[i]))
                    return true;
            }
            return false;
        }

        public static bool HasExtraButtons(GrabQvManager manager)
        {
            GrabQvEraseButton[] buttons = GetSceneButtons(manager);
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i].Manager == null)
                    return true;
                if (buttons[i].Manager != manager)
                    continue;
                if (!Contains(manager.TargetedPens, buttons[i].Pen) || IsDuplicate(buttons, i))
                    return true;
            }
            return false;
        }

        public static void CreateMissingButtons(GrabQvManager manager)
        {
            QvPen_PenManager[] pens = manager.TargetedPens;
            for (int i = 0; pens != null && i < pens.Length; i++)
            {
                GrabQvEraseButton[] buttons = pens[i] == null ? null :
                    pens[i].GetComponentsInChildren<GrabQvEraseButton>(true);
                if (pens[i] == null || HasButton(buttons, manager, pens[i]))
                    continue;
                ReplaceButton(manager, pens[i]);
            }
        }

        public static void RemoveExtraButtons(GrabQvManager manager)
        {
            GrabQvEraseButton[] buttons = GetSceneButtons(manager);
            for (int i = buttons.Length - 1; i >= 0; i--)
            {
                if (buttons[i].Manager != null && buttons[i].Manager != manager)
                    continue;
                if (buttons[i].Manager == manager && Contains(manager.TargetedPens, buttons[i].Pen) &&
                    !IsDuplicate(buttons, i))
                    continue;
                RestoreButton(buttons[i]);
            }
        }

        public static bool RefreshExistingButtons(GrabQvManager manager)
        {
            bool changed = false;
            QvPen_PenManager[] pens = manager.TargetedPens;
            for (int i = 0; pens != null && i < pens.Length; i++)
            {
                if (pens[i] == null)
                    continue;
                GrabQvEraseButton[] buttons = pens[i].GetComponentsInChildren<GrabQvEraseButton>(true);
                for (int j = 0; j < buttons.Length; j++)
                {
                    if (buttons[j].Manager != manager)
                        continue;
                    Text text = buttons[j].SplitTextObject == null ? null :
                        buttons[j].SplitTextObject.GetComponent<Text>();
                    changed |= ConfigureSplitText(text, true);
                }
            }
            return changed;
        }

        private static void ReplaceButton(GrabQvManager manager, QvPen_PenManager pen)
        {
            QvPen_ClearButton original = pen.GetComponentInChildren<QvPen_ClearButton>(true);
            if (original == null)
            {
                Debug.LogWarning(GrabQvStrings.MissingEraseReferences, manager);
                return;
            }
            SerializedObject source = new SerializedObject(original);
            Image ownIndicator = GetImage(source, "ownIndicator");
            Image allIndicator = GetImage(source, "allIndicator");
            Image ownText = GetImage(source, "ownTextImage");
            Image allText = GetImage(source, "allTextImage");
            if (ownIndicator == null || allIndicator == null || ownText == null || allText == null)
            {
                Debug.LogWarning(GrabQvStrings.MissingEraseReferences, original);
                return;
            }

            Undo.RecordObject(original, GrabQvStrings.ReplaceEraseButtons);
            original.enabled = false;
            Component originalUdon = GetBackingUdon(original);
            if (originalUdon != null)
            {
                Undo.RecordObject(originalUdon, GrabQvStrings.ReplaceEraseButtons);
                ((Behaviour)originalUdon).enabled = false;
            }

            RectTransform ownUi = ownIndicator.transform.parent as RectTransform;
            RectTransform allUi = allIndicator.transform.parent as RectTransform;
            if (ownUi == null || allUi == null || ownUi.parent != allUi.parent)
            {
                Debug.LogWarning(GrabQvStrings.MissingEraseReferences, original);
                original.enabled = true;
                if (originalUdon != null) ((Behaviour)originalUdon).enabled = true;
                return;
            }
            GameObject splitUi = Object.Instantiate(ownUi.gameObject, ownUi.parent);
            splitUi.name = GrabQvStrings.EraseSplitUiName;
            Undo.RegisterCreatedObjectUndo(splitUi, GrabQvStrings.ReplaceEraseButtons);
            RectTransform splitUiRect = (RectTransform)splitUi.transform;
            splitUiRect.anchoredPosition = ownUi.anchoredPosition +
                (ownUi.anchoredPosition - allUi.anchoredPosition);
            Image splitIndicator = splitUi.transform.Find(ownIndicator.name).GetComponent<Image>();
            splitIndicator.name = GrabQvStrings.EraseSplitIndicatorName;
            splitIndicator.color = new Color(1f, 0.69f, 0.376f, 1f);
            splitIndicator.fillAmount = 0f;

            Image[] clonedImages = splitUi.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < clonedImages.Length; i++)
            {
                if (clonedImages[i] != splitIndicator)
                    Object.DestroyImmediate(clonedImages[i].gameObject);
            }
            GameObject textObject = new GameObject(GrabQvStrings.EraseSplitTextName, typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Text));
            textObject.transform.SetParent(splitUi.transform, false);
            Text splitText = textObject.GetComponent<Text>();
            ConfigureSplitText(splitText, false);

            GrabQvEraseButton replacement = AddEraseComponent(original.gameObject);
            if (replacement == null)
            {
                Undo.DestroyObjectImmediate(splitUi);
                original.enabled = true;
                if (originalUdon != null) ((Behaviour)originalUdon).enabled = true;
                return;
            }
            SerializedObject serialized = new SerializedObject(replacement);
            serialized.FindProperty("manager").objectReferenceValue = manager;
            serialized.FindProperty("penManager").objectReferenceValue = pen;
            serialized.FindProperty("splitUi").objectReferenceValue = splitUi;
            serialized.FindProperty("splitIndicator").objectReferenceValue = splitIndicator;
            serialized.FindProperty("ownIndicator").objectReferenceValue = ownIndicator;
            serialized.FindProperty("allIndicator").objectReferenceValue = allIndicator;
            serialized.FindProperty("splitText").objectReferenceValue = splitText;
            serialized.FindProperty("ownTextImage").objectReferenceValue = ownText;
            serialized.FindProperty("allTextImage").objectReferenceValue = allText;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            GrabQvButtonEditorUtility.CopyProxyToUdon(replacement);

            Component replacementUdon = GetBackingUdon(replacement);
            if (originalUdon != null && replacementUdon != null)
            {
                Undo.RecordObject(replacementUdon, GrabQvStrings.ReplaceEraseButtons);
                SerializedObject originalSerialized = new SerializedObject(originalUdon);
                SerializedObject replacementSerialized = new SerializedObject(replacementUdon);
                SerializedProperty oldText = originalSerialized.FindProperty("interactText");
                SerializedProperty newText = replacementSerialized.FindProperty("interactText");
                SerializedProperty oldProximity = originalSerialized.FindProperty("proximity");
                SerializedProperty newProximity = replacementSerialized.FindProperty("proximity");
                if (oldText != null && newText != null) newText.stringValue = oldText.stringValue;
                if (oldProximity != null && newProximity != null) newProximity.floatValue = oldProximity.floatValue;
                replacementSerialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void RestoreButton(GrabQvEraseButton replacement)
        {
            GameObject clearObject = replacement.gameObject;
            GameObject splitUi = replacement.SplitUi;
            Component replacementUdon = GetBackingUdon(replacement);
            QvPen_ClearButton original = replacement.GetComponent<QvPen_ClearButton>();
            if (original != null)
            {
                Undo.RecordObject(original, GrabQvStrings.RestoreEraseButtons);
                original.enabled = true;
                Component originalUdon = GetBackingUdon(original);
                if (originalUdon != null)
                {
                    Undo.RecordObject(originalUdon, GrabQvStrings.RestoreEraseButtons);
                    ((Behaviour)originalUdon).enabled = true;
                }
            }
            Undo.DestroyObjectImmediate(replacement);
            if (replacementUdon != null) Undo.DestroyObjectImmediate(replacementUdon);
            if (splitUi != null) Undo.DestroyObjectImmediate(splitUi);
            GrabQvEraseButton[] remaining = clearObject.GetComponents<GrabQvEraseButton>();
            if (remaining.Length > 0 && original != null)
            {
                original.enabled = false;
                Component originalUdon = GetBackingUdon(original);
                if (originalUdon != null) ((Behaviour)originalUdon).enabled = false;
            }
        }

        private static bool ConfigureSplitText(Text text, bool recordUndo)
        {
            if (text == null)
                return false;
            RectTransform rect = text.rectTransform;
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Vector2 topLeft = new Vector2(0f, 1f);
            Vector2 size = new Vector2(160f, 56f);
            Vector2 position = new Vector2(5f, -4f);
            Vector3 scale = new Vector3(0.25f, 0.25f, 1f);
            bool changed = text.text != GrabQvStrings.EraseSplitText || text.font != font ||
                text.fontSize != 48 || text.color != Color.white ||
                text.alignment != TextAnchor.UpperLeft || text.raycastTarget ||
                text.horizontalOverflow != HorizontalWrapMode.Overflow ||
                text.verticalOverflow != VerticalWrapMode.Overflow ||
                rect.anchorMin != topLeft || rect.anchorMax != topLeft || rect.pivot != topLeft ||
                rect.sizeDelta != size || rect.anchoredPosition != position || rect.localScale != scale;
            if (!changed)
                return false;
            if (recordUndo)
            {
                Undo.RecordObject(text, GrabQvStrings.ReplaceEraseButtons);
                Undo.RecordObject(rect, GrabQvStrings.ReplaceEraseButtons);
            }
            text.text = GrabQvStrings.EraseSplitText;
            text.font = font;
            text.fontSize = 48;
            text.color = Color.white;
            text.alignment = TextAnchor.UpperLeft;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            rect.anchorMin = topLeft;
            rect.anchorMax = topLeft;
            rect.pivot = topLeft;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            rect.localScale = scale;
            EditorUtility.SetDirty(text);
            EditorUtility.SetDirty(rect);
            return true;
        }

        private static Image GetImage(SerializedObject source, string name)
        {
            SerializedProperty property = source.FindProperty(name);
            return property == null ? null : property.objectReferenceValue as Image;
        }

        private static GrabQvEraseButton AddEraseComponent(GameObject target)
        {
            System.Type type = System.Type.GetType("UdonSharpEditor.UdonSharpComponentExtensions, UdonSharp.Editor");
            MethodInfo method = type == null ? null : type.GetMethod("AddUdonSharpComponent",
                BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(GameObject), typeof(System.Type) }, null);
            return method == null ? null : method.Invoke(null,
                new object[] { target, typeof(GrabQvEraseButton) }) as GrabQvEraseButton;
        }

        private static Component GetBackingUdon(UdonSharp.UdonSharpBehaviour behaviour)
        {
            System.Type type = System.Type.GetType("UdonSharpEditor.UdonSharpEditorUtility, UdonSharp.Editor");
            MethodInfo method = type == null ? null : type.GetMethod("GetBackingUdonBehaviour",
                BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(UdonSharp.UdonSharpBehaviour) }, null);
            return method == null ? null : method.Invoke(null, new object[] { behaviour }) as Component;
        }

        private static GrabQvEraseButton[] GetSceneButtons(GrabQvManager manager)
        {
            List<GrabQvEraseButton> buttons = new List<GrabQvEraseButton>();
            GameObject[] roots = manager.gameObject.scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                buttons.AddRange(roots[i].GetComponentsInChildren<GrabQvEraseButton>(true));
            return buttons.ToArray();
        }

        private static bool IsDuplicate(GrabQvEraseButton[] buttons, int index)
        {
            GrabQvEraseButton button = buttons[index];
            if (button == null || button.Manager == null)
                return false;
            for (int i = 0; i < buttons.Length; i++)
            {
                if (i == index || buttons[i] == null || buttons[i].gameObject != button.gameObject)
                    continue;
                if (buttons[i].Manager == null)
                    continue;
                if (buttons[i].Manager != button.Manager ||
                    (i < index && buttons[i].Pen == button.Pen))
                    return true;
            }
            return false;
        }

        private static bool HasButton(GrabQvEraseButton[] buttons, GrabQvManager manager,
            QvPen_PenManager pen)
        {
            for (int i = 0; buttons != null && i < buttons.Length; i++)
                if (buttons[i].Manager == manager && buttons[i].Pen == pen) return true;
            return false;
        }

        private static bool Contains(QvPen_PenManager[] pens, QvPen_PenManager pen)
        {
            for (int i = 0; pens != null && i < pens.Length; i++)
                if (pens[i] == pen) return true;
            return false;
        }
    }

    internal static class GrabQvButtonEditorUtility
    {
        public static bool HasMissingButtons(GrabQvManager manager)
        {
            QvPen_PenManager[] pens = manager.TargetedPens;
            GrabQvSplitButton[] buttons = manager.GetComponentsInChildren<GrabQvSplitButton>(true);
            for (int i = 0; pens != null && i < pens.Length; i++)
            {
                if (pens[i] != null && !HasButton(buttons, pens[i]))
                    return true;
            }
            return false;
        }

        public static bool HasExtraButtons(GrabQvManager manager)
        {
            QvPen_PenManager[] pens = manager.TargetedPens;
            GrabQvSplitButton[] buttons = manager.GetComponentsInChildren<GrabQvSplitButton>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i].Pen == null || !Contains(pens, buttons[i].Pen))
                    return true;
            }
            return false;
        }

        public static void CreateMissingButtons(GrabQvManager manager)
        {
            Transform root = manager.transform.Find("SplitButtons");
            if (root == null)
            {
                GameObject rootObject = new GameObject("SplitButtons");
                Undo.RegisterCreatedObjectUndo(rootObject, GrabQvStrings.CreateSplitButtons);
                rootObject.transform.SetParent(manager.transform, false);
                root = rootObject.transform;
            }

            QvPen_PenManager[] pens = manager.TargetedPens;
            GrabQvSplitButton[] buttons = manager.GetComponentsInChildren<GrabQvSplitButton>(true);
            for (int i = 0; pens != null && i < pens.Length; i++)
            {
                if (pens[i] == null || HasButton(buttons, pens[i]))
                    continue;
                CreateButton(manager, pens[i], root, i);
            }
            EditorUtility.SetDirty(manager);
        }

        public static void RemoveExtraButtons(GrabQvManager manager)
        {
            QvPen_PenManager[] pens = manager.TargetedPens;
            GrabQvSplitButton[] buttons = manager.GetComponentsInChildren<GrabQvSplitButton>(true);
            for (int i = buttons.Length - 1; i >= 0; i--)
            {
                if (buttons[i].Pen == null || !Contains(pens, buttons[i].Pen))
                    Undo.DestroyObjectImmediate(buttons[i].gameObject);
            }
        }

        private static void CreateButton(GrabQvManager manager, QvPen_PenManager pen, Transform parent, int index)
        {
            VRC_Pickup pickup = pen.GetComponentInChildren<VRC_Pickup>(true);
            if (pickup == null)
                return;

            GameObject button = new GameObject("Split (" + index + ")", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(button, GrabQvStrings.CreateSplitButtons);
            button.transform.SetParent(parent, false);
            button.transform.localScale = Vector3.one * 0.05f;
            button.transform.localRotation = Quaternion.identity;
            BoxCollider collider = button.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(1f, 1f, 0.02f);

            PositionConstraint position = button.AddComponent<PositionConstraint>();
            position.AddSource(new ConstraintSource { sourceTransform = pickup.transform, weight = 1f });
            position.translationOffset = new Vector3(0f, -0.31f, 0f);
            position.locked = true;
            position.constraintActive = true;
            RotationConstraint rotation = button.AddComponent<RotationConstraint>();
            rotation.AddSource(new ConstraintSource { sourceTransform = pickup.transform, weight = 1f });
            rotation.rotationAxis = Axis.Y;
            rotation.rotationAtRest = Vector3.zero;
            rotation.rotationOffset = Vector3.zero;
            rotation.locked = true;
            rotation.constraintActive = true;

            GameObject canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasRenderer));
            canvasObject.transform.SetParent(button.transform, false);
            RectTransform canvasRect = (RectTransform)canvasObject.transform;
            canvasRect.sizeDelta = new Vector2(100f, 100f);
            canvasRect.localScale = Vector3.one * 0.01f;
            canvasRect.localPosition = new Vector3(0f, 0f, -0.011f);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            Image frame = canvasObject.AddComponent<Image>();
            frame.color = new Color(1f, 0.69f, 0.376f, 1f);

            GameObject backgroundObject = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            backgroundObject.transform.SetParent(canvasObject.transform, false);
            RectTransform backgroundRect = (RectTransform)backgroundObject.transform;
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = new Vector2(5f, 5f);
            backgroundRect.offsetMax = new Vector2(-5f, -5f);
            Image background = backgroundObject.GetComponent<Image>();
            background.color = Color.white;

            GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textObject.transform.SetParent(backgroundObject.transform, false);
            RectTransform textRect = (RectTransform)textObject.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            Text text = textObject.GetComponent<Text>();
            text.text = GrabQvStrings.SplitButtonText;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 24;
            text.color = Color.black;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;

            GrabQvSplitButton split = AddSplitComponent(button);
            if (split == null)
                return;
            SerializedObject serialized = new SerializedObject(split);
            serialized.FindProperty("manager").objectReferenceValue = manager;
            serialized.FindProperty("pen").objectReferenceValue = pen;
            serialized.FindProperty("background").objectReferenceValue = background;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            CopyProxyToUdon(split);
        }

        private static GrabQvSplitButton AddSplitComponent(GameObject target)
        {
            System.Type type = System.Type.GetType("UdonSharpEditor.UdonSharpComponentExtensions, UdonSharp.Editor");
            MethodInfo method = type == null ? null : type.GetMethod("AddUdonSharpComponent",
                BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(GameObject), typeof(System.Type) }, null);
            return method == null ? null : method.Invoke(null,
                new object[] { target, typeof(GrabQvSplitButton) }) as GrabQvSplitButton;
        }

        internal static void CopyProxyToUdon(UdonSharp.UdonSharpBehaviour behaviour)
        {
            System.Type type = System.Type.GetType("UdonSharpEditor.UdonSharpEditorUtility, UdonSharp.Editor");
            MethodInfo method = type == null ? null : type.GetMethod("CopyProxyToUdon",
                BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(UdonSharp.UdonSharpBehaviour) }, null);
            if (method != null)
                method.Invoke(null, new object[] { behaviour });
        }

        private static bool HasButton(GrabQvSplitButton[] buttons, QvPen_PenManager pen)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i].Pen == pen)
                    return true;
            }
            return false;
        }

        private static bool Contains(QvPen_PenManager[] pens, QvPen_PenManager pen)
        {
            for (int i = 0; pens != null && i < pens.Length; i++)
            {
                if (pens[i] == pen)
                    return true;
            }
            return false;
        }
    }

    internal static class GrabQvBodyLinkEditorUtility
    {
        public static int Refresh(GrabQvManager manager)
        {
            BodyQvManager[] managers = Resources.FindObjectsOfTypeAll<BodyQvManager>();
            BodyQvManager found = null;
            int count = 0;
            for (int i = 0; i < managers.Length; i++)
            {
                BodyQvManager candidate = managers[i];
                if (candidate == null || candidate.gameObject.scene != manager.gameObject.scene ||
                    !SharesPen(manager.TargetedPens, candidate.TargetedPens))
                    continue;
                found = candidate;
                count++;
            }
            BodyQvManager value = count == 1 ? found : null;
            if (manager.BodyQvManager == value)
                return count;

            Undo.RecordObject(manager, "BodyQv とのつなぎ込みを更新");
            SerializedObject serialized = new SerializedObject(manager);
            serialized.FindProperty("bodyQvManager").objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(manager);
            GrabQvButtonEditorUtility.CopyProxyToUdon(manager);
            return count;
        }

        private static bool SharesPen(QvPen_PenManager[] first, QvPen_PenManager[] second)
        {
            for (int i = 0; first != null && i < first.Length; i++)
            {
                for (int j = 0; second != null && j < second.Length; j++)
                {
                    if (first[i] != null && first[i] == second[j])
                        return true;
                }
            }
            return false;
        }
    }

    internal class GrabQvPenPickerWindow : EditorWindow
    {
        private GrabQvManager manager;
        private QvPen_PenManager[] scenePens;
        private bool[] selected;
        private Vector2 scroll;

        public static void Open(GrabQvManager targetManager)
        {
            GrabQvPenPickerWindow window = CreateInstance<GrabQvPenPickerWindow>();
            window.titleContent = new GUIContent(GrabQvStrings.PickerTitle);
            window.manager = targetManager;
            window.LoadScenePens();
            window.minSize = new Vector2(380f, 220f);
            window.ShowUtility();
        }

        private void LoadScenePens()
        {
            QvPen_PenManager[] found = Object.FindObjectsOfType<QvPen_PenManager>(true);
            List<QvPen_PenManager> valid = new List<QvPen_PenManager>();
            for (int i = 0; i < found.Length; i++)
                if (found[i].gameObject.scene.IsValid()) valid.Add(found[i]);
            scenePens = valid.ToArray();
            selected = new bool[scenePens.Length];
        }

        private void OnGUI()
        {
            if (manager == null) { Close(); return; }
            if (scenePens == null || scenePens.Length == 0)
            {
                EditorGUILayout.HelpBox(GrabQvStrings.PickerEmpty, MessageType.Info);
                return;
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (int i = 0; i < scenePens.Length; i++)
                selected[i] = EditorGUILayout.ToggleLeft(GetHierarchyPath(scenePens[i].transform), selected[i]);
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button(GrabQvStrings.AddSelected)) { AddSelectedPens(); Close(); }
        }

        private void AddSelectedPens()
        {
            SerializedObject serializedManager = new SerializedObject(manager);
            SerializedProperty pens = serializedManager.FindProperty("targetedPens");
            for (int i = 0; i < scenePens.Length; i++)
            {
                if (!selected[i] || Contains(pens, scenePens[i])) continue;
                int index = pens.arraySize;
                pens.InsertArrayElementAtIndex(index);
                pens.GetArrayElementAtIndex(index).objectReferenceValue = scenePens[i];
            }
            bool changed = serializedManager.ApplyModifiedProperties();
            changed |= manager.RefreshTargetReferences();
            if (changed)
            {
                GrabQvButtonEditorUtility.CopyProxyToUdon(manager);
                EditorUtility.SetDirty(manager);
            }
        }

        private static bool Contains(SerializedProperty array, Object value)
        {
            for (int i = 0; i < array.arraySize; i++)
                if (array.GetArrayElementAtIndex(i).objectReferenceValue == value) return true;
            return false;
        }

        private static string GetHierarchyPath(Transform target)
        {
            string path = target.name;
            while (target.parent != null) { target = target.parent; path = target.name + "/" + path; }
            return path;
        }
    }
}
