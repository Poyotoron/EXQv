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
        private const float BoundsPadding = 0.03f;
        private const float MinimumBoundsSize = 0.05f;
        private const int ObjectSequenceRange = 1000000;

        [Header(GrabQvStrings.TargetPensHeader)]
        [SerializeField, Tooltip(GrabQvStrings.TargetPensTooltip)]
        private QvPen_PenManager[] targetedPens = new QvPen_PenManager[0];

        [SerializeField, HideInInspector]
        private QvPen_LateSync[] targetLateSyncs = new QvPen_LateSync[0];

        [SerializeField, HideInInspector]
        private VRC_Pickup[] targetPickups = new VRC_Pickup[0];

        [SerializeField, InspectorName(GrabQvStrings.AutoSplitDistanceLabel),
         Tooltip(GrabQvStrings.AutoSplitDistanceTooltip)]
        private float autoSplitDistance = 2f;

        [SerializeField, InspectorName(GrabQvStrings.LogResultsLabel)]
        private bool logResults;

        [SerializeField, HideInInspector]
        private GameObject[] handleObjects = new GameObject[0];

        [SerializeField, HideInInspector]
        private VRC_Pickup[] handlePickups = new VRC_Pickup[0];

        [SerializeField, HideInInspector]
        private BoxCollider[] handleColliders = new BoxCollider[0];

        [SerializeField, HideInInspector]
        private VRCObjectSync[] handleSyncs = new VRCObjectSync[0];

        private Vector3[] handleHomePositions = new Vector3[0];
        private Quaternion[] handleHomeRotations = new Quaternion[0];
        private int[] handleObjectIds = new int[0];
        private float[] handleAssignedTimes = new float[0];
        private float[] handleEmptySince = new float[0];
        private bool[] handleSawInk = new bool[0];

        private int[] currentObjectIds = new int[0];
        private int localObjectSequence;

        private int[] objectReferenceIds = new int[MaxBindings];
        private Vector3[] objectReferencePositions = new Vector3[MaxBindings];
        private int objectReferenceCount;

        private int[] bindingPenIds = new int[MaxBindings];
        private int[] bindingInkIds = new int[MaxBindings];
        private int[] bindingObjectIds = new int[MaxBindings];
        private Vector3[] bindingHandlePositions = new Vector3[MaxBindings];
        private Quaternion[] bindingHandleRotations = new Quaternion[MaxBindings];
        private LineRenderer[] bindingLines = new LineRenderer[MaxBindings];
        private bool[] bindingSawInk = new bool[MaxBindings];
        private bool[] bindingWasApplied = new bool[MaxBindings];
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
        private LineRenderer[] pendingLines = new LineRenderer[MaxPendingInks];
        private int pendingInkCount;

        private int[] pendingRequestObjectIds = new int[MaxPendingRequests];
        private Vector3[] pendingRequestPositions = new Vector3[MaxPendingRequests];
        private float[] pendingRequestTimes = new float[MaxPendingRequests];
        private int pendingRequestCount;

        private float nextHandleCleanupTime;

        [UdonSynced] private int[] syncedHandleObjectIds = new int[0];
        [UdonSynced] private int[] syncedCurrentObjectIds = new int[0];
        [UdonSynced] private int[] syncedBindingPenIds = new int[0];
        [UdonSynced] private int[] syncedBindingInkIds = new int[0];
        [UdonSynced] private int[] syncedBindingObjectIds = new int[0];
        [UdonSynced] private Vector3[] syncedBindingHandlePositions = new Vector3[0];
        [UdonSynced] private Quaternion[] syncedBindingHandleRotations = new Quaternion[0];

        public QvPen_PenManager[] TargetedPens => targetedPens;
        public QvPen_LateSync[] TargetLateSyncs => targetLateSyncs;
        public VRC_Pickup[] TargetPickups => targetPickups;
        public GameObject[] HandleObjects => handleObjects;

        private void Start()
        {
            ResolveTargetReferences();
            InitializeHandles();
            currentObjectIds = new int[targetedPens.Length];
            poolChildCounts = new int[targetLateSyncs.Length * 2];
            poolLastChildren = new Transform[targetLateSyncs.Length * 2];
            for (int i = 0; i < poolChildCounts.Length; i++)
                poolChildCounts[i] = -1;
            nextFullPoolScanTime = Time.time + FullPoolScanInterval;
            nextHandleCleanupTime = Time.time + 0.5f;
        }

        private void Update()
        {
            ScanInkPools();
            RetryHandleRequests();
            if (Networking.IsOwner(gameObject) && Time.time >= nextHandleCleanupTime)
            {
                nextHandleCleanupTime = Time.time + 0.5f;
                CleanupHandles();
            }
        }

        public override void PostLateUpdate()
        {
            ProcessPendingInks();
            if (bindingCount == 0 || !HasAssignedHandle())
                return;

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

                int handleIndex = FindHandleForObject(bindingObjectIds[index]);
                if (handleIndex < 0 || !Utilities.IsValid(handleObjects[handleIndex]))
                {
                    index++;
                    continue;
                }

                Transform handle = handleObjects[handleIndex].transform;
                Quaternion deltaRotation = handle.rotation * Quaternion.Inverse(bindingHandleRotations[index]);
                Vector3 worldPosition = deltaRotation * (-bindingHandlePositions[index]) + handle.position;
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
            EnqueueInk(penId, inkId, penIndex, line);
        }

        private void EnqueueInk(int penId, int inkId, int penIndex, LineRenderer line)
        {
            if (pendingInkCount >= MaxPendingInks)
            {
                for (int i = 0; i < MaxPendingInks - 1; i++)
                {
                    pendingPenIds[i] = pendingPenIds[i + 1];
                    pendingInkIds[i] = pendingInkIds[i + 1];
                    pendingPenIndexes[i] = pendingPenIndexes[i + 1];
                    pendingLines[i] = pendingLines[i + 1];
                }
                pendingInkCount--;
            }
            pendingPenIds[pendingInkCount] = penId;
            pendingInkIds[pendingInkCount] = inkId;
            pendingPenIndexes[pendingInkCount] = penIndex;
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
                CreateOrAttachInk(pendingPenIds[i], pendingInkIds[i], pendingPenIndexes[i], line);
            }
        }

        private void CreateOrAttachInk(int penId, int inkId, int penIndex, LineRenderer line)
        {
            if (penIndex < 0 || penIndex >= currentObjectIds.Length)
                return;

            int objectId = currentObjectIds[penIndex];
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
            }
            else if (!TryGetObjectReference(objectId, out referencePosition))
            {
                referencePosition = GetLineBoundsCenter(line);
                RememberObjectReference(objectId, referencePosition);
            }

            int handleIndex = FindHandleForObject(objectId);
            Vector3 handlePosition = handleIndex >= 0 ? handleObjects[handleIndex].transform.position : referencePosition;
            Quaternion handleRotation = handleIndex >= 0 ? handleObjects[handleIndex].transform.rotation : Quaternion.identity;
            ReceiveBinding(penId, inkId, objectId, handlePosition, handleRotation);
            SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(ReceiveBinding),
                penId, inkId, objectId, handlePosition, handleRotation);
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
            handleObjectIds[handleIndex] = objectId;
            handleAssignedTimes[handleIndex] = Time.time;
            handleEmptySince[handleIndex] = -1f;
            handleSawInk[handleIndex] = false;
            MoveHandle(handleIndex, referencePosition, Quaternion.identity);
            SetHandleAvailable(handleIndex, true);
            RemovePendingRequest(objectId);
            RequestSerialization();
            Log(GrabQvStrings.AssignedLog + objectId);
        }

        [NetworkCallable(maxEventsPerSecond: 100)]
        public void ReceiveBinding(int penId, int inkId, int objectId,
            Vector3 handlePosition, Quaternion handleRotation)
        {
            if (!IsValidObjectId(objectId) || !IsFinite(handlePosition) || !IsValidRotation(handleRotation))
                return;
            AddOrMergeBinding(penId, inkId, objectId, handlePosition, handleRotation);
            if (Networking.IsOwner(gameObject))
                RequestSerialization();
        }

        private void AddOrMergeBinding(int penId, int inkId, int objectId,
            Vector3 handlePosition, Quaternion handleRotation)
        {
            int existing = FindBinding(penId, inkId);
            if (existing >= 0)
            {
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
            bindingHandlePositions[index] = handlePosition;
            bindingHandleRotations[index] = handleRotation;
            bindingLines[index] = null;
            bindingSawInk[index] = false;
            bindingWasApplied[index] = false;
            TryApplyBindingAt(index, FindKnownLine(penId, inkId));
        }

        private void TryApplyBindingAt(int index, LineRenderer line)
        {
            if (index < 0 || index >= bindingCount || bindingWasApplied[index] || !Utilities.IsValid(line))
                return;
            bindingLines[index] = line;
            bindingSawInk[index] = true;
            int handleIndex = FindHandleForObject(bindingObjectIds[index]);
            if (handleIndex < 0 || !Utilities.IsValid(handleObjects[handleIndex]))
                return;
            Transform inkTransform = line.transform;
            if (!line.useWorldSpace || !IsInkTransformIdentity(inkTransform))
                return;
            line.useWorldSpace = false;
            Transform handle = handleObjects[handleIndex].transform;
            Quaternion deltaRotation = handle.rotation * Quaternion.Inverse(bindingHandleRotations[index]);
            Vector3 worldPosition = deltaRotation * (-bindingHandlePositions[index]) + handle.position;
            inkTransform.SetPositionAndRotation(worldPosition, deltaRotation);
            bindingWasApplied[index] = true;
            handleSawInk[handleIndex] = true;
            handleEmptySince[handleIndex] = -1f;
            UpdateHandleBounds(bindingObjectIds[index]);
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
            if (handleIndex < 0 || !Utilities.IsValid(handleColliders[handleIndex]))
                return;
            Transform handle = handleObjects[handleIndex].transform;
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
            if (changed)
                RequestSerialization();
        }

        private bool HasLiveInk(int objectId)
        {
            for (int i = 0; i < bindingCount; i++)
            {
                if (bindingObjectIds[i] == objectId && Utilities.IsValid(bindingLines[i]))
                    return true;
            }
            return false;
        }

        private void ReleaseHandle(int index)
        {
            int objectId = handleObjectIds[index];
            SetHandleAvailable(index, false);
            handleObjectIds[index] = 0;
            handleAssignedTimes[index] = 0f;
            handleEmptySince[index] = -1f;
            handleSawInk[index] = false;
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (Utilities.IsValid(localPlayer) && Utilities.IsValid(handleObjects[index]))
                Networking.SetOwner(localPlayer, handleObjects[index]);
            MoveHandle(index, handleHomePositions[index], handleHomeRotations[index]);
            Log(GrabQvStrings.ReleasedLog + objectId);
        }

        private void InitializeHandles()
        {
            int length = handleObjects == null ? 0 : handleObjects.Length;
            handleObjectIds = new int[length];
            handleAssignedTimes = new float[length];
            handleEmptySince = new float[length];
            handleSawInk = new bool[length];
            handleHomePositions = new Vector3[length];
            handleHomeRotations = new Quaternion[length];
            for (int i = 0; i < length; i++)
            {
                if (!Utilities.IsValid(handleObjects[i]))
                    continue;
                handleHomePositions[i] = handleObjects[i].transform.position;
                handleHomeRotations[i] = handleObjects[i].transform.rotation;
                handleEmptySince[i] = -1f;
                SetHandleAvailable(i, false);
            }
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
                bindingHandlePositions[i] = bindingHandlePositions[i + 1];
                bindingHandleRotations[i] = bindingHandleRotations[i + 1];
                bindingLines[i] = bindingLines[i + 1];
                bindingSawInk[i] = bindingSawInk[i + 1];
                bindingWasApplied[i] = bindingWasApplied[i + 1];
            }
            bindingCount--;
            bindingLines[bindingCount] = null;
            bindingSawInk[bindingCount] = false;
            bindingWasApplied[bindingCount] = false;
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
            syncedBindingHandlePositions = new Vector3[bindingCount];
            syncedBindingHandleRotations = new Quaternion[bindingCount];
            for (int i = 0; i < bindingCount; i++)
            {
                syncedBindingPenIds[i] = bindingPenIds[i];
                syncedBindingInkIds[i] = bindingInkIds[i];
                syncedBindingObjectIds[i] = bindingObjectIds[i];
                syncedBindingHandlePositions[i] = bindingHandlePositions[i];
                syncedBindingHandleRotations[i] = bindingHandleRotations[i];
            }
        }

        public override void OnDeserialization()
        {
            ApplySyncedHandles();
            if (syncedCurrentObjectIds != null && syncedCurrentObjectIds.Length == currentObjectIds.Length)
            {
                for (int i = 0; i < currentObjectIds.Length; i++)
                {
                    if (syncedCurrentObjectIds[i] == 0 || IsValidObjectId(syncedCurrentObjectIds[i]))
                        currentObjectIds[i] = syncedCurrentObjectIds[i];
                }
            }

            if (syncedBindingPenIds == null || syncedBindingInkIds == null || syncedBindingObjectIds == null ||
                syncedBindingHandlePositions == null || syncedBindingHandleRotations == null)
                return;
            int length = syncedBindingPenIds.Length;
            if (length > MaxBindings || syncedBindingInkIds.Length != length || syncedBindingObjectIds.Length != length ||
                syncedBindingHandlePositions.Length != length || syncedBindingHandleRotations.Length != length)
                return;
            for (int i = 0; i < length; i++)
            {
                if (!IsValidObjectId(syncedBindingObjectIds[i]) || !IsFinite(syncedBindingHandlePositions[i]) ||
                    !IsValidRotation(syncedBindingHandleRotations[i]))
                    continue;
                AddOrMergeBinding(syncedBindingPenIds[i], syncedBindingInkIds[i], syncedBindingObjectIds[i],
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
                bool changed = handleObjectIds[i] != objectId;
                handleObjectIds[i] = objectId;
                if (!changed)
                    continue;
                if (objectId == 0)
                    SetHandleAvailable(i, false);
                else
                {
                    handleAssignedTimes[i] = Time.time;
                    handleEmptySince[i] = -1f;
                    handleSawInk[i] = HasLiveInk(objectId);
                    SetHandleAvailable(i, true);
                    RemovePendingRequest(objectId);
                    TryApplyBindingsForObject(objectId);
                }
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

        private void ResolveTargetReferences()
        {
            int length = targetedPens == null ? 0 : targetedPens.Length;
            if (targetLateSyncs == null || targetLateSyncs.Length != length)
                targetLateSyncs = new QvPen_LateSync[length];
            if (targetPickups == null || targetPickups.Length != length)
                targetPickups = new VRC_Pickup[length];
            for (int i = 0; i < length; i++)
            {
                QvPen_PenManager pen = targetedPens[i];
                if (!Utilities.IsValid(pen))
                {
                    targetLateSyncs[i] = null;
                    targetPickups[i] = null;
                    continue;
                }
                if (!Utilities.IsValid(targetLateSyncs[i]))
                    targetLateSyncs[i] = pen.GetComponentInChildren<QvPen_LateSync>(true);
                if (!Utilities.IsValid(targetPickups[i]))
                    targetPickups[i] = pen.GetComponentInChildren<VRC_Pickup>(true);
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
        }

        public void RefreshTargetReferences()
        {
            ResolveTargetReferences();
        }
#endif
    }
}
