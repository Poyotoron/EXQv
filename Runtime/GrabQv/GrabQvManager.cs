using QvPen.UdonScript;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using Utilities = VRC.SDKBase.Utilities;

namespace Maaaaa.EXQv
{
    [DefaultExecutionOrder(-90)]
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class GrabQvManager : UdonSharpBehaviour
    {
        public const int MaxBindings = 1024;
        private const int MaxKnownInks = 2048;
        private const int MaxPendingInks = 32;
        private const int MaxPendingRequests = 64;
        private const float IdentityTolerance = 0.0001f;
        private const float FullPoolScanInterval = 5f;
        private const float RequestRetryInterval = 3f;
        private const float EmptyReleaseDelay = 3f;
        private const float NeverArrivedReleaseDelay = 30f;
        private const float BindingArrivalGracePeriod = 10f;
        private const float BoundsPadding = 0.03f;
        private const float MinimumBoundsSize = 0.05f;
        private const float HeldHandleSyncTimeout = 1f;
        private const float HeldHandleMoveDistance = 0.001f;
        private const float HeldHandleMoveAngle = 0.1f;
        private const float EraseSendTimeout = 10f;
        public const float EraseSyncSafetyDelay = 1f;
        private const int ObjectSequenceRange = 1000000;
        private const int MaxCombinedStates = 256;
        private const int StateNone = 0;
        private const int StateAttached = 1;
        private const int StateFixed = 2;
        private const int StateHeld = 3;

        [Header(GrabQvStrings.TargetPensHeader)]
        [SerializeField, Tooltip(GrabQvStrings.TargetPensTooltip)]
        private QvPen_PenManager[] targetedPens = new QvPen_PenManager[0];

        [SerializeField, HideInInspector]
        private QvPen_LateSync[] targetLateSyncs = new QvPen_LateSync[0];

        [SerializeField, HideInInspector]
        private VRC_Pickup[] targetPickups = new VRC_Pickup[0];

        [SerializeField, HideInInspector]
        private BodyQvManager bodyQvManager;

        [SerializeField, InspectorName(GrabQvStrings.AutoSplitDistanceLabel),
         Tooltip(GrabQvStrings.AutoSplitDistanceTooltip)]
        private float autoSplitDistance = 2f;

        [SerializeField, InspectorName(GrabQvStrings.LogResultsLabel)]
        private bool logResults;

        [SerializeField, InspectorName(GrabQvStrings.ToggleGrabLabel),
         Tooltip(GrabQvStrings.ToggleGrabTooltip)]
        private bool toggleGrab = true;

        [SerializeField, HideInInspector]
        private GameObject[] handleObjects = new GameObject[0];

        [SerializeField, HideInInspector]
        private VRC_Pickup[] handlePickups = new VRC_Pickup[0];

        [SerializeField, HideInInspector]
        private BoxCollider[] handleColliders = new BoxCollider[0];

        [SerializeField, HideInInspector]
        private VRCObjectSync[] handleSyncs = new VRCObjectSync[0];

        [SerializeField, HideInInspector]
        private Transform[] handleTargets = new Transform[0];

        private Vector3[] handleHomePositions = new Vector3[0];
        private Quaternion[] handleHomeRotations = new Quaternion[0];
        private Vector3[] handleTargetHomePositions = new Vector3[0];
        private Quaternion[] handleTargetHomeRotations = new Quaternion[0];
        private int[] handleObjectIds = new int[0];
        private float[] handleAssignedTimes = new float[0];
        private float[] handleEmptySince = new float[0];
        private bool[] handleSawInk = new bool[0];
        private bool[] handleWasLocallyHeld = new bool[0];
        private bool[] handlePickupPending = new bool[0];
        private bool[] handleDropPending = new bool[0];
        private int[] handleHeldHandMasks = new int[0];
        private Vector3[] handleLastFramePositions = new Vector3[0];
        private Quaternion[] handleLastFrameRotations = new Quaternion[0];
        private bool[] handleHasLastFrame = new bool[0];

        private int[] currentObjectIds = new int[0];
        private int localObjectSequence;

        private int[] objectReferenceIds = new int[MaxBindings];
        private Vector3[] objectReferencePositions = new Vector3[MaxBindings];
        private int objectReferenceCount;

        private int[] bindingPenIds = new int[MaxBindings];
        private int[] bindingInkIds = new int[MaxBindings];
        private int[] bindingObjectIds = new int[MaxBindings];
        private int[] bindingPenIndexes = new int[MaxBindings];
        private Vector3[] bindingHandlePositions = new Vector3[MaxBindings];
        private Quaternion[] bindingHandleRotations = new Quaternion[MaxBindings];
        private LineRenderer[] bindingLines = new LineRenderer[MaxBindings];
        private bool[] bindingSawInk = new bool[MaxBindings];
        private bool[] bindingWasApplied = new bool[MaxBindings];
        private float[] bindingReceivedTimes = new float[MaxBindings];
        private int bindingCount;

        private int[] knownPenIds = new int[MaxKnownInks];
        private int[] knownInkIds = new int[MaxKnownInks];
        private LineRenderer[] knownLines = new LineRenderer[MaxKnownInks];
        private int knownInkCount;

        private int[] poolChildCounts = new int[0];
        private Transform[] poolLastChildren = new Transform[0];
        private float nextFullPoolScanTime;

        private int[] pendingPenIds = new int[MaxPendingInks];
        private int[] pendingInkIds = new int[MaxPendingInks];
        private int[] pendingPenIndexes = new int[MaxPendingInks];
        private int[] pendingExcludedHandMasks = new int[MaxPendingInks];
        private LineRenderer[] pendingLines = new LineRenderer[MaxPendingInks];
        private int pendingInkCount;

        private int[] pendingRequestObjectIds = new int[MaxPendingRequests];
        private Vector3[] pendingRequestPositions = new Vector3[MaxPendingRequests];
        private float[] pendingRequestTimes = new float[MaxPendingRequests];
        private int pendingRequestCount;

        private int[] erasePenIndexes = new int[MaxBindings];
        private Vector3[] erasePenIdVectors = new Vector3[MaxBindings];
        private Vector3[] eraseInkIdVectors = new Vector3[MaxBindings];
        private LineRenderer[] eraseLines = new LineRenderer[MaxBindings];
        private bool[] eraseOwnedByLocal = new bool[MaxBindings];
        private bool[] eraseWasSent = new bool[MaxBindings];
        private float[] eraseSendStartedTimes = new float[MaxBindings];
        private int eraseCount;
        private bool[] eraseActive = new bool[0];
        private bool[] eraseLastAcceptedWasLocal = new bool[0];
        private float[] eraseLastAcceptedTimes = new float[0];
        private bool[] eraseHasAccepted = new bool[0];

        private float nextHandleCleanupTime;

        private bool[] combinedPens = new bool[0];
        private Vector3[] bindingSamplePoints = new Vector3[BodyQvManager.MaxStrokeSamples];
        private Vector3[] rootCandidatePoints = new Vector3[BodyQvManager.MaxRootCandidates];

        private int[] stateObjectIds = new int[MaxCombinedStates];
        private int[] stateKinds = new int[MaxCombinedStates];
        private int[] statePlayerIds = new int[MaxCombinedStates];
        private int[] stateBindingTypes = new int[MaxCombinedStates];
        private Vector3[] statePositions = new Vector3[MaxCombinedStates];
        private Quaternion[] stateRotations = new Quaternion[MaxCombinedStates];
        private int[] stateFreshness = new int[MaxCombinedStates];
        private int[] stateAuthors = new int[MaxCombinedStates];
        private Vector3[] stateLastFramePositions = new Vector3[MaxCombinedStates];
        private Quaternion[] stateLastFrameRotations = new Quaternion[MaxCombinedStates];
        private bool[] stateHasLastFrame = new bool[MaxCombinedStates];
        private float[] stateReceivedTimes = new float[MaxCombinedStates];
        private Vector3[] stateHeldHandlePositions = new Vector3[MaxCombinedStates];
        private Quaternion[] stateHeldHandleRotations = new Quaternion[MaxCombinedStates];
        private bool[] stateWaitingForHandleSync = new bool[MaxCombinedStates];
        private int stateCount;
        private int[] framePosePlayerIds = new int[MaxCombinedStates];
        private int[] framePoseTypes = new int[MaxCombinedStates];
        private Vector3[] framePosePositions = new Vector3[MaxCombinedStates];
        private Quaternion[] framePoseRotations = new Quaternion[MaxCombinedStates];
        private bool[] framePoseValid = new bool[MaxCombinedStates];
        private int framePoseCount;

        [UdonSynced] private int[] syncedHandleObjectIds = new int[0];
        [UdonSynced] private int[] syncedCurrentObjectIds = new int[0];
        [UdonSynced] private int[] syncedBindingPenIds = new int[0];
        [UdonSynced] private int[] syncedBindingInkIds = new int[0];
        [UdonSynced] private int[] syncedBindingObjectIds = new int[0];
        [UdonSynced] private Vector3[] syncedBindingHandlePositions = new Vector3[0];
        [UdonSynced] private Quaternion[] syncedBindingHandleRotations = new Quaternion[0];
        [UdonSynced] private int[] syncedBindingPenIndexes = new int[0];
        [UdonSynced] private int[] syncedStateObjectIds = new int[0];
        [UdonSynced] private int[] syncedStateKinds = new int[0];
        [UdonSynced] private int[] syncedStatePlayerIds = new int[0];
        [UdonSynced] private int[] syncedStateBindingTypes = new int[0];
        [UdonSynced] private Vector3[] syncedStatePositions = new Vector3[0];
        [UdonSynced] private Quaternion[] syncedStateRotations = new Quaternion[0];
        [UdonSynced] private int[] syncedStateFreshness = new int[0];
        [UdonSynced] private int[] syncedStateAuthors = new int[0];

        public QvPen_PenManager[] TargetedPens => targetedPens;
        public QvPen_LateSync[] TargetLateSyncs => targetLateSyncs;
        public VRC_Pickup[] TargetPickups => targetPickups;
        public GameObject[] HandleObjects => handleObjects;
        public Transform[] HandleTargets => handleTargets;
        public VRC_Pickup[] HandlePickups => handlePickups;
        public BoxCollider[] HandleColliders => handleColliders;
        public VRCObjectSync[] HandleSyncs => handleSyncs;
        public BodyQvManager BodyQvManager => bodyQvManager;
        public bool[] CombinedPens => combinedPens;

        private void Start()
        {
            ResolveTargetReferences();
            InitializeCombinedPens();
            InitializeHandles();
            currentObjectIds = new int[targetedPens.Length];
            eraseActive = new bool[targetedPens.Length];
            eraseLastAcceptedWasLocal = new bool[targetedPens.Length];
            eraseLastAcceptedTimes = new float[targetedPens.Length];
            eraseHasAccepted = new bool[targetedPens.Length];
            poolChildCounts = new int[targetLateSyncs.Length * 2];
            poolLastChildren = new Transform[targetLateSyncs.Length * 2];
            for (int i = 0; i < poolChildCounts.Length; i++)
                poolChildCounts[i] = -1;
            nextFullPoolScanTime = Time.time + FullPoolScanInterval;
            nextHandleCleanupTime = Time.time + 0.5f;
            if (Utilities.IsValid(bodyQvManager) && bodyQvManager.gameObject.activeInHierarchy)
            {
                for (int i = 0; i < targetedPens.Length; i++)
                {
                    if (combinedPens[i])
                        bodyQvManager.RegisterCombinedPen(targetedPens[i]);
                }
            }
        }

        private void Update()
        {
            ScanInkPools();
            RetryHandleRequests();
            DetectCombinedHandleChanges();
            if (Networking.IsOwner(gameObject) && Time.time >= nextHandleCleanupTime)
            {
                nextHandleCleanupTime = Time.time + 0.5f;
                CleanupHandles();
            }
        }

        public override void PostLateUpdate()
        {
            ProcessSplitErases();
            ProcessPendingInks();
            framePoseCount = 0;
            ProcessHandleChanges();
            UpdateLocallyHeldHandles();
            if (bindingCount == 0 && stateCount == 0 && !HasAssignedHandle())
                return;

            UpdateFramesAndGrabTargets();

            int index = 0;
            while (index < bindingCount)
            {
                LineRenderer line = bindingLines[index];
                if (bindingSawInk[index] && !Utilities.IsValid(line))
                {
                    int objectId = bindingObjectIds[index];
                    RemoveBindingAt(index);
                    if (Networking.IsOwner(gameObject))
                        RequestSerialization();
                    UpdateHandleBounds(objectId);
                    continue;
                }

                if (!bindingWasApplied[index])
                {
                    if (Utilities.IsValid(line))
                        TryApplyBindingAt(index, line);
                    index++;
                    continue;
                }

                Vector3 framePosition;
                Quaternion frameRotation;
                if (IsCombinedBinding(index))
                {
                    if (!TryGetObjectFrame(bindingObjectIds[index], out framePosition, out frameRotation))
                    {
                        index++;
                        continue;
                    }
                }
                else
                {
                    int handleIndex = FindHandleForObject(bindingObjectIds[index]);
                    if (handleIndex < 0 || !Utilities.IsValid(handleObjects[handleIndex]))
                    {
                        index++;
                        continue;
                    }
                    Transform handle = handleObjects[handleIndex].transform;
                    framePosition = handle.position;
                    frameRotation = handle.rotation;
                }

                Quaternion deltaRotation = frameRotation * Quaternion.Inverse(bindingHandleRotations[index]);
                Vector3 worldPosition = deltaRotation * (-bindingHandlePositions[index]) + framePosition;
                line.transform.SetPositionAndRotation(worldPosition, deltaRotation);
                index++;
            }
        }

        public void SplitPen(QvPen_PenManager pen)
        {
            int penIndex = FindTargetPen(pen);
            if (penIndex < 0)
                return;

            ReceiveSplit(penIndex);
            SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(ReceiveSplit), penIndex);
        }

        public void EraseLatestGroup(QvPen_PenManager pen)
        {
            int penIndex = FindTargetPen(pen);
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (penIndex < 0 || !Utilities.IsValid(localPlayer))
                return;

            int newestInkId = int.MinValue;
            LineRenderer newestLine = null;
            Vector3 newestPenIdVector = Vector3.zero;
            Vector3 newestInkIdVector = Vector3.zero;
            QvPen_LateSync lateSync = targetLateSyncs[penIndex];
            if (!Utilities.IsValid(lateSync))
                return;
            FindNewestLocalInk(lateSync.InkPoolSynced, penIndex, localPlayer.playerId, ref newestInkId,
                ref newestLine, ref newestPenIdVector, ref newestInkIdVector);
            FindNewestLocalInk(lateSync.InkPoolNotSynced, penIndex, localPlayer.playerId, ref newestInkId,
                ref newestLine, ref newestPenIdVector, ref newestInkIdVector);
            if (!Utilities.IsValid(newestLine))
                return;

            int bindingIndex = FindBinding(QvPenUtilities.Vector3ToInt32(newestPenIdVector), newestInkId);
            int objectId = bindingIndex >= 0 ? bindingObjectIds[bindingIndex] : 0;
            int startCount = eraseCount;
            int otherCount = 0;
            if (objectId != 0)
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int i = 0; i < bindingCount; i++)
                    {
                        if (bindingPenIndexes[i] != penIndex || bindingObjectIds[i] != objectId ||
                            !Utilities.IsValid(bindingLines[i]) || IsQueuedForErase(penIndex, bindingLines[i]))
                            continue;
                        bool ownedByLocal = IsLineOwnedBy(bindingLines[i], localPlayer.playerId);
                        if ((pass == 0 && ownedByLocal) || (pass == 1 && !ownedByLocal))
                            continue;
                        if (AppendErase(penIndex, bindingLines[i], ownedByLocal) && !ownedByLocal)
                            otherCount++;
                    }
                }
            }
            else
            {
                AppendErase(penIndex, newestLine, true);
            }

            int added = eraseCount - startCount;
            if (added <= 0)
                return;
            if (!eraseActive[penIndex])
            {
                if (!Networking.IsOwner(pen.gameObject) && !pen._TakeOwnership())
                {
                    RemoveEraseEntries(penIndex);
                    return;
                }
                eraseActive[penIndex] = true;
            }
            Log(GrabQvStrings.EraseStartedLog + objectId + GrabQvStrings.EraseInkCountLog + added +
                GrabQvStrings.EraseOtherCountLog + otherCount);
        }

        public bool CanUndoAfterSplitErase(QvPen_PenManager pen)
        {
            int penIndex = FindTargetPen(pen);
            if (penIndex < 0)
                return true;
            return !eraseActive[penIndex] && (!eraseHasAccepted[penIndex] ||
                Time.time - eraseLastAcceptedTimes[penIndex] >= EraseSyncSafetyDelay);
        }

        public bool CanRunSelfAfterSplitErase(QvPen_PenManager pen)
        {
            int penIndex = FindTargetPen(pen);
            if (penIndex < 0)
                return true;
            if (eraseActive[penIndex])
            {
                int entry = FindFirstEraseEntry(penIndex);
                if (entry >= 0 && eraseWasSent[entry] && !Utilities.IsValid(eraseLines[entry]))
                    return eraseOwnedByLocal[entry];
                return eraseHasAccepted[penIndex] && eraseLastAcceptedWasLocal[penIndex];
            }
            return !eraseHasAccepted[penIndex] || eraseLastAcceptedWasLocal[penIndex] ||
                Time.time - eraseLastAcceptedTimes[penIndex] >= EraseSyncSafetyDelay;
        }

        public void StopSplitEraseForSelf(QvPen_PenManager pen)
        {
            StopSplitErase(FindTargetPen(pen), GrabQvStrings.EraseStoppedSelf);
        }

        public void StopSplitEraseForAll(QvPen_PenManager pen)
        {
            StopSplitErase(FindTargetPen(pen), GrabQvStrings.EraseStoppedAll);
        }

        private void FindNewestLocalInk(Transform pool, int penIndex, int localPlayerId, ref int newestInkId,
            ref LineRenderer newestLine, ref Vector3 newestPenIdVector, ref Vector3 newestInkIdVector)
        {
            if (!Utilities.IsValid(pool))
                return;
            for (int i = 0; i < pool.childCount; i++)
            {
                Transform child = pool.GetChild(i);
                Vector3 penIdVector;
                Vector3 inkIdVector;
                Vector3 ownerIdVector;
                if (!Utilities.IsValid(child) || !QvPenUtilities.TryGetIdFromInk(child.gameObject,
                    out penIdVector, out inkIdVector, out ownerIdVector) ||
                    QvPenUtilities.EulerAnglesToPlayerId(ownerIdVector) != localPlayerId)
                    continue;
                int inkId = QvPenUtilities.Vector3ToInt32(inkIdVector);
                LineRenderer line = child.GetComponent<LineRenderer>();
                if (inkId <= newestInkId || !Utilities.IsValid(line) || IsQueuedForErase(penIndex, line))
                    continue;
                newestInkId = inkId;
                newestLine = line;
                newestPenIdVector = penIdVector;
                newestInkIdVector = inkIdVector;
            }
        }

        private bool AppendErase(int penIndex, LineRenderer line, bool ownedByLocal)
        {
            if (eraseCount >= MaxBindings || !Utilities.IsValid(line))
                return false;
            Vector3 penIdVector;
            Vector3 inkIdVector;
            Vector3 ownerIdVector;
            if (!QvPenUtilities.TryGetIdFromInk(line.gameObject, out penIdVector, out inkIdVector,
                out ownerIdVector))
                return false;
            erasePenIndexes[eraseCount] = penIndex;
            erasePenIdVectors[eraseCount] = penIdVector;
            eraseInkIdVectors[eraseCount] = inkIdVector;
            eraseLines[eraseCount] = line;
            eraseOwnedByLocal[eraseCount] = ownedByLocal;
            eraseWasSent[eraseCount] = false;
            eraseSendStartedTimes[eraseCount] = 0f;
            eraseCount++;
            return true;
        }

        private bool IsLineOwnedBy(LineRenderer line, int playerId)
        {
            Vector3 penIdVector;
            Vector3 inkIdVector;
            Vector3 ownerIdVector;
            return Utilities.IsValid(line) && QvPenUtilities.TryGetIdFromInk(line.gameObject,
                out penIdVector, out inkIdVector, out ownerIdVector) &&
                QvPenUtilities.EulerAnglesToPlayerId(ownerIdVector) == playerId;
        }

        private bool IsQueuedForErase(int penIndex, LineRenderer line)
        {
            for (int i = 0; i < eraseCount; i++)
                if (erasePenIndexes[i] == penIndex && eraseLines[i] == line) return true;
            return false;
        }

        private void ProcessSplitErases()
        {
            if (eraseCount == 0)
                return;
            for (int penIndex = 0; penIndex < eraseActive.Length; penIndex++)
            {
                if (!eraseActive[penIndex])
                    continue;
                if (Utilities.IsValid(targetPickups[penIndex]) && targetPickups[penIndex].IsHeld)
                {
                    StopSplitErase(penIndex, GrabQvStrings.EraseStoppedHeld);
                    continue;
                }
                QvPen_PenManager pen = targetedPens[penIndex];
                if (!Utilities.IsValid(pen) || !Networking.IsOwner(pen.gameObject))
                {
                    StopSplitErase(penIndex, GrabQvStrings.EraseStoppedOwner);
                    continue;
                }
                int entry = FindFirstEraseEntry(penIndex);
                if (entry < 0)
                {
                    eraseActive[penIndex] = false;
                    continue;
                }
                LineRenderer line = eraseLines[entry];
                if (!Utilities.IsValid(line))
                {
                    if (eraseWasSent[entry])
                    {
                        eraseHasAccepted[penIndex] = true;
                        eraseLastAcceptedWasLocal[penIndex] = eraseOwnedByLocal[entry];
                        eraseLastAcceptedTimes[penIndex] = Time.time;
                    }
                    RemoveEraseAt(entry);
                    if (FindFirstEraseEntry(penIndex) < 0)
                        eraseActive[penIndex] = false;
                    continue;
                }
                if (eraseWasSent[entry] && Time.time - eraseSendStartedTimes[entry] >= EraseSendTimeout)
                {
                    StopSplitErase(penIndex, GrabQvStrings.EraseStoppedTimeout);
                    continue;
                }
                if (!eraseWasSent[entry])
                {
                    eraseWasSent[entry] = true;
                    eraseSendStartedTimes[entry] = Time.time;
                }
                int length = QvPen_Pen.FOOTER_ELEMENT_ERASE_LENGTH;
                Vector3[] data = new Vector3[length];
                VRCPlayerApi localPlayer = Networking.LocalPlayer;
                if (!Utilities.IsValid(localPlayer))
                    continue;
                data[length - 1 - QvPen_Pen.FOOTER_ELEMENT_DATA_INFO] =
                    new Vector3(localPlayer.playerId, (int)QvPen_Pen_Mode.Erase, length);
                data[length - 1 - QvPen_Pen.FOOTER_ELEMENT_PEN_ID] = erasePenIdVectors[entry];
                data[length - 1 - QvPen_Pen.FOOTER_ELEMENT_INK_ID] = eraseInkIdVectors[entry];
                pen._SendData(data);
            }
        }

        private int FindFirstEraseEntry(int penIndex)
        {
            for (int i = 0; i < eraseCount; i++)
                if (erasePenIndexes[i] == penIndex) return i;
            return -1;
        }

        private void StopSplitErase(int penIndex, string reason)
        {
            if (penIndex < 0 || penIndex >= eraseActive.Length)
                return;
            bool wasActive = eraseActive[penIndex];
            RemoveEraseEntries(penIndex);
            eraseActive[penIndex] = false;
            if (wasActive)
                Log(GrabQvStrings.EraseStoppedLog + reason);
        }

        private void RemoveEraseEntries(int penIndex)
        {
            for (int i = eraseCount - 1; i >= 0; i--)
                if (erasePenIndexes[i] == penIndex) RemoveEraseAt(i);
        }

        private void RemoveEraseAt(int index)
        {
            for (int i = index; i < eraseCount - 1; i++)
            {
                erasePenIndexes[i] = erasePenIndexes[i + 1];
                erasePenIdVectors[i] = erasePenIdVectors[i + 1];
                eraseInkIdVectors[i] = eraseInkIdVectors[i + 1];
                eraseLines[i] = eraseLines[i + 1];
                eraseOwnedByLocal[i] = eraseOwnedByLocal[i + 1];
                eraseWasSent[i] = eraseWasSent[i + 1];
                eraseSendStartedTimes[i] = eraseSendStartedTimes[i + 1];
            }
            eraseCount--;
            eraseLines[eraseCount] = null;
        }

        [NetworkCallable(maxEventsPerSecond: 20)]
        public void ReceiveSplit(int penIndex)
        {
            if (penIndex < 0 || penIndex >= currentObjectIds.Length)
                return;
            currentObjectIds[penIndex] = 0;
            if (Networking.IsOwner(gameObject))
                RequestSerialization();
        }

        private void ScanInkPools()
        {
            int poolCount = targetLateSyncs.Length * 2;
            if (poolCount == 0)
                return;

            bool periodicScan = Time.time >= nextFullPoolScanTime;
            if (periodicScan)
                nextFullPoolScanTime = Time.time + FullPoolScanInterval;

            for (int i = 0; i < targetLateSyncs.Length; i++)
            {
                QvPen_LateSync lateSync = targetLateSyncs[i];
                if (!Utilities.IsValid(lateSync))
                    continue;
                ScanInkPoolIfNeeded(lateSync.InkPoolSynced, i * 2, i, periodicScan);
                ScanInkPoolIfNeeded(lateSync.InkPoolNotSynced, i * 2 + 1, i, periodicScan);
            }
        }

        private void ScanInkPoolIfNeeded(Transform pool, int countIndex, int penIndex, bool periodicScan)
        {
            if (!Utilities.IsValid(pool))
                return;
            int childCount = pool.childCount;
            Transform lastChild = childCount > 0 ? pool.GetChild(childCount - 1) : null;
            if (!periodicScan && poolChildCounts[countIndex] == childCount && poolLastChildren[countIndex] == lastChild)
                return;
            poolChildCounts[countIndex] = childCount;
            poolLastChildren[countIndex] = lastChild;
            for (int i = 0; i < childCount; i++)
            {
                Transform child = pool.GetChild(i);
                if (Utilities.IsValid(child))
                    ProcessInk(child.gameObject, penIndex);
            }
        }

        private void ProcessInk(GameObject ink, int penIndex)
        {
            Vector3 penIdVector;
            Vector3 inkIdVector;
            Vector3 ownerIdVector;
            if (!QvPenUtilities.TryGetIdFromInk(ink, out penIdVector, out inkIdVector, out ownerIdVector))
                return;

            int penId = QvPenUtilities.Vector3ToInt32(penIdVector);
            int inkId = QvPenUtilities.Vector3ToInt32(inkIdVector);
            int knownIndex = FindKnownInk(penId, inkId);
            if (knownIndex >= 0)
            {
                if (!Utilities.IsValid(knownLines[knownIndex]))
                    knownLines[knownIndex] = ink.GetComponent<LineRenderer>();
                return;
            }

            LineRenderer line = ink.GetComponent<LineRenderer>();
            if (!Utilities.IsValid(line) || !line.useWorldSpace || !IsInkTransformIdentity(line.transform))
                return;
            RememberInk(penId, inkId, line);

            int bindingIndex = FindBinding(penId, inkId);
            if (bindingIndex >= 0)
            {
                TryApplyBindingAt(bindingIndex, line);
                return;
            }

            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (!Utilities.IsValid(localPlayer) || QvPenUtilities.EulerAnglesToPlayerId(ownerIdVector) != localPlayer.playerId)
                return;
            int excludedHandMask = 0;
            if (IsCombinedPen(penIndex) && Utilities.IsValid(bodyQvManager))
                excludedHandMask = bodyQvManager.GetDrawingHandMask(targetedPens[penIndex]);
            EnqueueInk(penId, inkId, penIndex, line, excludedHandMask);
        }

        private void EnqueueInk(int penId, int inkId, int penIndex, LineRenderer line, int excludedHandMask)
        {
            if (pendingInkCount >= MaxPendingInks)
            {
                for (int i = 0; i < MaxPendingInks - 1; i++)
                {
                    pendingPenIds[i] = pendingPenIds[i + 1];
                    pendingInkIds[i] = pendingInkIds[i + 1];
                    pendingPenIndexes[i] = pendingPenIndexes[i + 1];
                    pendingExcludedHandMasks[i] = pendingExcludedHandMasks[i + 1];
                    pendingLines[i] = pendingLines[i + 1];
                }
                pendingInkCount--;
            }
            pendingPenIds[pendingInkCount] = penId;
            pendingInkIds[pendingInkCount] = inkId;
            pendingPenIndexes[pendingInkCount] = penIndex;
            pendingExcludedHandMasks[pendingInkCount] = excludedHandMask;
            pendingLines[pendingInkCount] = line;
            pendingInkCount++;
        }

        private void ProcessPendingInks()
        {
            int count = pendingInkCount;
            pendingInkCount = 0;
            for (int i = 0; i < count; i++)
            {
                LineRenderer line = pendingLines[i];
                pendingLines[i] = null;
                if (!Utilities.IsValid(line) || !line.useWorldSpace || !IsInkTransformIdentity(line.transform) || line.positionCount <= 0)
                    continue;
                CreateOrAttachInk(pendingPenIds[i], pendingInkIds[i], pendingPenIndexes[i], line,
                    pendingExcludedHandMasks[i]);
            }
        }

        private void CreateOrAttachInk(int penId, int inkId, int penIndex, LineRenderer line,
            int excludedHandMask)
        {
            if (penIndex < 0 || penIndex >= currentObjectIds.Length)
                return;

            int objectId = currentObjectIds[penIndex];
            bool combined = IsCombinedPen(penIndex);
            if (objectId != 0 && FindHandleForObject(objectId) < 0 &&
                FindPendingRequest(objectId) < 0 && (!combined || FindState(objectId) < 0))
                objectId = 0;
            if (objectId != 0 && ShouldAutoSplit(objectId, line))
                objectId = 0;

            Vector3 referencePosition;
            if (objectId == 0)
            {
                objectId = CreateObjectId();
                if (objectId == 0)
                    return;
                referencePosition = GetLineBoundsCenter(line);
                RememberObjectReference(objectId, referencePosition);
                ReceiveCurrentObject(penIndex, objectId, referencePosition);
                SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(ReceiveCurrentObject),
                    penIndex, objectId, referencePosition);
                QueueHandleRequest(objectId, referencePosition);
                Log(GrabQvStrings.CreatedLog + objectId);
                if (combined)
                    DetermineAndBroadcastState(objectId, line, referencePosition, Quaternion.identity,
                        excludedHandMask, true);
            }
            else if (!TryGetObjectReference(objectId, out referencePosition))
            {
                referencePosition = GetLineBoundsCenter(line);
                RememberObjectReference(objectId, referencePosition);
            }

            int handleIndex = FindHandleForObject(objectId);
            Vector3 handlePosition = handleIndex >= 0 ? handleObjects[handleIndex].transform.position : referencePosition;
            Quaternion handleRotation = handleIndex >= 0 ? handleObjects[handleIndex].transform.rotation : Quaternion.identity;
            Vector3 framePosition = handlePosition;
            Quaternion frameRotation = handleRotation;
            if (combined && !TryGetObjectFrame(objectId, out framePosition, out frameRotation))
                return;
            ReceiveBinding(penId, inkId, objectId, penIndex, framePosition, frameRotation);
            SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(ReceiveBinding),
                penId, inkId, objectId, penIndex, framePosition, frameRotation);
        }

        private int CreateObjectId()
        {
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (!Utilities.IsValid(localPlayer) || localPlayer.playerId <= 0 || localPlayer.playerId > 2000)
                return 0;
            localObjectSequence++;
            if (localObjectSequence <= 0 || localObjectSequence >= ObjectSequenceRange)
                localObjectSequence = 1;
            return localPlayer.playerId * ObjectSequenceRange + localObjectSequence;
        }

        [NetworkCallable(maxEventsPerSecond: 50)]
        public void ReceiveCurrentObject(int penIndex, int objectId, Vector3 referencePosition)
        {
            if (penIndex < 0 || penIndex >= currentObjectIds.Length || !IsValidObjectId(objectId) || !IsFinite(referencePosition))
                return;
            currentObjectIds[penIndex] = objectId;
            RememberObjectReference(objectId, referencePosition);
            if (Networking.IsOwner(gameObject))
                RequestSerialization();
        }

        private void QueueHandleRequest(int objectId, Vector3 referencePosition)
        {
            int index = FindPendingRequest(objectId);
            if (index < 0)
            {
                if (pendingRequestCount >= MaxPendingRequests)
                    return;
                index = pendingRequestCount++;
                pendingRequestObjectIds[index] = objectId;
                pendingRequestPositions[index] = referencePosition;
            }
            pendingRequestTimes[index] = Time.time;
            SendHandleRequest(objectId, referencePosition);
        }

        private void SendHandleRequest(int objectId, Vector3 referencePosition)
        {
            if (Networking.IsOwner(gameObject))
                ReceiveHandleRequest(objectId, referencePosition);
            else
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(ReceiveHandleRequest), objectId, referencePosition);
        }

        private void RetryHandleRequests()
        {
            int index = 0;
            while (index < pendingRequestCount)
            {
                int objectId = pendingRequestObjectIds[index];
                if (FindHandleForObject(objectId) >= 0)
                {
                    RemovePendingRequestAt(index);
                    continue;
                }
                if (Time.time - pendingRequestTimes[index] >= RequestRetryInterval)
                {
                    pendingRequestTimes[index] = Time.time;
                    SendHandleRequest(objectId, pendingRequestPositions[index]);
                }
                index++;
            }
        }

        [NetworkCallable(maxEventsPerSecond: 50)]
        public void ReceiveHandleRequest(int objectId, Vector3 referencePosition)
        {
            if (!Networking.IsOwner(gameObject) || !IsValidObjectId(objectId) || !IsFinite(referencePosition))
                return;
            if (FindHandleForObject(objectId) >= 0)
                return;

            int handleIndex = FindFreeHandle();
            if (handleIndex < 0)
            {
                Log(GrabQvStrings.FullLog + objectId);
                return;
            }

            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (Utilities.IsValid(localPlayer))
                Networking.SetOwner(localPlayer, handleObjects[handleIndex]);
            ResetHandleState(handleIndex);
            handleObjectIds[handleIndex] = objectId;
            handleAssignedTimes[handleIndex] = Time.time;
            handleEmptySince[handleIndex] = -1f;
            handleSawInk[handleIndex] = false;
            MoveHandle(handleIndex, referencePosition, Quaternion.identity);
            if (Utilities.IsValid(handleTargets[handleIndex]))
                handleTargets[handleIndex].SetPositionAndRotation(referencePosition, Quaternion.identity);
            SetHandleAvailable(handleIndex, true);
            RemovePendingRequest(objectId);
            RequestSerialization();
            Log(GrabQvStrings.AssignedLog + objectId);
        }

        [NetworkCallable(maxEventsPerSecond: 100)]
        public void ReceiveBinding(int penId, int inkId, int objectId, int penIndex,
            Vector3 handlePosition, Quaternion handleRotation)
        {
            if (!IsValidObjectId(objectId) || penIndex < 0 || penIndex >= targetedPens.Length ||
                !IsFinite(handlePosition) || !IsValidRotation(handleRotation))
                return;
            AddOrMergeBinding(penId, inkId, objectId, penIndex, handlePosition, handleRotation);
            if (Networking.IsOwner(gameObject))
                RequestSerialization();
        }

        private void AddOrMergeBinding(int penId, int inkId, int objectId, int penIndex,
            Vector3 handlePosition, Quaternion handleRotation)
        {
            int existing = FindBinding(penId, inkId);
            if (existing >= 0)
            {
                bindingPenIndexes[existing] = penIndex;
                if (!bindingWasApplied[existing])
                    TryApplyBindingAt(existing, FindKnownLine(penId, inkId));
                return;
            }
            if (bindingCount >= MaxBindings)
                RemoveBindingAt(0);
            int index = bindingCount++;
            bindingPenIds[index] = penId;
            bindingInkIds[index] = inkId;
            bindingObjectIds[index] = objectId;
            bindingPenIndexes[index] = penIndex;
            bindingHandlePositions[index] = handlePosition;
            bindingHandleRotations[index] = handleRotation;
            bindingLines[index] = null;
            bindingSawInk[index] = false;
            bindingWasApplied[index] = false;
            bindingReceivedTimes[index] = Time.time;
            TryApplyBindingAt(index, FindKnownLine(penId, inkId));
        }

        private void TryApplyBindingAt(int index, LineRenderer line)
        {
            if (index < 0 || index >= bindingCount || bindingWasApplied[index] || !Utilities.IsValid(line))
                return;
            bindingLines[index] = line;
            bindingSawInk[index] = true;
            int handleIndex = FindHandleForObject(bindingObjectIds[index]);
            bool combined = IsCombinedBinding(index);
            if (!combined && (handleIndex < 0 || !Utilities.IsValid(handleObjects[handleIndex])))
                return;
            if (combined && FindState(bindingObjectIds[index]) < 0)
                return;
            Transform inkTransform = line.transform;
            if (!line.useWorldSpace || !IsInkTransformIdentity(inkTransform))
                return;
            Vector3 framePosition;
            Quaternion frameRotation;
            if (combined)
            {
                if (!TryGetObjectFrame(bindingObjectIds[index], out framePosition, out frameRotation))
                    return;
            }
            else
            {
                Transform handle = handleObjects[handleIndex].transform;
                framePosition = handle.position;
                frameRotation = handle.rotation;
            }
            line.useWorldSpace = false;
            Quaternion deltaRotation = frameRotation * Quaternion.Inverse(bindingHandleRotations[index]);
            Vector3 worldPosition = deltaRotation * (-bindingHandlePositions[index]) + framePosition;
            inkTransform.SetPositionAndRotation(worldPosition, deltaRotation);
            bindingWasApplied[index] = true;
            if (handleIndex >= 0)
            {
                handleSawInk[handleIndex] = true;
                handleEmptySince[handleIndex] = -1f;
            }
            UpdateHandleBounds(bindingObjectIds[index]);
        }

        private void DetermineAndBroadcastState(int objectId, LineRenderer firstLine,
            Vector3 framePosition, Quaternion frameRotation, int excludedHandMask, bool drawn)
        {
            if (!Utilities.IsValid(bodyQvManager) || !bodyQvManager.gameObject.activeInHierarchy)
            {
                BroadcastState(objectId, StateFixed, 0, -1, framePosition, frameRotation);
                LogCombinedResult(objectId, drawn ? GrabQvStrings.DrawnReason : GrabQvStrings.DroppedReason,
                    false, -1, -1, float.MaxValue, "");
                return;
            }

            int sampleCount;
            int rootCount;
            int rootEndpointCount;
            if (drawn)
            {
                ReadLineSamples(firstLine, out sampleCount, out rootCount);
                rootEndpointCount = rootCount;
            }
            else
                ReadObjectSamples(objectId, out sampleCount, out rootCount, out rootEndpointCount);
            if (sampleCount <= 0)
            {
                BroadcastState(objectId, StateFixed, 0, -1, framePosition, frameRotation);
                return;
            }

            int playerId;
            int bindingType;
            float surfaceDistance;
            string method;
            Vector3 bonePosition;
            Quaternion boneRotation;
            if (bodyQvManager.TryDetermineBinding(bindingSamplePoints, sampleCount,
                    rootCandidatePoints, rootCount, rootEndpointCount, drawn, excludedHandMask,
                    out playerId, out bindingType, out surfaceDistance, out method,
                    out bonePosition, out boneRotation))
            {
                Quaternion inverseBone = Quaternion.Inverse(boneRotation);
                Vector3 localPosition = inverseBone * (framePosition - bonePosition);
                Quaternion localRotation = inverseBone * frameRotation;
                BroadcastState(objectId, StateAttached, playerId, bindingType,
                    localPosition, localRotation);
                LogCombinedResult(objectId, drawn ? GrabQvStrings.DrawnReason : GrabQvStrings.DroppedReason,
                    true, playerId, bindingType, surfaceDistance, method);
                return;
            }

            BroadcastState(objectId, StateFixed, 0, -1, framePosition, frameRotation);
            LogCombinedResult(objectId, drawn ? GrabQvStrings.DrawnReason : GrabQvStrings.DroppedReason,
                false, bodyQvManager.LastNearestPlayerId, bodyQvManager.LastNearestType,
                bodyQvManager.LastNearestDistance, "");
        }

        private void ReadLineSamples(LineRenderer line, out int sampleCount, out int rootCount)
        {
            sampleCount = 0;
            rootCount = 0;
            if (!Utilities.IsValid(line) || line.positionCount <= 0)
                return;
            int pointCount = line.positionCount;
            sampleCount = Mathf.Min(pointCount, BodyQvManager.MaxStrokeSamples);
            for (int i = 0; i < sampleCount; i++)
            {
                int offset = sampleCount == 1 ? 0 : i * (pointCount - 1) / (sampleCount - 1);
                bindingSamplePoints[i] = GetLineWorldPoint(line, pointCount - 1 - offset);
            }
            rootCandidatePoints[0] = GetLineWorldPoint(line, pointCount - 1);
            rootCount = 1;
            if (pointCount > 1)
            {
                rootCandidatePoints[1] = GetLineWorldPoint(line, 0);
                rootCount = 2;
            }
        }

        private void ReadObjectSamples(int objectId, out int sampleCount, out int rootCount,
            out int rootEndpointCount)
        {
            int totalPointCount = 0;
            rootCount = 0;
            for (int i = 0; i < bindingCount; i++)
            {
                if (bindingObjectIds[i] != objectId || !Utilities.IsValid(bindingLines[i]) ||
                    bindingLines[i].positionCount <= 0)
                    continue;
                LineRenderer line = bindingLines[i];
                totalPointCount += line.positionCount;
                if (rootCount < BodyQvManager.MaxRootCandidates)
                    rootCandidatePoints[rootCount++] = GetLineWorldPoint(line, line.positionCount - 1);
                if (line.positionCount > 1 && rootCount < BodyQvManager.MaxRootCandidates)
                    rootCandidatePoints[rootCount++] = GetLineWorldPoint(line, 0);
            }
            rootEndpointCount = rootCount;

            sampleCount = Mathf.Min(totalPointCount, BodyQvManager.MaxStrokeSamples);
            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                int wanted = sampleCount == 1 ? 0 : sampleIndex * (totalPointCount - 1) / (sampleCount - 1);
                int passed = 0;
                for (int bindingIndex = 0; bindingIndex < bindingCount; bindingIndex++)
                {
                    LineRenderer line = bindingLines[bindingIndex];
                    if (bindingObjectIds[bindingIndex] != objectId || !Utilities.IsValid(line) ||
                        line.positionCount <= 0)
                        continue;
                    if (wanted >= passed + line.positionCount)
                    {
                        passed += line.positionCount;
                        continue;
                    }
                    int lineOffset = wanted - passed;
                    bindingSamplePoints[sampleIndex] =
                        GetLineWorldPoint(line, line.positionCount - 1 - lineOffset);
                    break;
                }
            }

            for (int i = 0; i < sampleCount && rootCount < BodyQvManager.MaxRootCandidates; i++)
                rootCandidatePoints[rootCount++] = bindingSamplePoints[i];
        }

        private void BroadcastState(int objectId, int stateKind, int playerId, int bindingType,
            Vector3 position, Quaternion rotation)
        {
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            int author = Utilities.IsValid(localPlayer) ? localPlayer.playerId : 0;
            int freshness = Networking.GetServerTimeInMilliseconds();
            ReceiveCombinedState(objectId, stateKind, playerId, bindingType,
                position, rotation, freshness, author);
            SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(ReceiveCombinedState),
                objectId, stateKind, playerId, bindingType, position, rotation, freshness, author);
        }

        [NetworkCallable(maxEventsPerSecond: 100)]
        public void ReceiveCombinedState(int objectId, int stateKind, int playerId, int bindingType,
            Vector3 position, Quaternion rotation, int freshness, int author)
        {
            if (!IsValidObjectId(objectId) || stateKind < StateNone || stateKind > StateHeld ||
                !IsFinite(position) || !IsValidRotation(rotation) || author < 0 || author > 65535)
                return;
            if (stateKind == StateAttached &&
                (!Utilities.IsValid(bodyQvManager) || !bodyQvManager.IsValidBindingTarget(playerId, bindingType)))
                return;
            if (stateKind == StateHeld && (playerId <= 0 || playerId > 65535))
                return;

            int index = FindState(objectId);
            if (index >= 0 && !IsNewerState(freshness, author, stateFreshness[index], stateAuthors[index]))
                return;
            if (stateKind == StateNone)
            {
                if (index >= 0)
                    RemoveStateAt(index);
                if (Networking.IsOwner(gameObject))
                    RequestSerialization();
                return;
            }
            if (index < 0)
            {
                if (stateCount >= MaxCombinedStates)
                    RemoveStateAt(0);
                index = stateCount++;
                stateObjectIds[index] = objectId;
                stateHasLastFrame[index] = false;
            }
            stateKinds[index] = stateKind;
            statePlayerIds[index] = playerId;
            stateBindingTypes[index] = bindingType;
            statePositions[index] = position;
            stateRotations[index] = rotation;
            stateFreshness[index] = freshness;
            stateAuthors[index] = author;
            stateReceivedTimes[index] = Time.time;
            stateWaitingForHandleSync[index] = false;
            if (stateKind == StateHeld)
            {
                int handleIndex = FindHandleForObject(objectId);
                if (handleIndex >= 0 && Utilities.IsValid(handleObjects[handleIndex]))
                {
                    Transform handle = handleObjects[handleIndex].transform;
                    stateHeldHandlePositions[index] = handle.position;
                    stateHeldHandleRotations[index] = handle.rotation;
                    stateWaitingForHandleSync[index] = true;
                }
                StopLocalGrabIfHeldByAnother(handleIndex, playerId);
            }
            if (Networking.IsOwner(gameObject))
                RequestSerialization();
            TryApplyBindingsForObject(objectId);
        }

        private bool IsNewerState(int freshness, int author, int previousFreshness, int previousAuthor)
        {
            int difference = freshness - previousFreshness;
            return difference > 0 || (difference == 0 && author > previousAuthor);
        }

        private int FindState(int objectId)
        {
            for (int i = 0; i < stateCount; i++)
            {
                if (stateObjectIds[i] == objectId)
                    return i;
            }
            return -1;
        }

        private void RemoveStateAt(int index)
        {
            if (index < 0 || index >= stateCount)
                return;
            for (int i = index; i < stateCount - 1; i++)
            {
                stateObjectIds[i] = stateObjectIds[i + 1];
                stateKinds[i] = stateKinds[i + 1];
                statePlayerIds[i] = statePlayerIds[i + 1];
                stateBindingTypes[i] = stateBindingTypes[i + 1];
                statePositions[i] = statePositions[i + 1];
                stateRotations[i] = stateRotations[i + 1];
                stateFreshness[i] = stateFreshness[i + 1];
                stateAuthors[i] = stateAuthors[i + 1];
                stateLastFramePositions[i] = stateLastFramePositions[i + 1];
                stateLastFrameRotations[i] = stateLastFrameRotations[i + 1];
                stateHasLastFrame[i] = stateHasLastFrame[i + 1];
                stateReceivedTimes[i] = stateReceivedTimes[i + 1];
                stateHeldHandlePositions[i] = stateHeldHandlePositions[i + 1];
                stateHeldHandleRotations[i] = stateHeldHandleRotations[i + 1];
                stateWaitingForHandleSync[i] = stateWaitingForHandleSync[i + 1];
            }
            stateCount--;
            stateObjectIds[stateCount] = 0;
            stateHasLastFrame[stateCount] = false;
            stateWaitingForHandleSync[stateCount] = false;
        }

        private bool TryGetObjectFrame(int objectId, out Vector3 position, out Quaternion rotation)
        {
            int index = FindState(objectId);
            if (index < 0)
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                return false;
            }
            if (stateKinds[index] == StateFixed)
            {
                position = statePositions[index];
                rotation = stateRotations[index];
                return true;
            }
            if (stateKinds[index] == StateAttached)
            {
                Vector3 bonePosition;
                Quaternion boneRotation;
                if (!Utilities.IsValid(bodyQvManager) ||
                    !TryGetCachedBodyPose(statePlayerIds[index], stateBindingTypes[index],
                        out bonePosition, out boneRotation))
                {
                    if (stateHasLastFrame[index])
                    {
                        position = stateLastFramePositions[index];
                        rotation = stateLastFrameRotations[index];
                        return true;
                    }
                    position = Vector3.zero;
                    rotation = Quaternion.identity;
                    return false;
                }
                position = bonePosition + boneRotation * statePositions[index];
                rotation = boneRotation * stateRotations[index];
                return true;
            }

            int handleIndex = FindHandleForObject(objectId);
            if (handleIndex < 0 || !Utilities.IsValid(handleObjects[handleIndex]))
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                return false;
            }
            Transform handle = handleObjects[handleIndex].transform;
            if (IsHandleHeldLocally(handleIndex))
            {
                stateWaitingForHandleSync[index] = false;
                position = handle.position;
                rotation = handle.rotation;
                return true;
            }
            if (stateWaitingForHandleSync[index])
            {
                bool moved = Vector3.Distance(handle.position, stateHeldHandlePositions[index]) >=
                             HeldHandleMoveDistance ||
                             Quaternion.Angle(handle.rotation, stateHeldHandleRotations[index]) >=
                             HeldHandleMoveAngle;
                if (!moved && Time.time - stateReceivedTimes[index] < HeldHandleSyncTimeout)
                {
                    position = statePositions[index];
                    rotation = stateRotations[index];
                    return true;
                }
                stateWaitingForHandleSync[index] = false;
            }
            position = handle.position;
            rotation = handle.rotation;
            return true;
        }

        private bool TryGetCachedBodyPose(int playerId, int bindingType,
            out Vector3 position, out Quaternion rotation)
        {
            for (int i = 0; i < framePoseCount; i++)
            {
                if (framePosePlayerIds[i] != playerId || framePoseTypes[i] != bindingType)
                    continue;
                position = framePosePositions[i];
                rotation = framePoseRotations[i];
                return framePoseValid[i];
            }
            bool valid = bodyQvManager.TryGetBindingPose(playerId, bindingType, out position, out rotation);
            if (framePoseCount < MaxCombinedStates)
            {
                framePosePlayerIds[framePoseCount] = playerId;
                framePoseTypes[framePoseCount] = bindingType;
                framePosePositions[framePoseCount] = position;
                framePoseRotations[framePoseCount] = rotation;
                framePoseValid[framePoseCount] = valid;
                framePoseCount++;
            }
            return valid;
        }

        private void UpdateFramesAndGrabTargets()
        {
            for (int i = 0; i < handleObjectIds.Length; i++)
            {
                int objectId = handleObjectIds[i];
                if (objectId == 0 || !Utilities.IsValid(handleTargets[i]))
                    continue;
                Vector3 position;
                Quaternion rotation;
                int stateIndex = FindState(objectId);
                if (stateIndex >= 0)
                {
                    if (!TryGetObjectFrame(objectId, out position, out rotation))
                        continue;
                    stateLastFramePositions[stateIndex] = position;
                    stateLastFrameRotations[stateIndex] = rotation;
                    stateHasLastFrame[stateIndex] = true;
                }
                else
                {
                    if (!Utilities.IsValid(handleObjects[i]))
                        continue;
                    position = handleObjects[i].transform.position;
                    rotation = handleObjects[i].transform.rotation;
                }
                if (!IsHandleHeldLocally(i))
                    handleTargets[i].SetPositionAndRotation(position, rotation);
                handleLastFramePositions[i] = position;
                handleLastFrameRotations[i] = rotation;
                handleHasLastFrame[i] = true;
            }
        }

        private void DetectCombinedHandleChanges()
        {
            for (int i = 0; i < handleObjectIds.Length; i++)
            {
                int objectId = handleObjectIds[i];
                if (objectId == 0 || !Utilities.IsValid(handlePickups[i]))
                {
                    // 何もすることが無いフレームで書き込まないよう、残っているものがあるときだけ戻す。
                    if (handleWasLocallyHeld[i] || handlePickupPending[i] || handleDropPending[i] || handleHasLastFrame[i])
                        ResetHandleState(i);
                    continue;
                }
                VRC_Pickup pickup = handlePickups[i];
                VRCPlayerApi player = pickup.currentPlayer;
                bool heldLocally = pickup.IsHeld && Utilities.IsValid(player) && player.isLocal;
                if (heldLocally && handleWasLocallyHeld[i] && Utilities.IsValid(handleObjects[i]) &&
                    !Networking.IsOwner(handleObjects[i]))
                {
                    pickup.Drop();
                    ResetHandleState(i);
                    continue;
                }
                if (heldLocally)
                {
                    if (pickup.currentHand == VRC_Pickup.PickupHand.Left)
                        handleHeldHandMasks[i] = BodyQvManager.LeftHandMask;
                    else if (pickup.currentHand == VRC_Pickup.PickupHand.Right)
                        handleHeldHandMasks[i] = BodyQvManager.RightHandMask;
                }
                if (heldLocally && !handleWasLocallyHeld[i])
                    handlePickupPending[i] = true;
                else if (!heldLocally && handleWasLocallyHeld[i])
                    handleDropPending[i] = true;
                handleWasLocallyHeld[i] = heldLocally;
            }
        }

        private void ProcessHandleChanges()
        {
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            for (int i = 0; i < handleObjectIds.Length; i++)
            {
                int objectId = handleObjectIds[i];
                if (objectId == 0)
                    continue;
                if (handlePickupPending[i])
                {
                    handlePickupPending[i] = false;
                    Vector3 framePosition;
                    Quaternion frameRotation;
                    int stateIndex = FindState(objectId);
                    if (stateIndex >= 0 && stateHasLastFrame[stateIndex])
                    {
                        framePosition = stateLastFramePositions[stateIndex];
                        frameRotation = stateLastFrameRotations[stateIndex];
                    }
                    else if (!TryGetObjectFrame(objectId, out framePosition, out frameRotation))
                    {
                        if (!Utilities.IsValid(handleObjects[i]))
                            continue;
                        framePosition = handleObjects[i].transform.position;
                        frameRotation = handleObjects[i].transform.rotation;
                    }
                    if (Utilities.IsValid(localPlayer) && Utilities.IsValid(handleObjects[i]))
                        Networking.SetOwner(localPlayer, handleObjects[i]);
                    MoveHandle(i, framePosition, frameRotation);
                    if (stateIndex >= 0)
                        BroadcastState(objectId, StateHeld,
                            Utilities.IsValid(localPlayer) ? localPlayer.playerId : 0, -1,
                            framePosition, frameRotation);
                    Log(GrabQvStrings.HeldLog + objectId);
                }
                if (!handleDropPending[i])
                    continue;
                handleDropPending[i] = false;
                VRCPlayerApi currentPlayer = handlePickups[i].currentPlayer;
                if (handlePickups[i].IsHeld && Utilities.IsValid(currentPlayer) && !currentPlayer.isLocal)
                    continue;
                Vector3 droppedPosition;
                Quaternion droppedRotation;
                if (!Utilities.IsValid(handleObjects[i]))
                    continue;
                droppedPosition = handleObjects[i].transform.position;
                droppedRotation = handleObjects[i].transform.rotation;
                if (FindState(objectId) >= 0)
                    DetermineAndBroadcastState(objectId, null, droppedPosition, droppedRotation,
                        handleHeldHandMasks[i], false);
                Log(GrabQvStrings.DroppedLog + objectId);
            }
        }

        private void UpdateLocallyHeldHandles()
        {
            for (int i = 0; i < handleObjectIds.Length; i++)
            {
                if (handleObjectIds[i] == 0 || !IsHandleHeldLocally(i) ||
                    !Utilities.IsValid(handleObjects[i]) || !Utilities.IsValid(handleTargets[i]) ||
                    !Networking.IsOwner(handleObjects[i]))
                    continue;
                handleObjects[i].transform.SetPositionAndRotation(handleTargets[i].position,
                    handleTargets[i].rotation);
            }
        }

        private bool IsHandleHeldLocally(int index)
        {
            if (index < 0 || index >= handlePickups.Length || !Utilities.IsValid(handlePickups[index]))
                return false;
            VRCPlayerApi player = handlePickups[index].currentPlayer;
            return handlePickups[index].IsHeld && Utilities.IsValid(player) && player.isLocal;
        }

        private void StopLocalGrabIfHeldByAnother(int handleIndex, int playerId)
        {
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (handleIndex < 0 || handleIndex >= handlePickups.Length ||
                !Utilities.IsValid(localPlayer) || playerId == localPlayer.playerId ||
                !Utilities.IsValid(handlePickups[handleIndex]) || !IsHandleHeldLocally(handleIndex))
                return;
            handlePickups[handleIndex].Drop();
            ResetHandleState(handleIndex);
        }

        private bool IsCombinedPen(int penIndex)
        {
            return penIndex >= 0 && penIndex < combinedPens.Length && combinedPens[penIndex];
        }

        private bool IsCombinedBinding(int bindingIndex)
        {
            return bindingIndex >= 0 && bindingIndex < bindingCount &&
                   IsCombinedPen(bindingPenIndexes[bindingIndex]);
        }

        private void LogCombinedResult(int objectId, string reason, bool attached,
            int playerId, int bindingType, float distance, string method)
        {
            if (!logResults)
                return;
            if (attached)
            {
                Log(GrabQvStrings.BindingResultPrefix + objectId + " / " + reason +
                    " / playerId " + playerId + " / " + bodyQvManager.GetBindingTypeName(bindingType) +
                    " / " + distance + " m / " + method);
                return;
            }
            string nearest = playerId > 0 && bindingType >= 0
                ? " / 最寄り: playerId " + playerId + " / " + bodyQvManager.GetBindingTypeName(bindingType) +
                  " / " + distance + " m"
                : " / 最寄り: なし";
            Log(GrabQvStrings.BindingResultPrefix + objectId + " / " + reason + " / 付かない" + nearest);
        }

        private bool ShouldAutoSplit(int objectId, LineRenderer line)
        {
            if (autoSplitDistance <= 0f)
                return false;
            int handleIndex = FindHandleForObject(objectId);
            if (handleIndex < 0 || !Utilities.IsValid(handleColliders[handleIndex]) || !handleColliders[handleIndex].enabled)
                return false;
            BoxCollider box = handleColliders[handleIndex];
            int pointCount = line.positionCount;
            for (int i = 0; i < pointCount; i++)
            {
                Vector3 point = GetLineWorldPoint(line, i);
                if (Vector3.Distance(point, box.ClosestPoint(point)) < autoSplitDistance)
                    return false;
            }
            return pointCount > 0;
        }

        private void UpdateHandleBounds(int objectId)
        {
            int handleIndex = FindHandleForObject(objectId);
            if (handleIndex < 0 || !Utilities.IsValid(handleTargets[handleIndex]) ||
                !Utilities.IsValid(handleColliders[handleIndex]))
                return;
            int stateIndex = FindState(objectId);
            Vector3 framePosition;
            Quaternion frameRotation;
            if (stateIndex >= 0)
            {
                if (!TryGetObjectFrame(objectId, out framePosition, out frameRotation))
                    return;
            }
            else
            {
                if (!Utilities.IsValid(handleObjects[handleIndex]))
                    return;
                framePosition = handleObjects[handleIndex].transform.position;
                frameRotation = handleObjects[handleIndex].transform.rotation;
            }
            if (!IsHandleHeldLocally(handleIndex))
                handleTargets[handleIndex].SetPositionAndRotation(framePosition, frameRotation);
            handleLastFramePositions[handleIndex] = framePosition;
            handleLastFrameRotations[handleIndex] = frameRotation;
            handleHasLastFrame[handleIndex] = true;
            Transform handle = handleTargets[handleIndex];
            Vector3 minimum = Vector3.zero;
            Vector3 maximum = Vector3.zero;
            bool found = false;
            for (int i = 0; i < bindingCount; i++)
            {
                if (bindingObjectIds[i] != objectId || !Utilities.IsValid(bindingLines[i]))
                    continue;
                LineRenderer line = bindingLines[i];
                for (int pointIndex = 0; pointIndex < line.positionCount; pointIndex++)
                {
                    Vector3 local = handle.InverseTransformPoint(GetLineWorldPoint(line, pointIndex));
                    if (!found)
                    {
                        minimum = local;
                        maximum = local;
                        found = true;
                    }
                    else
                    {
                        minimum = Vector3.Min(minimum, local);
                        maximum = Vector3.Max(maximum, local);
                    }
                }
            }
            if (!found)
                return;
            BoxCollider box = handleColliders[handleIndex];
            box.center = (minimum + maximum) * 0.5f;
            Vector3 size = maximum - minimum + Vector3.one * (BoundsPadding * 2f);
            box.size = new Vector3(Mathf.Max(MinimumBoundsSize, size.x),
                Mathf.Max(MinimumBoundsSize, size.y), Mathf.Max(MinimumBoundsSize, size.z));
            box.enabled = true;
        }

        private Vector3 GetLineWorldPoint(LineRenderer line, int index)
        {
            Vector3 point = line.GetPosition(index);
            return line.useWorldSpace ? point : line.transform.TransformPoint(point);
        }

        private Vector3 GetLineBoundsCenter(LineRenderer line)
        {
            Vector3 minimum = GetLineWorldPoint(line, 0);
            Vector3 maximum = minimum;
            for (int i = 1; i < line.positionCount; i++)
            {
                Vector3 point = GetLineWorldPoint(line, i);
                minimum = Vector3.Min(minimum, point);
                maximum = Vector3.Max(maximum, point);
            }
            return (minimum + maximum) * 0.5f;
        }

        private void CleanupHandles()
        {
            bool changed = false;
            for (int i = 0; i < handleObjectIds.Length; i++)
            {
                int objectId = handleObjectIds[i];
                if (objectId == 0)
                    continue;
                bool hasInk = HasLiveInk(objectId);
                if (hasInk)
                {
                    handleSawInk[i] = true;
                    handleEmptySince[i] = -1f;
                    continue;
                }
                if (!handleSawInk[i])
                {
                    if (Time.time - handleAssignedTimes[i] < NeverArrivedReleaseDelay)
                        continue;
                }
                else
                {
                    if (handleEmptySince[i] < 0f)
                    {
                        handleEmptySince[i] = Time.time;
                        continue;
                    }
                    if (Time.time - handleEmptySince[i] < EmptyReleaseDelay)
                        continue;
                }
                ReleaseHandle(i);
                changed = true;
            }
            int stateIndex = 0;
            while (stateIndex < stateCount)
            {
                int objectId = stateObjectIds[stateIndex];
                if (HasLiveInk(objectId) || Time.time - stateReceivedTimes[stateIndex] <= BindingArrivalGracePeriod)
                {
                    stateIndex++;
                    continue;
                }
                BroadcastState(objectId, StateNone, 0, -1, Vector3.zero, Quaternion.identity);
                changed = true;
                if (stateIndex < stateCount && stateObjectIds[stateIndex] == objectId)
                    stateIndex++;
            }
            if (changed)
                RequestSerialization();
        }

        private bool HasLiveInk(int objectId)
        {
            for (int i = 0; i < bindingCount; i++)
            {
                if (bindingObjectIds[i] != objectId)
                    continue;
                if (Utilities.IsValid(bindingLines[i]) ||
                    (!bindingSawInk[i] && Time.time - bindingReceivedTimes[i] <= BindingArrivalGracePeriod))
                    return true;
            }
            return false;
        }

        private void ReleaseHandle(int index)
        {
            int objectId = handleObjectIds[index];
            ClearCurrentObject(objectId);
            SetHandleAvailable(index, false);
            ResetHandleState(index);
            handleObjectIds[index] = 0;
            handleAssignedTimes[index] = 0f;
            handleEmptySince[index] = -1f;
            handleSawInk[index] = false;
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (Utilities.IsValid(localPlayer) && Utilities.IsValid(handleObjects[index]))
                Networking.SetOwner(localPlayer, handleObjects[index]);
            MoveHandle(index, handleHomePositions[index], handleHomeRotations[index]);
            MoveHandleTargetHome(index);
            if (FindState(objectId) >= 0)
                BroadcastState(objectId, StateNone, 0, -1, Vector3.zero, Quaternion.identity);
            Log(GrabQvStrings.ReleasedLog + objectId);
        }

        private bool ClearCurrentObject(int objectId)
        {
            bool changed = false;
            for (int penIndex = 0; penIndex < currentObjectIds.Length; penIndex++)
            {
                if (currentObjectIds[penIndex] != objectId)
                    continue;
                currentObjectIds[penIndex] = 0;
                changed = true;
                Log(GrabQvStrings.CurrentObjectClearedLog + objectId + GrabQvStrings.PenLabel + penIndex);
            }
            return changed;
        }

        private void InitializeHandles()
        {
            int length = handleObjects == null ? 0 : handleObjects.Length;
            handleObjectIds = new int[length];
            handleAssignedTimes = new float[length];
            handleEmptySince = new float[length];
            handleSawInk = new bool[length];
            handleWasLocallyHeld = new bool[length];
            handlePickupPending = new bool[length];
            handleDropPending = new bool[length];
            handleHeldHandMasks = new int[length];
            handleLastFramePositions = new Vector3[length];
            handleLastFrameRotations = new Quaternion[length];
            handleHasLastFrame = new bool[length];
            handleHomePositions = new Vector3[length];
            handleHomeRotations = new Quaternion[length];
            handleTargetHomePositions = new Vector3[length];
            handleTargetHomeRotations = new Quaternion[length];
            for (int i = 0; i < length; i++)
            {
                if (!Utilities.IsValid(handleObjects[i]))
                    continue;
                handleHomePositions[i] = handleObjects[i].transform.position;
                handleHomeRotations[i] = handleObjects[i].transform.rotation;
                if (Utilities.IsValid(handleTargets[i]))
                {
                    handleTargetHomePositions[i] = handleTargets[i].position;
                    handleTargetHomeRotations[i] = handleTargets[i].rotation;
                }
                handleEmptySince[i] = -1f;
                SetHandleAvailable(i, false);
            }
        }

        private void ResetHandleState(int index)
        {
            if (index < 0 || index >= handleObjectIds.Length)
                return;
            handleWasLocallyHeld[index] = false;
            handleHeldHandMasks[index] = 0;
            handlePickupPending[index] = false;
            handleDropPending[index] = false;
            handleHasLastFrame[index] = false;
            handleLastFramePositions[index] = Vector3.zero;
            handleLastFrameRotations[index] = Quaternion.identity;
        }

        private void SetHandleAvailable(int index, bool assigned)
        {
            if (index < 0 || index >= handleObjects.Length)
                return;
            if (Utilities.IsValid(handlePickups[index]))
            {
                if (!assigned && handlePickups[index].IsHeld)
                    handlePickups[index].Drop();
                handlePickups[index].pickupable = assigned;
            }
            if (Utilities.IsValid(handleColliders[index]))
                handleColliders[index].enabled = assigned && HasLiveInk(handleObjectIds[index]);
        }

        private void MoveHandle(int index, Vector3 position, Quaternion rotation)
        {
            if (!Utilities.IsValid(handleObjects[index]))
                return;
            handleObjects[index].transform.SetPositionAndRotation(position, rotation);
            if (Utilities.IsValid(handleSyncs[index]))
                handleSyncs[index].FlagDiscontinuity();
        }

        private void MoveHandleTargetHome(int index)
        {
            if (index < 0 || index >= handleTargets.Length || !Utilities.IsValid(handleTargets[index]))
                return;
            handleTargets[index].SetPositionAndRotation(handleTargetHomePositions[index],
                handleTargetHomeRotations[index]);
        }

        private bool HasAssignedHandle()
        {
            for (int i = 0; i < handleObjectIds.Length; i++)
            {
                if (handleObjectIds[i] != 0)
                    return true;
            }
            return false;
        }

        private int FindFreeHandle()
        {
            for (int i = 0; i < handleObjectIds.Length; i++)
            {
                if (handleObjectIds[i] == 0 && Utilities.IsValid(handleObjects[i]))
                    return i;
            }
            return -1;
        }

        private int FindHandleForObject(int objectId)
        {
            for (int i = 0; i < handleObjectIds.Length; i++)
            {
                if (handleObjectIds[i] == objectId)
                    return i;
            }
            return -1;
        }

        private void RememberObjectReference(int objectId, Vector3 position)
        {
            for (int i = 0; i < objectReferenceCount; i++)
            {
                if (objectReferenceIds[i] != objectId)
                    continue;
                objectReferencePositions[i] = position;
                return;
            }
            if (objectReferenceCount >= MaxBindings)
                return;
            objectReferenceIds[objectReferenceCount] = objectId;
            objectReferencePositions[objectReferenceCount] = position;
            objectReferenceCount++;
        }

        private bool TryGetObjectReference(int objectId, out Vector3 position)
        {
            for (int i = 0; i < objectReferenceCount; i++)
            {
                if (objectReferenceIds[i] == objectId)
                {
                    position = objectReferencePositions[i];
                    return true;
                }
            }
            position = Vector3.zero;
            return false;
        }

        private int FindPendingRequest(int objectId)
        {
            for (int i = 0; i < pendingRequestCount; i++)
            {
                if (pendingRequestObjectIds[i] == objectId)
                    return i;
            }
            return -1;
        }

        private void RemovePendingRequest(int objectId)
        {
            int index = FindPendingRequest(objectId);
            if (index >= 0)
                RemovePendingRequestAt(index);
        }

        private void RemovePendingRequestAt(int index)
        {
            for (int i = index; i < pendingRequestCount - 1; i++)
            {
                pendingRequestObjectIds[i] = pendingRequestObjectIds[i + 1];
                pendingRequestPositions[i] = pendingRequestPositions[i + 1];
                pendingRequestTimes[i] = pendingRequestTimes[i + 1];
            }
            pendingRequestCount--;
        }

        private int FindBinding(int penId, int inkId)
        {
            for (int i = 0; i < bindingCount; i++)
            {
                if (bindingPenIds[i] == penId && bindingInkIds[i] == inkId)
                    return i;
            }
            return -1;
        }

        private void RemoveBindingAt(int index)
        {
            if (index < 0 || index >= bindingCount)
                return;
            for (int i = index; i < bindingCount - 1; i++)
            {
                bindingPenIds[i] = bindingPenIds[i + 1];
                bindingInkIds[i] = bindingInkIds[i + 1];
                bindingObjectIds[i] = bindingObjectIds[i + 1];
                bindingPenIndexes[i] = bindingPenIndexes[i + 1];
                bindingHandlePositions[i] = bindingHandlePositions[i + 1];
                bindingHandleRotations[i] = bindingHandleRotations[i + 1];
                bindingLines[i] = bindingLines[i + 1];
                bindingSawInk[i] = bindingSawInk[i + 1];
                bindingWasApplied[i] = bindingWasApplied[i + 1];
                bindingReceivedTimes[i] = bindingReceivedTimes[i + 1];
            }
            bindingCount--;
            bindingPenIndexes[bindingCount] = 0;
            bindingLines[bindingCount] = null;
            bindingSawInk[bindingCount] = false;
            bindingWasApplied[bindingCount] = false;
            bindingReceivedTimes[bindingCount] = 0f;
        }

        private int FindKnownInk(int penId, int inkId)
        {
            for (int i = 0; i < knownInkCount; i++)
            {
                if (knownPenIds[i] == penId && knownInkIds[i] == inkId)
                    return i;
            }
            return -1;
        }

        private LineRenderer FindKnownLine(int penId, int inkId)
        {
            int index = FindKnownInk(penId, inkId);
            return index >= 0 ? knownLines[index] : null;
        }

        private void RememberInk(int penId, int inkId, LineRenderer line)
        {
            if (knownInkCount >= MaxKnownInks)
            {
                for (int i = 0; i < MaxKnownInks - 1; i++)
                {
                    knownPenIds[i] = knownPenIds[i + 1];
                    knownInkIds[i] = knownInkIds[i + 1];
                    knownLines[i] = knownLines[i + 1];
                }
                knownInkCount--;
            }
            knownPenIds[knownInkCount] = penId;
            knownInkIds[knownInkCount] = inkId;
            knownLines[knownInkCount] = line;
            knownInkCount++;
        }

        private bool IsInkTransformIdentity(Transform inkTransform)
        {
            return inkTransform.localPosition.sqrMagnitude <= IdentityTolerance &&
                   Quaternion.Angle(inkTransform.localRotation, Quaternion.identity) <= IdentityTolerance &&
                   (inkTransform.localScale - Vector3.one).sqrMagnitude <= IdentityTolerance;
        }

        private bool IsValidObjectId(int objectId)
        {
            return objectId > 0 && objectId <= 2000999999;
        }

        private bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private bool IsValidRotation(Quaternion rotation)
        {
            if (!IsFinite(rotation.x) || !IsFinite(rotation.y) || !IsFinite(rotation.z) || !IsFinite(rotation.w))
                return false;
            float magnitude = rotation.x * rotation.x + rotation.y * rotation.y +
                              rotation.z * rotation.z + rotation.w * rotation.w;
            return magnitude > 0.5f && magnitude < 1.5f;
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (Utilities.IsValid(player) && !player.isLocal && Networking.IsOwner(gameObject))
                RequestSerialization();
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (Utilities.IsValid(player) && player.isLocal)
                RequestSerialization();
        }

        public override void OnPreSerialization()
        {
            syncedHandleObjectIds = new int[handleObjectIds.Length];
            for (int i = 0; i < handleObjectIds.Length; i++)
                syncedHandleObjectIds[i] = handleObjectIds[i];

            syncedCurrentObjectIds = new int[currentObjectIds.Length];
            for (int i = 0; i < currentObjectIds.Length; i++)
                syncedCurrentObjectIds[i] = currentObjectIds[i];

            syncedBindingPenIds = new int[bindingCount];
            syncedBindingInkIds = new int[bindingCount];
            syncedBindingObjectIds = new int[bindingCount];
            syncedBindingPenIndexes = new int[bindingCount];
            syncedBindingHandlePositions = new Vector3[bindingCount];
            syncedBindingHandleRotations = new Quaternion[bindingCount];
            for (int i = 0; i < bindingCount; i++)
            {
                syncedBindingPenIds[i] = bindingPenIds[i];
                syncedBindingInkIds[i] = bindingInkIds[i];
                syncedBindingObjectIds[i] = bindingObjectIds[i];
                syncedBindingPenIndexes[i] = bindingPenIndexes[i];
                syncedBindingHandlePositions[i] = bindingHandlePositions[i];
                syncedBindingHandleRotations[i] = bindingHandleRotations[i];
            }

            syncedStateObjectIds = new int[stateCount];
            syncedStateKinds = new int[stateCount];
            syncedStatePlayerIds = new int[stateCount];
            syncedStateBindingTypes = new int[stateCount];
            syncedStatePositions = new Vector3[stateCount];
            syncedStateRotations = new Quaternion[stateCount];
            syncedStateFreshness = new int[stateCount];
            syncedStateAuthors = new int[stateCount];
            for (int i = 0; i < stateCount; i++)
            {
                syncedStateObjectIds[i] = stateObjectIds[i];
                syncedStateKinds[i] = stateKinds[i];
                syncedStatePlayerIds[i] = statePlayerIds[i];
                syncedStateBindingTypes[i] = stateBindingTypes[i];
                syncedStatePositions[i] = statePositions[i];
                syncedStateRotations[i] = stateRotations[i];
                syncedStateFreshness[i] = stateFreshness[i];
                syncedStateAuthors[i] = stateAuthors[i];
            }
        }

        public override void OnDeserialization()
        {
            if (syncedCurrentObjectIds != null && syncedCurrentObjectIds.Length == currentObjectIds.Length)
            {
                for (int i = 0; i < currentObjectIds.Length; i++)
                {
                    if (syncedCurrentObjectIds[i] == 0 || IsValidObjectId(syncedCurrentObjectIds[i]))
                        currentObjectIds[i] = syncedCurrentObjectIds[i];
                }
            }
            ApplySyncedHandles();
            ApplySyncedStates();

            if (syncedBindingPenIds == null || syncedBindingInkIds == null || syncedBindingObjectIds == null ||
                syncedBindingPenIndexes == null || syncedBindingHandlePositions == null ||
                syncedBindingHandleRotations == null)
                return;
            int length = syncedBindingPenIds.Length;
            if (length > MaxBindings || syncedBindingInkIds.Length != length || syncedBindingObjectIds.Length != length ||
                syncedBindingPenIndexes.Length != length ||
                syncedBindingHandlePositions.Length != length || syncedBindingHandleRotations.Length != length)
                return;
            for (int i = 0; i < length; i++)
            {
                if (!IsValidObjectId(syncedBindingObjectIds[i]) || syncedBindingPenIndexes[i] < 0 ||
                    syncedBindingPenIndexes[i] >= targetedPens.Length || !IsFinite(syncedBindingHandlePositions[i]) ||
                    !IsValidRotation(syncedBindingHandleRotations[i]))
                    continue;
                AddOrMergeBinding(syncedBindingPenIds[i], syncedBindingInkIds[i], syncedBindingObjectIds[i],
                    syncedBindingPenIndexes[i],
                    syncedBindingHandlePositions[i], syncedBindingHandleRotations[i]);
            }
        }

        private void ApplySyncedHandles()
        {
            if (syncedHandleObjectIds == null || syncedHandleObjectIds.Length != handleObjectIds.Length)
                return;
            for (int i = 0; i < handleObjectIds.Length; i++)
            {
                int objectId = syncedHandleObjectIds[i];
                if (objectId != 0 && !IsValidObjectId(objectId))
                    continue;
                int previousObjectId = handleObjectIds[i];
                bool changed = previousObjectId != objectId;
                if (!changed)
                    continue;
                ResetHandleState(i);
                handleObjectIds[i] = objectId;
                if (objectId == 0)
                {
                    ClearCurrentObject(previousObjectId);
                    SetHandleAvailable(i, false);
                    MoveHandleTargetHome(i);
                    handleAssignedTimes[i] = 0f;
                    handleEmptySince[i] = -1f;
                    handleSawInk[i] = false;
                }
                else
                {
                    handleAssignedTimes[i] = Time.time;
                    handleEmptySince[i] = -1f;
                    handleSawInk[i] = HasLiveInk(objectId);
                    SetHandleAvailable(i, true);
                    if (Utilities.IsValid(handleObjects[i]) && Utilities.IsValid(handleTargets[i]))
                        handleTargets[i].SetPositionAndRotation(handleObjects[i].transform.position,
                            handleObjects[i].transform.rotation);
                    RemovePendingRequest(objectId);
                    TryApplyBindingsForObject(objectId);
                }
            }
        }

        private void ApplySyncedStates()
        {
            if (syncedStateObjectIds == null || syncedStateKinds == null || syncedStatePlayerIds == null ||
                syncedStateBindingTypes == null || syncedStatePositions == null || syncedStateRotations == null ||
                syncedStateFreshness == null || syncedStateAuthors == null)
                return;
            int length = syncedStateObjectIds.Length;
            if (length > MaxCombinedStates || syncedStateKinds.Length != length ||
                syncedStatePlayerIds.Length != length || syncedStateBindingTypes.Length != length ||
                syncedStatePositions.Length != length || syncedStateRotations.Length != length ||
                syncedStateFreshness.Length != length || syncedStateAuthors.Length != length)
                return;
            for (int i = 0; i < length; i++)
            {
                ReceiveCombinedState(syncedStateObjectIds[i], syncedStateKinds[i],
                    syncedStatePlayerIds[i], syncedStateBindingTypes[i], syncedStatePositions[i],
                    syncedStateRotations[i], syncedStateFreshness[i], syncedStateAuthors[i]);
            }
        }

        private void TryApplyBindingsForObject(int objectId)
        {
            for (int i = 0; i < bindingCount; i++)
            {
                if (bindingObjectIds[i] == objectId && !bindingWasApplied[i])
                    TryApplyBindingAt(i, Utilities.IsValid(bindingLines[i]) ? bindingLines[i] :
                        FindKnownLine(bindingPenIds[i], bindingInkIds[i]));
            }
            UpdateHandleBounds(objectId);
        }

        private int FindTargetPen(QvPen_PenManager pen)
        {
            if (!Utilities.IsValid(pen))
                return -1;
            for (int i = 0; i < targetedPens.Length; i++)
            {
                if (targetedPens[i] == pen)
                    return i;
            }
            return -1;
        }

        private void InitializeCombinedPens()
        {
            int length = targetedPens == null ? 0 : targetedPens.Length;
            combinedPens = new bool[length];
            if (!Utilities.IsValid(bodyQvManager) || !bodyQvManager.gameObject.activeInHierarchy)
                return;
            QvPen_PenManager[] bodyPens = bodyQvManager.TargetedPens;
            for (int i = 0; i < length; i++)
            {
                QvPen_PenManager pen = targetedPens[i];
                for (int j = 0; bodyPens != null && j < bodyPens.Length; j++)
                {
                    if (pen != null && bodyPens[j] == pen)
                    {
                        combinedPens[i] = true;
                        break;
                    }
                }
            }
        }

        private bool ResolveTargetReferences()
        {
            bool changed = false;
            int length = targetedPens == null ? 0 : targetedPens.Length;
            if (targetLateSyncs == null || targetLateSyncs.Length != length)
            {
                targetLateSyncs = new QvPen_LateSync[length];
                changed = true;
            }
            if (targetPickups == null || targetPickups.Length != length)
            {
                targetPickups = new VRC_Pickup[length];
                changed = true;
            }
            for (int i = 0; i < length; i++)
            {
                QvPen_PenManager pen = targetedPens[i];
                if (!Utilities.IsValid(pen))
                {
                    if (Utilities.IsValid(targetLateSyncs[i]))
                    {
                        targetLateSyncs[i] = null;
                        changed = true;
                    }
                    if (Utilities.IsValid(targetPickups[i]))
                    {
                        targetPickups[i] = null;
                        changed = true;
                    }
                    continue;
                }
                QvPen_LateSync lateSync = pen.GetComponentInChildren<QvPen_LateSync>(true);
                VRC_Pickup pickup = pen.GetComponentInChildren<VRC_Pickup>(true);
                if (targetLateSyncs[i] != lateSync)
                {
                    targetLateSyncs[i] = lateSync;
                    changed = true;
                }
                if (targetPickups[i] != pickup)
                {
                    targetPickups[i] = pickup;
                    changed = true;
                }
            }
            return changed;
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !Networking.IsOwner(gameObject))
                return;
            int playerId = player.playerId;
            for (int i = 0; i < stateCount; i++)
            {
                if ((stateKinds[i] != StateAttached && stateKinds[i] != StateHeld) ||
                    statePlayerIds[i] != playerId)
                    continue;
                Vector3 position = stateHasLastFrame[i] ? stateLastFramePositions[i] : statePositions[i];
                Quaternion rotation = stateHasLastFrame[i] ? stateLastFrameRotations[i] : stateRotations[i];
                int objectId = stateObjectIds[i];
                BroadcastState(objectId, StateFixed, 0, -1, position, rotation);
                Log(GrabQvStrings.PlayerLeftLog + objectId);
            }
        }

        private void Log(string message)
        {
            if (logResults)
                Debug.Log(message);
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        private void OnValidate()
        {
            if (autoSplitDistance < 0f)
                autoSplitDistance = 0f;
            ResolveTargetReferences();
            RefreshBodyQvManagerReference();
            RefreshHandleAutoHold();
        }

        public bool RefreshTargetReferences()
        {
            return ResolveTargetReferences();
        }

        public int RefreshBodyQvManagerReference()
        {
            BodyQvManager[] managers = Resources.FindObjectsOfTypeAll<BodyQvManager>();
            BodyQvManager found = null;
            int count = 0;
            for (int i = 0; i < managers.Length; i++)
            {
                BodyQvManager candidate = managers[i];
                if (candidate == null || candidate.gameObject.scene != gameObject.scene ||
                    !SharesTargetPen(candidate))
                    continue;
                count++;
                found = candidate;
            }
            bodyQvManager = count == 1 ? found : null;
            return count;
        }

        public bool RefreshHandleAutoHold()
        {
            bool changed = false;
            VRC_Pickup.AutoHoldMode expected = toggleGrab
                ? VRC_Pickup.AutoHoldMode.Yes
                : VRC_Pickup.AutoHoldMode.No;
            for (int i = 0; handlePickups != null && i < handlePickups.Length; i++)
            {
                VRC_Pickup pickup = handlePickups[i];
                if (pickup == null || pickup.AutoHold == expected)
                    continue;
                pickup.AutoHold = expected;
                UnityEditor.EditorUtility.SetDirty(pickup);
                changed = true;
            }
            return changed;
        }

        private bool SharesTargetPen(BodyQvManager manager)
        {
            QvPen_PenManager[] bodyPens = manager.TargetedPens;
            for (int i = 0; targetedPens != null && i < targetedPens.Length; i++)
            {
                for (int j = 0; bodyPens != null && j < bodyPens.Length; j++)
                {
                    if (targetedPens[i] != null && targetedPens[i] == bodyPens[j])
                        return true;
                }
            }
            return false;
        }
#endif
    }
}
