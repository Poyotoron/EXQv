using System.Collections.Generic;
using Maaaaa.EXQv;
using QvPen.UdonScript;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;

namespace Maaaaa.EXQv.Editor
{
    [CustomEditor(typeof(BodyQvManager))]
    public class BodyQvManagerEditor : UnityEditor.Editor
    {
        private SerializedProperty targetedPens;
        private SerializedProperty enableBodyColliders;
        private SerializedProperty bodyColliderLayer;

        private void OnEnable()
        {
            targetedPens = serializedObject.FindProperty("targetedPens");
            enableBodyColliders = serializedObject.FindProperty("enableBodyColliders");
            bodyColliderLayer = serializedObject.FindProperty("bodyColliderLayer");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();

            EditorGUILayout.Space();
            if (GUILayout.Button(BodyQvStrings.AddScenePens))
                BodyQvPenPickerWindow.Open((BodyQvManager)target);

            serializedObject.ApplyModifiedProperties();
            ((BodyQvManager)target).RefreshTargetReferences();

            DrawPenWarnings();
            DrawQvPenVersionWarning();

            if (!enableBodyColliders.boolValue)
                return;

            int layer = bodyColliderLayer.intValue;
            if (layer < 22 || layer > 31)
            {
                EditorGUILayout.HelpBox(BodyQvStrings.InvalidLayer, MessageType.Warning);
                return;
            }

            DrawCollisionWarnings(layer);
            DrawSurftraceWarnings(layer);
        }

        private void DrawPenWarnings()
        {
            BodyQvManager manager = (BodyQvManager)target;
            QvPen_PenManager[] pens = manager.TargetedPens;
            if (pens == null || pens.Length == 0)
            {
                EditorGUILayout.HelpBox(BodyQvStrings.MissingPens, MessageType.Warning);
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
                EditorGUILayout.HelpBox(BodyQvStrings.NullPen, MessageType.Warning);
            if (missingLateSync)
                EditorGUILayout.HelpBox(BodyQvStrings.MissingLateSync, MessageType.Warning);
        }

        private void DrawQvPenVersionWarning()
        {
            UnityEditor.PackageManager.PackageInfo package =
                UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/net.ureishi.qvpen/package.json");
            string version = package == null ? "不明" : package.version;
            if (version != "3.3.15")
                EditorGUILayout.HelpBox(string.Format(BodyQvStrings.QvPenVersion, version), MessageType.Warning);
        }

        private void DrawCollisionWarnings(int layer)
        {
            const int playerLayer = 9;
            const int playerLocalLayer = 10;
            bool collidesWithPlayer = !Physics.GetIgnoreLayerCollision(layer, playerLayer) ||
                                      !Physics.GetIgnoreLayerCollision(layer, playerLocalLayer);
            if (collidesWithPlayer)
            {
                EditorGUILayout.HelpBox(BodyQvStrings.PlayerCollision, MessageType.Warning);
                if (GUILayout.Button(BodyQvStrings.FixPlayerCollision) &&
                    EditorUtility.DisplayDialog(BodyQvStrings.ConfirmTitle,
                        string.Format(BodyQvStrings.ConfirmPlayerCollision, layer),
                        BodyQvStrings.Apply, BodyQvStrings.Cancel))
                {
                    Physics.IgnoreLayerCollision(layer, playerLayer, true);
                    Physics.IgnoreLayerCollision(layer, playerLocalLayer, true);
                }
            }

            List<int> pickupLayers = GetTargetPickupLayers();
            bool missingPickupCollision = false;
            for (int i = 0; i < pickupLayers.Count; i++)
            {
                if (Physics.GetIgnoreLayerCollision(layer, pickupLayers[i]))
                {
                    missingPickupCollision = true;
                    break;
                }
            }

            if (!missingPickupCollision)
                return;

            EditorGUILayout.HelpBox(BodyQvStrings.PickupCollision, MessageType.Warning);
            if (GUILayout.Button(BodyQvStrings.FixPickupCollision) &&
                EditorUtility.DisplayDialog(BodyQvStrings.ConfirmTitle,
                    string.Format(BodyQvStrings.ConfirmPickupCollision, layer),
                    BodyQvStrings.Apply, BodyQvStrings.Cancel))
            {
                for (int i = 0; i < pickupLayers.Count; i++)
                    Physics.IgnoreLayerCollision(layer, pickupLayers[i], false);
            }
        }

        private List<int> GetTargetPickupLayers()
        {
            BodyQvManager manager = (BodyQvManager)target;
            List<int> layers = new List<int>();
            VRC_Pickup[] pickups = manager.TargetPickups;
            if (pickups == null)
                return layers;

            for (int i = 0; i < pickups.Length; i++)
            {
                VRC_Pickup pickup = pickups[i];
                if (pickup == null || layers.Contains(pickup.gameObject.layer))
                    continue;
                layers.Add(pickup.gameObject.layer);
            }
            return layers;
        }

        private void DrawSurftraceWarnings(int layer)
        {
            BodyQvManager manager = (BodyQvManager)target;
            QvPen_PenManager[] pens = manager.TargetedPens;
            if (pens == null)
                return;

            int bit = 1 << layer;
            for (int i = 0; i < pens.Length; i++)
            {
                QvPen_PenManager pen = pens[i];
                if (pen == null || (pen.surftraceMask.value & bit) != 0)
                    continue;

                EditorGUILayout.HelpBox(BodyQvStrings.SurftraceMask + "\n" + pen.name, MessageType.Warning);
                if (!GUILayout.Button(BodyQvStrings.FixSurftraceMask + " — " + pen.name))
                    continue;

                Undo.RecordObject(pen, BodyQvStrings.FixSurftraceMask);
                pen.surftraceMask = pen.surftraceMask.value | bit;
                EditorUtility.SetDirty(pen);
            }
        }
    }

    internal class BodyQvPenPickerWindow : EditorWindow
    {
        private BodyQvManager manager;
        private QvPen_PenManager[] scenePens;
        private bool[] selected;
        private Vector2 scroll;

        public static void Open(BodyQvManager targetManager)
        {
            BodyQvPenPickerWindow window = CreateInstance<BodyQvPenPickerWindow>();
            window.titleContent = new GUIContent(BodyQvStrings.PickerTitle);
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
            {
                if (found[i].gameObject.scene.IsValid())
                    valid.Add(found[i]);
            }
            scenePens = valid.ToArray();
            selected = new bool[scenePens.Length];
        }

        private void OnGUI()
        {
            if (manager == null)
            {
                Close();
                return;
            }

            if (scenePens == null || scenePens.Length == 0)
            {
                EditorGUILayout.HelpBox(BodyQvStrings.PickerEmpty, MessageType.Info);
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (int i = 0; i < scenePens.Length; i++)
            {
                QvPen_PenManager pen = scenePens[i];
                selected[i] = EditorGUILayout.ToggleLeft(GetHierarchyPath(pen.transform), selected[i]);
            }
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button(BodyQvStrings.AddSelected))
            {
                AddSelectedPens();
                Close();
            }
        }

        private void AddSelectedPens()
        {
            SerializedObject serializedManager = new SerializedObject(manager);
            SerializedProperty pens = serializedManager.FindProperty("targetedPens");
            for (int i = 0; i < scenePens.Length; i++)
            {
                if (!selected[i] || Contains(pens, scenePens[i]))
                    continue;

                int index = pens.arraySize;
                pens.InsertArrayElementAtIndex(index);
                pens.GetArrayElementAtIndex(index).objectReferenceValue = scenePens[i];
            }

            serializedManager.ApplyModifiedProperties();
            manager.RefreshTargetReferences();
            EditorUtility.SetDirty(manager);
        }

        private static bool Contains(SerializedProperty array, Object value)
        {
            for (int i = 0; i < array.arraySize; i++)
            {
                if (array.GetArrayElementAtIndex(i).objectReferenceValue == value)
                    return true;
            }
            return false;
        }

        private static string GetHierarchyPath(Transform target)
        {
            string path = target.name;
            while (target.parent != null)
            {
                target = target.parent;
                path = target.name + "/" + path;
            }
            return path;
        }
    }
}
