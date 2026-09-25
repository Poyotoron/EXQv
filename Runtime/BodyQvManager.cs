using QvPen.UdonScript;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using Utilities = VRC.SDKBase.Utilities;

namespace Maaaaa.BodyQv
{
    [DefaultExecutionOrder(-100)]
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class BodyQvManager : UdonSharpBehaviour
    {
        public const int MaxBindings = 1024;
        public const int TrackingHead = 100;
        public const int TrackingLeftHand = 101;
        public const int TrackingRightHand = 102;
        public const int PlayerOrigin = 200;

        private const int MaxKnownInks = 2048;
        private const int MaxPendingBindings = 32;
        private const int MaxStrokeSamples = 16;
        private const int MaxPlayers = 80;
        private const int MaxColliderPlayers = 16;
        private const int CollidersPerPlayer = 22;
        private const int MaxBodyColliders = MaxColliderPlayers * CollidersPerPlayer;
        private const float CandidatePlayerDistance = 5f;
        private const float ReferenceEyeHeight = 1.3f;
        private const float IdentityTolerance = 0.0001f;
        private const float FullPoolScanInterval = 5f;
        private const float DistalTieDistance = 0.005f;
        private const float HeadCenterBehindEyes = 0.07f;
        private const float HeadCenterAboveBone = 0.09f;
        private const float HeadAccessoryBottom = -0.05f;
        private const float HandHeadPreferenceDistance = 0.005f;
        private const int HeadAccessoryRequiredPercent = 80;
        private const int LeftHandMask = 1;
        private const int RightHandMask = 2;

        [Header("対象ペン")]
        [SerializeField, Tooltip("体への追従と表面吸着を有効にする QvPen の PenManager です。")]
        private QvPen_PenManager[] targetedPens = new QvPen_PenManager[0];

        [SerializeField, HideInInspector]
        private QvPen_LateSync[] targetLateSyncs = new QvPen_LateSync[0];

        [SerializeField, HideInInspector]
        private VRC_Pickup[] targetPickups = new VRC_Pickup[0];

        [Header("線の追従")]
        [SerializeField, Tooltip("線から体の表面までの平均距離が、この値以内なら体に紐付けます。")]
        private float surfaceBindingDistance = 0.05f;

        [Header("頭の飾り")]
        [SerializeField, InspectorName("頭の上の飾り（耳・輪など）を頭に付ける"),
         Tooltip("頭の周りや上に描いた耳・輪などを、頭と一緒に動かします。")]
        private bool enableHeadAccessories = true;

        [SerializeField, InspectorName("頭の飾りの範囲の高さ（m、身長 1.3 m 基準）"),
         Tooltip("頭の中心から上へ、飾りとして判定する範囲の高さです。")]
        private float headAccessoryHeight = 0.40f;

        [SerializeField, InspectorName("頭の飾りの範囲の半径（m、身長 1.3 m 基準）"),
         Tooltip("頭の中心を通る上下軸から、飾りとして判定する範囲の半径です。")]
        private float headAccessoryRadius = 0.22f;

        [SerializeField, InspectorName("紐付けの結果をログに出す（確認用）"),
         Tooltip("描いた線の紐付け結果を、描いた本人のログに一行だけ出します。")]
        private bool logBindingResults;

        [Header("体コライダー")]
        [SerializeField, Tooltip("対象ペンで体の表面をなぞる機能を有効にします。")]
        private bool enableBodyColliders = true;

        [SerializeField, Tooltip("体コライダーに使うユーザーレイヤー（22～31）です。")]
        private int bodyColliderLayer = 23;

        [SerializeField, Tooltip("体コライダーを表示する、自分からの最大距離です。")]
        private float bodyColliderDistance = 3f;

        [SerializeField, Tooltip("身長 1.3 m のアバターに対する腰から背骨の半径です。")]
        private float hipsRadius = 0.16f;

        [SerializeField, Tooltip("身長 1.3 m のアバターに対する胸部の半径です。")]
        private float torsoRadius = 0.15f;

        [SerializeField, Tooltip("身長 1.3 m のアバターに対する首の半径です。")]
        private float neckRadius = 0.06f;

        [SerializeField, Tooltip("身長 1.3 m のアバターに対する頭の半径です。")]
        private float headRadius = 0.11f;

        [SerializeField, Tooltip("身長 1.3 m のアバターに対する上腕の半径です。")]
        private float upperArmRadius = 0.075f;

        [SerializeField, Tooltip("身長 1.3 m のアバターに対する前腕の半径です。")]
        private float lowerArmRadius = 0.065f;

        [SerializeField, Tooltip("身長 1.3 m のアバターに対する手のひらの半径です。")]
        private float handRadius = 0.07f;

        [SerializeField, Tooltip("身長 1.3 m のアバターに対する指の半径です。")]
        private float fingerRadius = 0.012f;

        [SerializeField, Tooltip("身長 1.3 m のアバターに対する太ももの半径です。")]
        private float upperLegRadius = 0.105f;

        [SerializeField, Tooltip("身長 1.3 m のアバターに対するすねの半径です。")]
        private float lowerLegRadius = 0.085f;

        [SerializeField, Tooltip("身長 1.3 m のアバターに対する足の半径です。")]
        private float footRadius = 0.085f;

        [SerializeField, Tooltip("対象ペンを持っている間、体コライダーと同じ形を半透明で表示します。")]
        private bool showBodyShape;

        [SerializeField, HideInInspector]
        private GameObject bodyColliderTemplate;

        [SerializeField, HideInInspector]
        private Transform bodyColliderPoolRoot;

        [SerializeField, HideInInspector]
        private Material headAccessoryPreviewMaterial;

        private int[] bindingPenIds = new int[MaxBindings];
        private int[] bindingInkIds = new int[MaxBindings];
        private int[] bindingPlayerIds = new int[MaxBindings];
        private int[] bindingTypes = new int[MaxBindings];
        private Vector3[] bindingPositions = new Vector3[MaxBindings];
        private Quaternion[] bindingRotations = new Quaternion[MaxBindings];
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
        private int[] lastHeldHandMasks = new int[0];
        private int currentHeldHandMask;
        private float nextFullPoolScanTime;
        private VRCPlayerApi[] players = new VRCPlayerApi[MaxPlayers];
        private VRCPlayerApi[] colliderCandidates = new VRCPlayerApi[MaxPlayers];
        private float[] colliderCandidateDistances = new float[MaxPlayers];

        private int[] poseCachePlayerIds = new int[MaxBindings];
        private int[] poseCacheTypes = new int[MaxBindings];
        private Vector3[] poseCachePositions = new Vector3[MaxBindings];
        private Quaternion[] poseCacheRotations = new Quaternion[MaxBindings];
        private bool[] poseCacheValid = new bool[MaxBindings];
        private int poseCacheCount;

        private int[] pendingPenIds = new int[MaxPendingBindings];
        private int[] pendingInkIds = new int[MaxPendingBindings];
        private LineRenderer[] pendingLines = new LineRenderer[MaxPendingBindings];
        private int[] pendingExcludedHandMasks = new int[MaxPendingBindings];
        private int pendingBindingCount;
        private Vector3[] strokeSamplePoints = new Vector3[MaxStrokeSamples];
        private int lastNearestPlayerId = -1;
        private int lastNearestType = -1;
        private float lastNearestDistance = float.MaxValue;

        private GameObject[] bodyColliderObjects = new GameObject[0];
        private CapsuleCollider[] bodyColliders = new CapsuleCollider[0];
        private GameObject[] bodyShapePreviews = new GameObject[0];
        private GameObject[] headAccessoryPreviews = new GameObject[0];
        private int activeBodyColliderCount;
        private int activeHeadAccessoryPreviewCount;
        private bool colliderPoolReady;

        [UdonSynced] private int[] syncedPenIds = new int[0];
        [UdonSynced] private int[] syncedInkIds = new int[0];
        [UdonSynced] private int[] syncedPlayerIds = new int[0];
        [UdonSynced] private int[] syncedBindingTypes = new int[0];
        [UdonSynced] private Vector3[] syncedBindingPositions = new Vector3[0];
        [UdonSynced] private Quaternion[] syncedBindingRotations = new Quaternion[0];

        public QvPen_PenManager[] TargetedPens => targetedPens;
        public QvPen_LateSync[] TargetLateSyncs => targetLateSyncs;
        public VRC_Pickup[] TargetPickups => targetPickups;
        public bool EnableBodyColliders => enableBodyColliders;
        public int BodyColliderLayer => bodyColliderLayer;
        public GameObject BodyColliderTemplate => bodyColliderTemplate;
        public Transform BodyColliderPoolRoot => bodyColliderPoolRoot;

        private void Start()
        {
            ResolveTargetReferences();
            poolChildCounts = new int[targetLateSyncs.Length * 2];
            poolLastChildren = new Transform[targetLateSyncs.Length * 2];
            for (int i = 0; i < poolChildCounts.Length; i++)
                poolChildCounts[i] = -1;
            nextFullPoolScanTime = Time.time + FullPoolScanInterval;

            CreateBodyColliderPool();
        }

        private void Update()
        {
            UpdateHeldPenHands(Networking.LocalPlayer);
            ScanInkPools();
            UpdateBodyColliders();
        }

        public override void PostLateUpdate()
        {
            ProcessPendingBindings();
            if (bindingCount == 0)
                return;

            poseCacheCount = 0;
            int index = 0;
            while (index < bindingCount)
            {
                LineRenderer line = bindingLines[index];
                if (bindingSawInk[index] && !Utilities.IsValid(line))
                {
                    RemoveBindingAt(index);
                    continue;
                }

                if (!bindingWasApplied[index])
                {
                    if (Utilities.IsValid(line))
                        TryApplyBindingAt(index, line);
                    index++;
                    continue;
                }

                Vector3 currentPosition;
                Quaternion currentRotation;
                if (!TryGetCachedPose(bindingPlayerIds[index], bindingTypes[index], out currentPosition, out currentRotation))
                {
                    index++;
                    continue;
                }

                Quaternion deltaRotation = currentRotation * Quaternion.Inverse(bindingRotations[index]);
                Vector3 worldPosition = deltaRotation * (-bindingPositions[index]) + currentPosition;
                line.transform.SetPositionAndRotation(worldPosition, deltaRotation);
                index++;
            }
        }

        private void ScanInkPools()
        {
            int poolCount = targetLateSyncs.Length * 2;
            if (poolCount == 0)
                return;

            if (poolChildCounts.Length != poolCount)
            {
                poolChildCounts = new int[poolCount];
                poolLastChildren = new Transform[poolCount];
                for (int i = 0; i < poolCount; i++)
                    poolChildCounts[i] = -1;
            }

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
            if (!periodicScan && poolChildCounts[countIndex] == childCount &&
                poolLastChildren[countIndex] == lastChild)
                return;

            poolChildCounts[countIndex] = childCount;
            poolLastChildren[countIndex] = lastChild;
            for (int i = 0; i < childCount; i++)
            {
                Transform child = pool.GetChild(i);
                if (!Utilities.IsValid(child))
                    continue;

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
            if (!Utilities.IsValid(line))
                return;

            if (!line.useWorldSpace || !IsInkTransformIdentity(line.transform))
                return;

            RememberInk(penId, inkId, line);

            int bindingIndex = FindBinding(penId, inkId);
            if (bindingIndex >= 0)
            {
                TryApplyBindingAt(bindingIndex, line);
                return;
            }

            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (!Utilities.IsValid(localPlayer))
                return;

            int ownerId = QvPenUtilities.EulerAnglesToPlayerId(ownerIdVector);
            if (ownerId != localPlayer.playerId)
                return;

            EnqueueBinding(penId, inkId, line, GetDrawingHandMask(penIndex));
        }

        private void EnqueueBinding(int penId, int inkId, LineRenderer line, int excludedHandMask)
        {
            if (pendingBindingCount >= MaxPendingBindings)
            {
                for (int i = 0; i < MaxPendingBindings - 1; i++)
                {
                    pendingPenIds[i] = pendingPenIds[i + 1];
                    pendingInkIds[i] = pendingInkIds[i + 1];
                    pendingLines[i] = pendingLines[i + 1];
                    pendingExcludedHandMasks[i] = pendingExcludedHandMasks[i + 1];
                }
                pendingBindingCount--;
            }

            pendingPenIds[pendingBindingCount] = penId;
            pendingInkIds[pendingBindingCount] = inkId;
            pendingLines[pendingBindingCount] = line;
            pendingExcludedHandMasks[pendingBindingCount] = excludedHandMask;
            pendingBindingCount++;
        }

        private void ProcessPendingBindings()
        {
            int count = pendingBindingCount;
            pendingBindingCount = 0;
            for (int i = 0; i < count; i++)
            {
                LineRenderer line = pendingLines[i];
                pendingLines[i] = null;
                if (!Utilities.IsValid(line) || !line.useWorldSpace || !IsInkTransformIdentity(line.transform))
                    continue;

                DetermineAndBroadcastBinding(pendingPenIds[i], pendingInkIds[i], line,
                    pendingExcludedHandMasks[i]);
            }
        }

        private void DetermineAndBroadcastBinding(int penId, int inkId, LineRenderer line, int excludedHandMask)
        {
            int sampleCount = ReadStrokeSamples(line);
            if (sampleCount <= 0)
                return;

            int playerId;
            int bindingType;
            float surfaceDistance;
            string method;
            Vector3 bindingPosition;
            Quaternion bindingRotation;
            if (!TryDetermineBinding(strokeSamplePoints, sampleCount, excludedHandMask,
                    out playerId, out bindingType, out surfaceDistance, out method,
                    out bindingPosition, out bindingRotation))
            {
                LogNoBinding(lastNearestPlayerId, lastNearestType, lastNearestDistance);
                return;
            }

            LogBindingResult(playerId, bindingType, surfaceDistance, method);
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ReceiveBinding),
                penId, inkId, playerId, bindingType, bindingPosition, bindingRotation);
        }

        public bool TryDetermineBinding(Vector3[] samplePoints, int sampleCount, int excludedHandMask,
            out int playerId, out int bindingType, out float surfaceDistance, out string method,
            out Vector3 bindingPosition, out Quaternion bindingRotation)
        {
            playerId = -1;
            bindingType = -1;
            surfaceDistance = float.MaxValue;
            method = "";
            bindingPosition = Vector3.zero;
            bindingRotation = Quaternion.identity;
            lastNearestPlayerId = -1;
            lastNearestType = -1;
            lastNearestDistance = float.MaxValue;
            if (samplePoints == null || sampleCount <= 0)
                return false;

            sampleCount = Mathf.Min(sampleCount, Mathf.Min(samplePoints.Length, MaxStrokeSamples));
            if (sampleCount <= 0)
                return false;

            VRCPlayerApi.GetPlayers(players);
            Vector3 sampleCenter = GetSampleCenter(samplePoints, sampleCount);

            int armPlayerId;
            int armType;
            float armDistance;
            Vector3 armPosition;
            Quaternion armRotation;
            bool hasArm = FindBestArmSurface(samplePoints, sampleCount, sampleCenter, excludedHandMask,
                out armPlayerId, out armType, out armDistance, out armPosition, out armRotation);

            int headPlayerId;
            int headType;
            float headDistance;
            Vector3 headPosition;
            Quaternion headRotation;
            bool hasHead = FindBestHeadRegion(samplePoints, sampleCount, sampleCenter,
                out headPlayerId, out headType, out headDistance, out headPosition, out headRotation);

            if (hasArm && armDistance <= surfaceBindingDistance &&
                (!hasHead || armDistance + HandHeadPreferenceDistance <= headDistance))
            {
                playerId = armPlayerId;
                bindingType = armType;
                surfaceDistance = armDistance;
                method = "手・腕";
                bindingPosition = armPosition;
                bindingRotation = armRotation;
                return true;
            }

            if (hasHead)
            {
                playerId = headPlayerId;
                bindingType = headType;
                surfaceDistance = headDistance;
                method = "頭の範囲";
                bindingPosition = headPosition;
                bindingRotation = headRotation;
                return true;
            }

            bool hasBody = FindBestBodySurface(samplePoints, sampleCount, sampleCenter, excludedHandMask,
                out playerId, out bindingType, out surfaceDistance, out bindingPosition, out bindingRotation);
            lastNearestPlayerId = playerId;
            lastNearestType = bindingType;
            lastNearestDistance = surfaceDistance;
            if (!hasBody || surfaceDistance > surfaceBindingDistance)
            {
                playerId = -1;
                bindingType = -1;
                method = "";
                bindingPosition = Vector3.zero;
                bindingRotation = Quaternion.identity;
                return false;
            }

            method = "表面";
            return true;
        }

        private bool FindBestArmSurface(Vector3[] samplePoints, int sampleCount, Vector3 sampleCenter,
            int excludedHandMask, out int bestPlayerId, out int bestType, out float bestDistance,
            out Vector3 bestPosition, out Quaternion bestRotation)
        {
            bestPlayerId = -1;
            bestType = -1;
            bestDistance = float.MaxValue;
            bestPosition = Vector3.zero;
            bestRotation = Quaternion.identity;
            for (int playerIndex = 0; playerIndex < players.Length; playerIndex++)
            {
                VRCPlayerApi player = players[playerIndex];
                if (!IsCandidatePlayer(player, sampleCenter))
                    continue;

                bool foundHumanoidArm = false;
                for (int boneValue = 0; boneValue < (int)HumanBodyBones.LastBone; boneValue++)
                {
                    if ((!IsLeftArmBone(boneValue) && !IsRightArmBone(boneValue)) ||
                        (player.isLocal && IsArmExcluded(boneValue, excludedHandMask)))
                        continue;

                    Vector3 shapeStart;
                    Vector3 shapeEnd;
                    float shapeRadius;
                    if (!TryGetBodyShape(player, boneValue, out shapeStart, out shapeEnd, out shapeRadius))
                        continue;

                    foundHumanoidArm = true;
                    float distance = GetAverageSurfaceDistance(samplePoints, sampleCount,
                        shapeStart, shapeEnd, shapeRadius);
                    if (!ShouldReplaceCandidate(distance, player.playerId, boneValue,
                            bestDistance, bestPlayerId, bestType))
                        continue;

                    bestDistance = distance;
                    bestPlayerId = player.playerId;
                    bestType = boneValue;
                    bestPosition = player.GetBonePosition((HumanBodyBones)boneValue);
                    bestRotation = player.GetBoneRotation((HumanBodyBones)boneValue);
                }

                if (foundHumanoidArm)
                    continue;

                float scale = GetAvatarScale(player);
                ConsiderTrackingPoint(samplePoints, sampleCount, player, TrackingLeftHand,
                    VRCPlayerApi.TrackingDataType.LeftHand, handRadius * scale,
                    player.isLocal && (excludedHandMask & LeftHandMask) != 0,
                    ref bestDistance, ref bestPlayerId, ref bestType, ref bestPosition, ref bestRotation);
                ConsiderTrackingPoint(samplePoints, sampleCount, player, TrackingRightHand,
                    VRCPlayerApi.TrackingDataType.RightHand, handRadius * scale,
                    player.isLocal && (excludedHandMask & RightHandMask) != 0,
                    ref bestDistance, ref bestPlayerId, ref bestType, ref bestPosition, ref bestRotation);
            }
            return bestPlayerId > 0;
        }

        private bool FindBestHeadRegion(Vector3[] samplePoints, int sampleCount, Vector3 sampleCenter,
            out int bestPlayerId, out int bestType, out float bestSurfaceDistance,
            out Vector3 bestPosition, out Quaternion bestRotation)
        {
            bestPlayerId = -1;
            bestType = -1;
            bestSurfaceDistance = float.MaxValue;
            bestPosition = Vector3.zero;
            bestRotation = Quaternion.identity;
            int bestInsideCount = -1;
            float bestCenterDistance = float.MaxValue;
            for (int playerIndex = 0; playerIndex < players.Length; playerIndex++)
            {
                VRCPlayerApi player = players[playerIndex];
                if (!IsCandidatePlayer(player, sampleCenter))
                    continue;

                Vector3 center;
                Vector3 up;
                int candidateType;
                Vector3 candidatePosition;
                Quaternion candidateRotation;
                float scale;
                if (!TryGetHeadReference(player, out center, out up, out candidateType,
                        out candidatePosition, out candidateRotation, out scale))
                    continue;

                float averageCenterDistance;
                float averageSurfaceDistance;
                int insideCount = GetHeadRegionInsideCount(samplePoints, sampleCount, center, up, scale,
                    out averageCenterDistance, out averageSurfaceDistance);
                if (insideCount * 100 < sampleCount * HeadAccessoryRequiredPercent ||
                    insideCount < bestInsideCount ||
                    (insideCount == bestInsideCount && averageCenterDistance >= bestCenterDistance))
                    continue;

                bestPlayerId = player.playerId;
                bestType = candidateType;
                bestInsideCount = insideCount;
                bestCenterDistance = averageCenterDistance;
                bestSurfaceDistance = averageSurfaceDistance;
                bestPosition = candidatePosition;
                bestRotation = candidateRotation;
            }
            return bestPlayerId > 0;
        }

        private int GetHeadRegionInsideCount(Vector3[] samplePoints, int sampleCount,
            Vector3 center, Vector3 up, float scale, out float averageCenterDistance,
            out float averageSurfaceDistance)
        {
            int insideCount = 0;
            float centerDistanceSum = 0f;
            float surfaceDistanceSum = 0f;
            float headSphereRadius = headRadius * scale;
            float expandedHeadRadius = headSphereRadius + surfaceBindingDistance;
            float bottom = HeadAccessoryBottom * scale;
            float top = headAccessoryHeight * scale;
            float accessoryRadius = headAccessoryRadius * scale;
            for (int i = 0; i < sampleCount; i++)
            {
                Vector3 offset = samplePoints[i] - center;
                float centerDistance = offset.magnitude;
                centerDistanceSum += centerDistance;
                surfaceDistanceSum += Mathf.Max(0f, centerDistance - headSphereRadius);
                bool inside = centerDistance <= expandedHeadRadius;
                if (!inside && enableHeadAccessories)
                {
                    float height = Vector3.Dot(offset, up);
                    float axisDistance = (offset - up * height).magnitude;
                    inside = height >= bottom && height <= top && axisDistance <= accessoryRadius;
                }
                if (inside)
                    insideCount++;
            }

            averageCenterDistance = centerDistanceSum / sampleCount;
            averageSurfaceDistance = surfaceDistanceSum / sampleCount;
            return insideCount;
        }

        private bool FindBestBodySurface(Vector3[] samplePoints, int sampleCount, Vector3 sampleCenter,
            int excludedHandMask, out int bestPlayerId, out int bestType, out float bestDistance,
            out Vector3 bestPosition, out Quaternion bestRotation)
        {
            bestPlayerId = -1;
            bestType = -1;
            bestDistance = float.MaxValue;
            bestPosition = Vector3.zero;
            bestRotation = Quaternion.identity;
            for (int playerIndex = 0; playerIndex < players.Length; playerIndex++)
            {
                VRCPlayerApi player = players[playerIndex];
                if (!IsCandidatePlayer(player, sampleCenter))
                    continue;

                bool foundHumanoidShape = false;
                for (int boneValue = 0; boneValue < (int)HumanBodyBones.LastBone; boneValue++)
                {
                    if (player.isLocal && IsArmExcluded(boneValue, excludedHandMask))
                        continue;

                    Vector3 shapeStart;
                    Vector3 shapeEnd;
                    float shapeRadius;
                    if (!TryGetBodyShape(player, boneValue, out shapeStart, out shapeEnd, out shapeRadius))
                        continue;

                    foundHumanoidShape = true;
                    float distance = GetAverageSurfaceDistance(samplePoints, sampleCount,
                        shapeStart, shapeEnd, shapeRadius);
                    if (!ShouldReplaceCandidate(distance, player.playerId, boneValue,
                            bestDistance, bestPlayerId, bestType))
                        continue;

                    bestDistance = distance;
                    bestPlayerId = player.playerId;
                    bestType = boneValue;
                    bestPosition = player.GetBonePosition((HumanBodyBones)boneValue);
                    bestRotation = player.GetBoneRotation((HumanBodyBones)boneValue);
                }

                if (foundHumanoidShape)
                    continue;

                float scale = GetAvatarScale(player);
                ConsiderTrackingPoint(samplePoints, sampleCount, player, TrackingHead,
                    VRCPlayerApi.TrackingDataType.Head, headRadius * scale, false,
                    ref bestDistance, ref bestPlayerId, ref bestType, ref bestPosition, ref bestRotation);
                ConsiderTrackingPoint(samplePoints, sampleCount, player, TrackingLeftHand,
                    VRCPlayerApi.TrackingDataType.LeftHand, handRadius * scale,
                    player.isLocal && (excludedHandMask & LeftHandMask) != 0,
                    ref bestDistance, ref bestPlayerId, ref bestType, ref bestPosition, ref bestRotation);
                ConsiderTrackingPoint(samplePoints, sampleCount, player, TrackingRightHand,
                    VRCPlayerApi.TrackingDataType.RightHand, handRadius * scale,
                    player.isLocal && (excludedHandMask & RightHandMask) != 0,
                    ref bestDistance, ref bestPlayerId, ref bestType, ref bestPosition, ref bestRotation);

                Vector3 originPosition = player.GetPosition();
                float originDistance = GetAverageSurfaceDistance(samplePoints, sampleCount,
                    originPosition, originPosition, 0f);
                if (ShouldReplaceCandidate(originDistance, player.playerId, PlayerOrigin,
                        bestDistance, bestPlayerId, bestType))
                {
                    bestDistance = originDistance;
                    bestPlayerId = player.playerId;
                    bestType = PlayerOrigin;
                    bestPosition = originPosition;
                    bestRotation = player.GetRotation();
                }
            }
            return bestPlayerId > 0;
        }

        private bool IsCandidatePlayer(VRCPlayerApi player, Vector3 sampleCenter)
        {
            return Utilities.IsValid(player) &&
                   (player.GetPosition() - sampleCenter).sqrMagnitude <=
                   CandidatePlayerDistance * CandidatePlayerDistance;
        }

        private Vector3 GetSampleCenter(Vector3[] samplePoints, int sampleCount)
        {
            Vector3 center = Vector3.zero;
            for (int i = 0; i < sampleCount; i++)
                center += samplePoints[i];
            return center / sampleCount;
        }

        private void LogBindingResult(int playerId, int bindingType, float surfaceDistance, string method)
        {
            if (!logBindingResults)
                return;

            VRCPlayerApi player = VRCPlayerApi.GetPlayerById(playerId);
            string playerName = Utilities.IsValid(player) ? player.displayName : "不明";
            Debug.Log("[BodyQv] 付け先: " + playerName + " (ID " + playerId + ") / 部位: " +
                      GetBindingTypeName(bindingType) + " / 平均の表面距離: " + surfaceDistance +
                      " m / 決め方: " + method);
        }

        private void LogNoBinding(int playerId, int bindingType, float surfaceDistance)
        {
            if (!logBindingResults)
                return;

            if (playerId <= 0 || bindingType < 0 || surfaceDistance == float.MaxValue)
            {
                Debug.Log("[BodyQv] 紐付けなし / 最も近い部位: なし");
                return;
            }

            VRCPlayerApi player = VRCPlayerApi.GetPlayerById(playerId);
            string playerName = Utilities.IsValid(player) ? player.displayName : "不明";
            Debug.Log("[BodyQv] 紐付けなし / 最も近い付け先: " + playerName + " (ID " + playerId +
                      ") / 部位: " + GetBindingTypeName(bindingType) + " / 平均の表面距離: " +
                      surfaceDistance + " m");
        }

        private string GetBindingTypeName(int bindingType)
        {
            if (bindingType == TrackingHead)
                return "頭のトラッキング点";
            if (bindingType == TrackingLeftHand)
                return "左手のトラッキング点";
            if (bindingType == TrackingRightHand)
                return "右手のトラッキング点";
            if (bindingType == PlayerOrigin)
                return "プレイヤー原点";
            if (bindingType >= 0 && bindingType < (int)HumanBodyBones.LastBone)
                return ((HumanBodyBones)bindingType).ToString();
            return "不明";
        }

        private void ConsiderTrackingPoint(Vector3[] samplePoints, int sampleCount,
            VRCPlayerApi player, int bindingType,
            VRCPlayerApi.TrackingDataType trackingType, float radius, bool excluded,
            ref float bestDistance, ref int bestPlayerId, ref int bestType,
            ref Vector3 bestPosition, ref Quaternion bestRotation)
        {
            if (excluded)
                return;

            VRCPlayerApi.TrackingData trackingData = player.GetTrackingData(trackingType);
            float distance = GetAverageSurfaceDistance(samplePoints, sampleCount,
                trackingData.position, trackingData.position, radius);
            if (!ShouldReplaceCandidate(distance, player.playerId, bindingType,
                    bestDistance, bestPlayerId, bestType))
                return;

            bestDistance = distance;
            bestPlayerId = player.playerId;
            bestType = bindingType;
            bestPosition = trackingData.position;
            bestRotation = trackingData.rotation;
        }

        private int ReadStrokeSamples(LineRenderer line)
        {
            int pointCount = line.positionCount;
            if (pointCount <= 0)
                return 0;

            int sampleCount = Mathf.Min(pointCount, MaxStrokeSamples);
            for (int i = 0; i < sampleCount; i++)
            {
                int pointIndex = sampleCount == 1 ? 0 : i * (pointCount - 1) / (sampleCount - 1);
                strokeSamplePoints[i] = line.GetPosition(pointIndex);
            }
            return sampleCount;
        }

        private float GetAverageSurfaceDistance(Vector3[] samplePoints, int sampleCount,
            Vector3 start, Vector3 end, float radius)
        {
            float distanceSum = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                Vector3 point = samplePoints[i];
                float centerDistance = Vector3.Distance(point, ClosestPointOnSegment(point, start, end));
                distanceSum += Mathf.Max(0f, centerDistance - radius);
            }
            return distanceSum / sampleCount;
        }

        private bool ShouldReplaceCandidate(float distance, int playerId, int bindingType,
            float bestDistance, int bestPlayerId, int bestType)
        {
            if (distance < bestDistance - DistalTieDistance)
                return true;

            if (bestPlayerId != playerId || Mathf.Abs(distance - bestDistance) > DistalTieDistance)
                return false;

            return GetDistalPriority(bindingType) > GetDistalPriority(bestType);
        }

        private int GetDistalPriority(int bindingType)
        {
            HumanBodyBones bone = (HumanBodyBones)bindingType;
            switch (bone)
            {
                case HumanBodyBones.Spine: return 1;
                case HumanBodyBones.Chest: return 2;
                case HumanBodyBones.UpperChest: return 3;
                case HumanBodyBones.Neck: return 4;
                case HumanBodyBones.Head: return 5;
                case HumanBodyBones.LeftUpperArm:
                case HumanBodyBones.RightUpperArm:
                case HumanBodyBones.LeftUpperLeg:
                case HumanBodyBones.RightUpperLeg: return 1;
                case HumanBodyBones.LeftLowerArm:
                case HumanBodyBones.RightLowerArm:
                case HumanBodyBones.LeftLowerLeg:
                case HumanBodyBones.RightLowerLeg: return 2;
                case HumanBodyBones.LeftHand:
                case HumanBodyBones.RightHand:
                case HumanBodyBones.LeftFoot:
                case HumanBodyBones.RightFoot: return 3;
                case HumanBodyBones.LeftToes:
                case HumanBodyBones.RightToes: return 4;
            }

            if (IsFingerBone(bindingType))
                return 4 + GetFingerSegmentPriority(bindingType);
            if (bindingType == TrackingHead)
                return 5;
            if (bindingType == TrackingLeftHand || bindingType == TrackingRightHand)
                return 3;
            return 0;
        }

        private int GetFingerSegmentPriority(int bindingType)
        {
            HumanBodyBones bone = (HumanBodyBones)bindingType;
            switch (bone)
            {
                case HumanBodyBones.LeftThumbIntermediate:
                case HumanBodyBones.LeftIndexIntermediate:
                case HumanBodyBones.LeftMiddleIntermediate:
                case HumanBodyBones.LeftRingIntermediate:
                case HumanBodyBones.LeftLittleIntermediate:
                case HumanBodyBones.RightThumbIntermediate:
                case HumanBodyBones.RightIndexIntermediate:
                case HumanBodyBones.RightMiddleIntermediate:
                case HumanBodyBones.RightRingIntermediate:
                case HumanBodyBones.RightLittleIntermediate: return 1;
                case HumanBodyBones.LeftThumbDistal:
                case HumanBodyBones.LeftIndexDistal:
                case HumanBodyBones.LeftMiddleDistal:
                case HumanBodyBones.LeftRingDistal:
                case HumanBodyBones.LeftLittleDistal:
                case HumanBodyBones.RightThumbDistal:
                case HumanBodyBones.RightIndexDistal:
                case HumanBodyBones.RightMiddleDistal:
                case HumanBodyBones.RightRingDistal:
                case HumanBodyBones.RightLittleDistal: return 2;
            }
            return 0;
        }

        private bool TryGetBodyShape(VRCPlayerApi player, int bindingType,
            out Vector3 start, out Vector3 end, out float radius)
        {
            start = Vector3.zero;
            end = Vector3.zero;
            radius = 0f;
            if (bindingType < 0 || bindingType >= (int)HumanBodyBones.LastBone)
                return false;

            HumanBodyBones bone = (HumanBodyBones)bindingType;
            if (bone == HumanBodyBones.LeftEye || bone == HumanBodyBones.RightEye ||
                bone == HumanBodyBones.Jaw || !IsSupportedShapeBone(bindingType))
                return false;

            start = player.GetBonePosition(bone);
            if (start == Vector3.zero)
                return false;

            float scale = GetAvatarScale(player);
            int childValue = GetChildBoneValue(bindingType);
            end = childValue >= 0 ? player.GetBonePosition((HumanBodyBones)childValue) : start;

            switch (bone)
            {
                case HumanBodyBones.Hips:
                    radius = hipsRadius * scale;
                    break;
                case HumanBodyBones.Spine:
                case HumanBodyBones.Chest:
                case HumanBodyBones.UpperChest:
                    radius = torsoRadius * scale;
                    bool endsAtNeck = bone == HumanBodyBones.UpperChest;
                    if (bone == HumanBodyBones.Chest && end == Vector3.zero)
                    {
                        end = player.GetBonePosition(HumanBodyBones.Neck);
                        endsAtNeck = true;
                    }
                    if (endsAtNeck && end != Vector3.zero)
                    {
                        Vector3 torsoDirection = end - start;
                        if (torsoDirection.sqrMagnitude > 0.000001f)
                        {
                            torsoDirection.Normalize();
                            Vector3 shortenedEnd = end - torsoDirection *
                                Mathf.Max(0f, radius - neckRadius * scale);
                            end = Vector3.Dot(shortenedEnd - start, torsoDirection) >= 0f
                                ? shortenedEnd
                                : start;
                        }
                    }
                    break;
                case HumanBodyBones.Neck:
                    radius = neckRadius * scale;
                    break;
                case HumanBodyBones.Head:
                    radius = headRadius * scale;
                    Vector3 headUp;
                    if (!TryGetHumanoidHeadFrame(player, scale, out start, out headUp))
                        return false;
                    end = start;
                    break;
                case HumanBodyBones.LeftShoulder:
                case HumanBodyBones.RightShoulder:
                case HumanBodyBones.LeftUpperArm:
                case HumanBodyBones.RightUpperArm:
                    radius = upperArmRadius * scale;
                    break;
                case HumanBodyBones.LeftLowerArm:
                case HumanBodyBones.RightLowerArm:
                    radius = lowerArmRadius * scale;
                    break;
                case HumanBodyBones.LeftHand:
                case HumanBodyBones.RightHand:
                    radius = handRadius * scale;
                    HumanBodyBones middle = bone == HumanBodyBones.LeftHand
                        ? HumanBodyBones.LeftMiddleProximal
                        : HumanBodyBones.RightMiddleProximal;
                    end = player.GetBonePosition(middle);
                    if (end == Vector3.zero)
                    {
                        HumanBodyBones lowerArm = bone == HumanBodyBones.LeftHand
                            ? HumanBodyBones.LeftLowerArm
                            : HumanBodyBones.RightLowerArm;
                        Vector3 direction = start - player.GetBonePosition(lowerArm);
                        end = direction.sqrMagnitude > 0.000001f
                            ? start + direction.normalized * (0.08f * scale)
                            : start;
                    }
                    break;
                case HumanBodyBones.LeftUpperLeg:
                case HumanBodyBones.RightUpperLeg:
                    radius = upperLegRadius * scale;
                    break;
                case HumanBodyBones.LeftLowerLeg:
                case HumanBodyBones.RightLowerLeg:
                    radius = lowerLegRadius * scale;
                    break;
                case HumanBodyBones.LeftFoot:
                case HumanBodyBones.RightFoot:
                case HumanBodyBones.LeftToes:
                case HumanBodyBones.RightToes:
                    radius = footRadius * scale;
                    break;
                default:
                    radius = fingerRadius * scale;
                    break;
            }

            if (end == Vector3.zero)
                end = start;
            return radius > 0f;
        }

        private bool TryGetHumanoidHeadFrame(VRCPlayerApi player, float scale,
            out Vector3 center, out Vector3 up)
        {
            Vector3 head = player.GetBonePosition(HumanBodyBones.Head);
            center = Vector3.zero;
            up = Vector3.up;
            if (head == Vector3.zero)
                return false;

            Vector3 neck = player.GetBonePosition(HumanBodyBones.Neck);
            Vector3 neckDirection = head - neck;
            if (neck != Vector3.zero && neckDirection.sqrMagnitude > 0.000001f)
                up = neckDirection.normalized;

            Vector3 leftEye = player.GetBonePosition(HumanBodyBones.LeftEye);
            Vector3 rightEye = player.GetBonePosition(HumanBodyBones.RightEye);
            if (leftEye != Vector3.zero && rightEye != Vector3.zero)
            {
                Vector3 eyeMid = (leftEye + rightEye) * 0.5f;
                Vector3 rightDirection = rightEye - leftEye;
                Vector3 forward = Vector3.Cross(rightDirection.normalized, up);
                if (rightDirection.sqrMagnitude > 0.000001f && forward.sqrMagnitude > 0.000001f)
                {
                    forward.Normalize();
                    if (Vector3.Dot(forward, eyeMid - head) < 0f)
                        forward = -forward;
                    center = eyeMid - forward * (HeadCenterBehindEyes * scale);
                    return true;
                }
            }

            center = head + up * (HeadCenterAboveBone * scale);
            return true;
        }

        private bool TryGetHeadReference(VRCPlayerApi player, out Vector3 center, out Vector3 up,
            out int bindingType, out Vector3 bindingPosition, out Quaternion bindingRotation,
            out float scale)
        {
            scale = GetAvatarScale(player);
            if (TryGetHumanoidHeadFrame(player, scale, out center, out up))
            {
                bindingType = (int)HumanBodyBones.Head;
                bindingPosition = player.GetBonePosition(HumanBodyBones.Head);
                bindingRotation = player.GetBoneRotation(HumanBodyBones.Head);
                return true;
            }

            VRCPlayerApi.TrackingData tracking = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            center = tracking.position;
            up = tracking.rotation * Vector3.up;
            if (up.sqrMagnitude <= 0.000001f)
                up = Vector3.up;
            else
                up.Normalize();
            bindingType = TrackingHead;
            bindingPosition = tracking.position;
            bindingRotation = tracking.rotation;
            return center != Vector3.zero;
        }

        private float GetAvatarScale(VRCPlayerApi player)
        {
            float eyeHeight = player.GetAvatarEyeHeightAsMeters();
            return eyeHeight > 0f ? eyeHeight / ReferenceEyeHeight : 1f;
        }

        private bool IsSupportedShapeBone(int bindingType)
        {
            HumanBodyBones bone = (HumanBodyBones)bindingType;
            switch (bone)
            {
                case HumanBodyBones.Hips:
                case HumanBodyBones.Spine:
                case HumanBodyBones.Chest:
                case HumanBodyBones.UpperChest:
                case HumanBodyBones.Neck:
                case HumanBodyBones.Head:
                case HumanBodyBones.LeftShoulder:
                case HumanBodyBones.RightShoulder:
                case HumanBodyBones.LeftUpperArm:
                case HumanBodyBones.RightUpperArm:
                case HumanBodyBones.LeftLowerArm:
                case HumanBodyBones.RightLowerArm:
                case HumanBodyBones.LeftHand:
                case HumanBodyBones.RightHand:
                case HumanBodyBones.LeftUpperLeg:
                case HumanBodyBones.RightUpperLeg:
                case HumanBodyBones.LeftLowerLeg:
                case HumanBodyBones.RightLowerLeg:
                case HumanBodyBones.LeftFoot:
                case HumanBodyBones.RightFoot:
                case HumanBodyBones.LeftToes:
                case HumanBodyBones.RightToes:
                    return true;
            }
            return IsFingerBone(bindingType);
        }

        private bool IsFingerBone(int bindingType)
        {
            HumanBodyBones bone = (HumanBodyBones)bindingType;
            switch (bone)
            {
                case HumanBodyBones.LeftThumbProximal:
                case HumanBodyBones.LeftThumbIntermediate:
                case HumanBodyBones.LeftThumbDistal:
                case HumanBodyBones.LeftIndexProximal:
                case HumanBodyBones.LeftIndexIntermediate:
                case HumanBodyBones.LeftIndexDistal:
                case HumanBodyBones.LeftMiddleProximal:
                case HumanBodyBones.LeftMiddleIntermediate:
                case HumanBodyBones.LeftMiddleDistal:
                case HumanBodyBones.LeftRingProximal:
                case HumanBodyBones.LeftRingIntermediate:
                case HumanBodyBones.LeftRingDistal:
                case HumanBodyBones.LeftLittleProximal:
                case HumanBodyBones.LeftLittleIntermediate:
                case HumanBodyBones.LeftLittleDistal:
                case HumanBodyBones.RightThumbProximal:
                case HumanBodyBones.RightThumbIntermediate:
                case HumanBodyBones.RightThumbDistal:
                case HumanBodyBones.RightIndexProximal:
                case HumanBodyBones.RightIndexIntermediate:
                case HumanBodyBones.RightIndexDistal:
                case HumanBodyBones.RightMiddleProximal:
                case HumanBodyBones.RightMiddleIntermediate:
                case HumanBodyBones.RightMiddleDistal:
                case HumanBodyBones.RightRingProximal:
                case HumanBodyBones.RightRingIntermediate:
                case HumanBodyBones.RightRingDistal:
                case HumanBodyBones.RightLittleProximal:
                case HumanBodyBones.RightLittleIntermediate:
                case HumanBodyBones.RightLittleDistal:
                    return true;
            }
            return false;
        }

        private bool IsArmExcluded(int bindingType, int excludedHandMask)
        {
            if ((excludedHandMask & LeftHandMask) != 0 && IsLeftArmBone(bindingType))
                return true;
            return (excludedHandMask & RightHandMask) != 0 && IsRightArmBone(bindingType);
        }

        private bool IsLeftArmBone(int bindingType)
        {
            HumanBodyBones bone = (HumanBodyBones)bindingType;
            return bone == HumanBodyBones.LeftUpperArm || bone == HumanBodyBones.LeftLowerArm ||
                   bone == HumanBodyBones.LeftHand || (IsFingerBone(bindingType) && IsLeftFingerBone(bone));
        }

        private bool IsRightArmBone(int bindingType)
        {
            HumanBodyBones bone = (HumanBodyBones)bindingType;
            return bone == HumanBodyBones.RightUpperArm || bone == HumanBodyBones.RightLowerArm ||
                   bone == HumanBodyBones.RightHand || (IsFingerBone(bindingType) && !IsLeftFingerBone(bone));
        }

        private bool IsLeftFingerBone(HumanBodyBones bone)
        {
            int boneValue = (int)bone;
            return boneValue >= (int)HumanBodyBones.LeftThumbProximal &&
                   boneValue <= (int)HumanBodyBones.LeftLittleDistal;
        }

        [NetworkCallable(maxEventsPerSecond: 100)]
        public void ReceiveBinding(int penId, int inkId, int playerId, int bindingType,
            Vector3 bindingPosition, Quaternion bindingRotation)
        {
            if (!IsValidBindingData(playerId, bindingType, bindingPosition, bindingRotation))
                return;

            AddOrMergeBinding(penId, inkId, playerId, bindingType, bindingPosition, bindingRotation);
        }

        private void AddOrMergeBinding(int penId, int inkId, int playerId, int bindingType,
            Vector3 bindingPosition, Quaternion bindingRotation)
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
            bindingPlayerIds[index] = playerId;
            bindingTypes[index] = bindingType;
            bindingPositions[index] = bindingPosition;
            bindingRotations[index] = bindingRotation;
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

            VRCPlayerApi player = VRCPlayerApi.GetPlayerById(bindingPlayerIds[index]);
            if (!Utilities.IsValid(player))
                return;

            Vector3 currentPosition;
            Quaternion currentRotation;
            if (!TryGetPose(player, bindingTypes[index], out currentPosition, out currentRotation))
                return;

            Transform inkTransform = line.transform;
            if (!IsInkTransformIdentity(inkTransform))
                return;

            line.useWorldSpace = false;
            Quaternion deltaRotation = currentRotation * Quaternion.Inverse(bindingRotations[index]);
            Vector3 worldPosition = deltaRotation * (-bindingPositions[index]) + currentPosition;
            inkTransform.SetPositionAndRotation(worldPosition, deltaRotation);
            bindingLines[index] = line;
            bindingWasApplied[index] = true;
        }

        private bool IsInkTransformIdentity(Transform inkTransform)
        {
            return inkTransform.localPosition.sqrMagnitude <= IdentityTolerance &&
                   Quaternion.Angle(inkTransform.localRotation, Quaternion.identity) <= IdentityTolerance &&
                   (inkTransform.localScale - Vector3.one).sqrMagnitude <= IdentityTolerance;
        }

        private bool TryGetCachedPose(int playerId, int bindingType, out Vector3 position, out Quaternion rotation)
        {
            for (int i = 0; i < poseCacheCount; i++)
            {
                if (poseCachePlayerIds[i] != playerId || poseCacheTypes[i] != bindingType)
                    continue;

                position = poseCachePositions[i];
                rotation = poseCacheRotations[i];
                return poseCacheValid[i];
            }

            VRCPlayerApi player = VRCPlayerApi.GetPlayerById(playerId);
            position = Vector3.zero;
            rotation = Quaternion.identity;
            bool valid = Utilities.IsValid(player) && TryGetPose(player, bindingType, out position, out rotation);

            if (poseCacheCount < MaxBindings)
            {
                poseCachePlayerIds[poseCacheCount] = playerId;
                poseCacheTypes[poseCacheCount] = bindingType;
                poseCachePositions[poseCacheCount] = position;
                poseCacheRotations[poseCacheCount] = rotation;
                poseCacheValid[poseCacheCount] = valid;
                poseCacheCount++;
            }

            return valid;
        }

        private bool TryGetPose(VRCPlayerApi player, int bindingType, out Vector3 position, out Quaternion rotation)
        {
            if (bindingType >= 0 && bindingType < (int)HumanBodyBones.LastBone &&
                bindingType != (int)HumanBodyBones.LeftEye &&
                bindingType != (int)HumanBodyBones.RightEye &&
                bindingType != (int)HumanBodyBones.Jaw)
            {
                HumanBodyBones bone = (HumanBodyBones)bindingType;
                position = player.GetBonePosition(bone);
                rotation = player.GetBoneRotation(bone);
                return position != Vector3.zero;
            }

            if (bindingType == TrackingHead || bindingType == TrackingLeftHand || bindingType == TrackingRightHand)
            {
                VRCPlayerApi.TrackingDataType type = VRCPlayerApi.TrackingDataType.Head;
                if (bindingType == TrackingLeftHand)
                    type = VRCPlayerApi.TrackingDataType.LeftHand;
                else if (bindingType == TrackingRightHand)
                    type = VRCPlayerApi.TrackingDataType.RightHand;

                VRCPlayerApi.TrackingData data = player.GetTrackingData(type);
                position = data.position;
                rotation = data.rotation;
                return true;
            }

            if (bindingType == PlayerOrigin)
            {
                position = player.GetPosition();
                rotation = player.GetRotation();
                return true;
            }

            position = Vector3.zero;
            rotation = Quaternion.identity;
            return false;
        }

        private bool IsValidBindingData(int playerId, int bindingType, Vector3 position, Quaternion rotation)
        {
            if (playerId <= 0 || playerId > 65535)
                return false;

            bool validType = (bindingType >= 0 && bindingType < (int)HumanBodyBones.LastBone &&
                              bindingType != (int)HumanBodyBones.LeftEye &&
                              bindingType != (int)HumanBodyBones.RightEye &&
                              bindingType != (int)HumanBodyBones.Jaw) ||
                             bindingType == TrackingHead || bindingType == TrackingLeftHand ||
                             bindingType == TrackingRightHand || bindingType == PlayerOrigin;
            if (!validType)
                return false;

            if (!IsFinite(position.x) || !IsFinite(position.y) || !IsFinite(position.z) ||
                !IsFinite(rotation.x) || !IsFinite(rotation.y) || !IsFinite(rotation.z) || !IsFinite(rotation.w))
                return false;

            float rotationMagnitude = rotation.x * rotation.x + rotation.y * rotation.y +
                                      rotation.z * rotation.z + rotation.w * rotation.w;
            return rotationMagnitude > 0.5f && rotationMagnitude < 1.5f;
        }

        private bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (Utilities.IsValid(player) && !player.isLocal && Networking.IsOwner(gameObject))
                RequestSerialization();
        }

        public override void OnPreSerialization()
        {
            syncedPenIds = new int[bindingCount];
            syncedInkIds = new int[bindingCount];
            syncedPlayerIds = new int[bindingCount];
            syncedBindingTypes = new int[bindingCount];
            syncedBindingPositions = new Vector3[bindingCount];
            syncedBindingRotations = new Quaternion[bindingCount];

            for (int i = 0; i < bindingCount; i++)
            {
                syncedPenIds[i] = bindingPenIds[i];
                syncedInkIds[i] = bindingInkIds[i];
                syncedPlayerIds[i] = bindingPlayerIds[i];
                syncedBindingTypes[i] = bindingTypes[i];
                syncedBindingPositions[i] = bindingPositions[i];
                syncedBindingRotations[i] = bindingRotations[i];
            }
        }

        public override void OnDeserialization()
        {
            if (syncedPenIds == null || syncedInkIds == null || syncedPlayerIds == null ||
                syncedBindingTypes == null || syncedBindingPositions == null || syncedBindingRotations == null)
                return;

            int length = syncedPenIds.Length;
            if (length > MaxBindings || syncedInkIds.Length != length || syncedPlayerIds.Length != length ||
                syncedBindingTypes.Length != length || syncedBindingPositions.Length != length ||
                syncedBindingRotations.Length != length)
                return;

            for (int i = 0; i < length; i++)
            {
                if (!IsValidBindingData(syncedPlayerIds[i], syncedBindingTypes[i],
                        syncedBindingPositions[i], syncedBindingRotations[i]))
                    continue;

                AddOrMergeBinding(syncedPenIds[i], syncedInkIds[i], syncedPlayerIds[i],
                    syncedBindingTypes[i], syncedBindingPositions[i], syncedBindingRotations[i]);
            }
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
                bindingPlayerIds[i] = bindingPlayerIds[i + 1];
                bindingTypes[i] = bindingTypes[i + 1];
                bindingPositions[i] = bindingPositions[i + 1];
                bindingRotations[i] = bindingRotations[i + 1];
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

        private int GetChildBoneValue(int boneValue)
        {
            HumanBodyBones bone = (HumanBodyBones)boneValue;
            switch (bone)
            {
                case HumanBodyBones.Hips: return (int)HumanBodyBones.Spine;
                case HumanBodyBones.Spine: return (int)HumanBodyBones.Chest;
                case HumanBodyBones.Chest: return (int)HumanBodyBones.UpperChest;
                case HumanBodyBones.UpperChest: return (int)HumanBodyBones.Neck;
                case HumanBodyBones.Neck: return (int)HumanBodyBones.Head;
                case HumanBodyBones.LeftShoulder: return (int)HumanBodyBones.LeftUpperArm;
                case HumanBodyBones.RightShoulder: return (int)HumanBodyBones.RightUpperArm;
                case HumanBodyBones.LeftUpperArm: return (int)HumanBodyBones.LeftLowerArm;
                case HumanBodyBones.RightUpperArm: return (int)HumanBodyBones.RightLowerArm;
                case HumanBodyBones.LeftLowerArm: return (int)HumanBodyBones.LeftHand;
                case HumanBodyBones.RightLowerArm: return (int)HumanBodyBones.RightHand;
                case HumanBodyBones.LeftUpperLeg: return (int)HumanBodyBones.LeftLowerLeg;
                case HumanBodyBones.RightUpperLeg: return (int)HumanBodyBones.RightLowerLeg;
                case HumanBodyBones.LeftLowerLeg: return (int)HumanBodyBones.LeftFoot;
                case HumanBodyBones.RightLowerLeg: return (int)HumanBodyBones.RightFoot;
                case HumanBodyBones.LeftFoot: return (int)HumanBodyBones.LeftToes;
                case HumanBodyBones.RightFoot: return (int)HumanBodyBones.RightToes;
                case HumanBodyBones.LeftThumbProximal: return (int)HumanBodyBones.LeftThumbIntermediate;
                case HumanBodyBones.LeftThumbIntermediate: return (int)HumanBodyBones.LeftThumbDistal;
                case HumanBodyBones.LeftIndexProximal: return (int)HumanBodyBones.LeftIndexIntermediate;
                case HumanBodyBones.LeftIndexIntermediate: return (int)HumanBodyBones.LeftIndexDistal;
                case HumanBodyBones.LeftMiddleProximal: return (int)HumanBodyBones.LeftMiddleIntermediate;
                case HumanBodyBones.LeftMiddleIntermediate: return (int)HumanBodyBones.LeftMiddleDistal;
                case HumanBodyBones.LeftRingProximal: return (int)HumanBodyBones.LeftRingIntermediate;
                case HumanBodyBones.LeftRingIntermediate: return (int)HumanBodyBones.LeftRingDistal;
                case HumanBodyBones.LeftLittleProximal: return (int)HumanBodyBones.LeftLittleIntermediate;
                case HumanBodyBones.LeftLittleIntermediate: return (int)HumanBodyBones.LeftLittleDistal;
                case HumanBodyBones.RightThumbProximal: return (int)HumanBodyBones.RightThumbIntermediate;
                case HumanBodyBones.RightThumbIntermediate: return (int)HumanBodyBones.RightThumbDistal;
                case HumanBodyBones.RightIndexProximal: return (int)HumanBodyBones.RightIndexIntermediate;
                case HumanBodyBones.RightIndexIntermediate: return (int)HumanBodyBones.RightIndexDistal;
                case HumanBodyBones.RightMiddleProximal: return (int)HumanBodyBones.RightMiddleIntermediate;
                case HumanBodyBones.RightMiddleIntermediate: return (int)HumanBodyBones.RightMiddleDistal;
                case HumanBodyBones.RightRingProximal: return (int)HumanBodyBones.RightRingIntermediate;
                case HumanBodyBones.RightRingIntermediate: return (int)HumanBodyBones.RightRingDistal;
                case HumanBodyBones.RightLittleProximal: return (int)HumanBodyBones.RightLittleIntermediate;
                case HumanBodyBones.RightLittleIntermediate: return (int)HumanBodyBones.RightLittleDistal;
            }
            return -1;
        }

        private Vector3 ClosestPointOnSegment(Vector3 point, Vector3 start, Vector3 end)
        {
            Vector3 segment = end - start;
            float sqrLength = segment.sqrMagnitude;
            if (sqrLength <= 0.000001f)
                return start;

            float t = Mathf.Clamp01(Vector3.Dot(point - start, segment) / sqrLength);
            return start + segment * t;
        }

        private void CreateBodyColliderPool()
        {
            colliderPoolReady = false;
            if (!enableBodyColliders || !Utilities.IsValid(bodyColliderTemplate) || !Utilities.IsValid(bodyColliderPoolRoot))
                return;

            bodyColliderObjects = new GameObject[MaxBodyColliders];
            bodyColliders = new CapsuleCollider[MaxBodyColliders];
            bodyShapePreviews = new GameObject[MaxBodyColliders];
            headAccessoryPreviews = new GameObject[MaxColliderPlayers];
            GameObject previewTemplate = bodyColliderTemplate.transform.childCount > 0
                ? bodyColliderTemplate.transform.GetChild(0).gameObject
                : null;
            for (int i = 0; i < MaxBodyColliders; i++)
            {
                GameObject instance = i == 0 ? bodyColliderTemplate : Instantiate(bodyColliderTemplate);
                instance.name = "Body Collider " + i;
                instance.transform.SetParent(bodyColliderPoolRoot, false);
                instance.layer = bodyColliderLayer;
                CapsuleCollider capsule = instance.GetComponent<CapsuleCollider>();
                if (!Utilities.IsValid(capsule))
                {
                    instance.SetActive(false);
                    continue;
                }

                capsule.isTrigger = false;
                capsule.direction = 1;
                instance.SetActive(false);
                bodyColliderObjects[i] = instance;
                bodyColliders[i] = capsule;
                if (instance.transform.childCount > 0)
                    bodyShapePreviews[i] = instance.transform.GetChild(0).gameObject;
            }

            if (Utilities.IsValid(previewTemplate))
            {
                for (int i = 0; i < MaxColliderPlayers; i++)
                {
                    GameObject preview = Instantiate(previewTemplate);
                    preview.name = "Head Accessory Preview " + i;
                    preview.transform.SetParent(bodyColliderPoolRoot, false);
                    Renderer previewRenderer = preview.GetComponent<Renderer>();
                    if (Utilities.IsValid(previewRenderer) && Utilities.IsValid(headAccessoryPreviewMaterial))
                        previewRenderer.sharedMaterial = headAccessoryPreviewMaterial;
                    preview.SetActive(false);
                    headAccessoryPreviews[i] = preview;
                }
            }

            bodyColliderTemplate.SetActive(false);
            colliderPoolReady = true;
        }

        private void UpdateBodyColliders()
        {
            if (!enableBodyColliders || bodyColliderLayer < 22 || bodyColliderLayer > 31 || !colliderPoolReady)
            {
                DisableActiveBodyColliders();
                return;
            }

            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (!Utilities.IsValid(localPlayer) || currentHeldHandMask == 0)
            {
                DisableActiveBodyColliders();
                return;
            }

            VRCPlayerApi.GetPlayers(players);
            int nextCollider = 0;
            Vector3 localPosition = localPlayer.GetPosition();
            float maximumDistanceSqr = bodyColliderDistance * bodyColliderDistance;
            int candidateCount = CollectColliderCandidates(localPosition, maximumDistanceSqr);
            int colliderPlayerCount = 0;
            int nextAccessoryPreview = 0;

            for (int i = 0; i < candidateCount && colliderPlayerCount < MaxColliderPlayers; i++)
            {
                VRCPlayerApi player = colliderCandidates[i];
                if (!Utilities.IsValid(player))
                    continue;

                int playerStartCollider = nextCollider;
                for (int boneValue = 0; boneValue < (int)HumanBodyBones.LastBone; boneValue++)
                {
                    if (IsFingerBone(boneValue) ||
                        (player.isLocal && IsArmExcluded(boneValue, currentHeldHandMask)))
                        continue;

                    Vector3 shapeStart;
                    Vector3 shapeEnd;
                    float shapeRadius;
                    if (!TryGetBodyShape(player, boneValue, out shapeStart, out shapeEnd, out shapeRadius))
                        continue;

                    nextCollider = AddBodyShape(nextCollider, shapeStart, shapeEnd, shapeRadius);
                }

                if (nextCollider == playerStartCollider)
                {
                    float scale = GetAvatarScale(player);
                    VRCPlayerApi.TrackingData head = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                    nextCollider = AddBodyShape(nextCollider, head.position, head.position, headRadius * scale);

                    if (!player.isLocal || (currentHeldHandMask & LeftHandMask) == 0)
                    {
                        VRCPlayerApi.TrackingData left = player.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand);
                        nextCollider = AddBodyShape(nextCollider, left.position, left.position, handRadius * scale);
                    }
                    if (!player.isLocal || (currentHeldHandMask & RightHandMask) == 0)
                    {
                        VRCPlayerApi.TrackingData right = player.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand);
                        nextCollider = AddBodyShape(nextCollider, right.position, right.position, handRadius * scale);
                    }
                }

                if (nextCollider > playerStartCollider)
                    colliderPlayerCount++;

                if (showBodyShape && enableHeadAccessories && nextAccessoryPreview < headAccessoryPreviews.Length)
                {
                    Vector3 headCenter;
                    Vector3 headUp;
                    int headType;
                    Vector3 headPosition;
                    Quaternion headRotation;
                    float headScale;
                    if (TryGetHeadReference(player, out headCenter, out headUp, out headType,
                            out headPosition, out headRotation, out headScale) &&
                        SetHeadAccessoryPreview(nextAccessoryPreview, headCenter, headUp, headScale))
                        nextAccessoryPreview++;
                }
            }

            for (int i = nextCollider; i < activeBodyColliderCount; i++)
            {
                if (Utilities.IsValid(bodyColliderObjects[i]))
                    bodyColliderObjects[i].SetActive(false);
            }
            activeBodyColliderCount = nextCollider;

            for (int i = nextAccessoryPreview; i < activeHeadAccessoryPreviewCount; i++)
            {
                if (Utilities.IsValid(headAccessoryPreviews[i]))
                    headAccessoryPreviews[i].SetActive(false);
            }
            activeHeadAccessoryPreviewCount = nextAccessoryPreview;
        }

        private bool SetHeadAccessoryPreview(int index, Vector3 center, Vector3 up, float scale)
        {
            GameObject preview = headAccessoryPreviews[index];
            if (!Utilities.IsValid(preview))
                return false;

            Vector3 start = center + up * (HeadAccessoryBottom * scale);
            Vector3 end = center + up * (headAccessoryHeight * scale);
            Vector3 delta = end - start;
            float radius = headAccessoryRadius * scale;
            Transform previewTransform = preview.transform;
            previewTransform.SetPositionAndRotation((start + end) * 0.5f,
                Quaternion.FromToRotation(Vector3.up, delta));
            previewTransform.localScale = new Vector3(radius * 2f,
                (delta.magnitude + radius * 2f) * 0.5f, radius * 2f);
            preview.SetActive(true);
            return true;
        }

        private int CollectColliderCandidates(Vector3 localPosition, float maximumDistanceSqr)
        {
            int candidateCount = 0;
            for (int i = 0; i < players.Length; i++)
            {
                VRCPlayerApi player = players[i];
                if (!Utilities.IsValid(player))
                    continue;

                float distanceSqr = (player.GetPosition() - localPosition).sqrMagnitude;
                if (distanceSqr > maximumDistanceSqr)
                    continue;

                int insertIndex = candidateCount;
                while (insertIndex > 0 && distanceSqr < colliderCandidateDistances[insertIndex - 1])
                {
                    colliderCandidates[insertIndex] = colliderCandidates[insertIndex - 1];
                    colliderCandidateDistances[insertIndex] = colliderCandidateDistances[insertIndex - 1];
                    insertIndex--;
                }

                colliderCandidates[insertIndex] = player;
                colliderCandidateDistances[insertIndex] = distanceSqr;
                candidateCount++;
            }

            return candidateCount;
        }

        private int AddBodyShape(int index, Vector3 start, Vector3 end, float radius)
        {
            if (start == Vector3.zero || end == Vector3.zero || radius <= 0f || index >= bodyColliders.Length)
                return index;

            Vector3 delta = end - start;
            float length = delta.magnitude;
            GameObject colliderObject = bodyColliderObjects[index];
            CapsuleCollider capsule = bodyColliders[index];
            if (!Utilities.IsValid(colliderObject) || !Utilities.IsValid(capsule))
                return index;

            Transform colliderTransform = colliderObject.transform;
            Quaternion rotation = length > 0.0001f
                ? Quaternion.FromToRotation(Vector3.up, delta)
                : Quaternion.identity;
            colliderTransform.SetPositionAndRotation((start + end) * 0.5f, rotation);
            colliderTransform.localScale = Vector3.one;
            capsule.center = Vector3.zero;
            capsule.radius = radius;
            capsule.height = length + radius * 2f;
            colliderObject.layer = bodyColliderLayer;

            GameObject preview = bodyShapePreviews[index];
            if (Utilities.IsValid(preview))
            {
                Transform previewTransform = preview.transform;
                previewTransform.localPosition = Vector3.zero;
                previewTransform.localRotation = Quaternion.identity;
                previewTransform.localScale = new Vector3(radius * 2f, capsule.height * 0.5f, radius * 2f);
                preview.SetActive(showBodyShape);
            }
            colliderObject.SetActive(true);
            return index + 1;
        }

        private void UpdateHeldPenHands(VRCPlayerApi localPlayer)
        {
            currentHeldHandMask = 0;
            if (!Utilities.IsValid(localPlayer))
                return;

            if (lastHeldHandMasks.Length != targetPickups.Length)
                lastHeldHandMasks = new int[targetPickups.Length];

            VRC_Pickup right = localPlayer.GetPickupInHand(VRC_Pickup.PickupHand.Right);
            VRC_Pickup left = localPlayer.GetPickupInHand(VRC_Pickup.PickupHand.Left);
            for (int i = 0; i < targetPickups.Length; i++)
            {
                VRC_Pickup pickup = targetPickups[i];
                if (!Utilities.IsValid(pickup))
                    continue;

                int handMask = 0;
                if (pickup == left)
                    handMask = LeftHandMask;
                else if (pickup == right)
                    handMask = RightHandMask;

                if (handMask == 0)
                    continue;

                lastHeldHandMasks[i] = handMask;
                currentHeldHandMask |= handMask;
            }
        }

        private int GetDrawingHandMask(int penIndex)
        {
            int penHandMask = 0;
            if (penIndex >= 0 && penIndex < targetPickups.Length)
            {
                VRC_Pickup pickup = targetPickups[penIndex];
                if (Utilities.IsValid(pickup))
                {
                    VRCPlayerApi holdingPlayer = pickup.currentPlayer;
                    if (Utilities.IsValid(holdingPlayer) && holdingPlayer.isLocal)
                    {
                        if (pickup.currentHand == VRC_Pickup.PickupHand.Left)
                            penHandMask = LeftHandMask;
                        else if (pickup.currentHand == VRC_Pickup.PickupHand.Right)
                            penHandMask = RightHandMask;
                    }
                }

                if (penHandMask == 0 && penIndex < lastHeldHandMasks.Length)
                    penHandMask = lastHeldHandMasks[penIndex];
            }
            return currentHeldHandMask | penHandMask;
        }

        private void DisableActiveBodyColliders()
        {
            for (int i = 0; i < activeBodyColliderCount; i++)
            {
                if (Utilities.IsValid(bodyColliderObjects[i]))
                    bodyColliderObjects[i].SetActive(false);
            }
            activeBodyColliderCount = 0;
            for (int i = 0; i < activeHeadAccessoryPreviewCount; i++)
            {
                if (Utilities.IsValid(headAccessoryPreviews[i]))
                    headAccessoryPreviews[i].SetActive(false);
            }
            activeHeadAccessoryPreviewCount = 0;
        }

        private void ResolveTargetReferences()
        {
            int length = targetedPens == null ? 0 : targetedPens.Length;
            if (targetLateSyncs == null || targetLateSyncs.Length != length)
                targetLateSyncs = new QvPen_LateSync[length];
            if (targetPickups == null || targetPickups.Length != length)
                targetPickups = new VRC_Pickup[length];
            if (lastHeldHandMasks == null || lastHeldHandMasks.Length != length)
                lastHeldHandMasks = new int[length];

            for (int i = 0; i < length; i++)
            {
                QvPen_PenManager penManager = targetedPens[i];
                if (!Utilities.IsValid(penManager))
                {
                    targetLateSyncs[i] = null;
                    targetPickups[i] = null;
                    continue;
                }

                if (!Utilities.IsValid(targetLateSyncs[i]))
                    targetLateSyncs[i] = penManager.GetComponentInChildren<QvPen_LateSync>(true);
                if (!Utilities.IsValid(targetPickups[i]))
                    targetPickups[i] = penManager.GetComponentInChildren<VRC_Pickup>(true);
            }
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        private void OnValidate()
        {
            ResolveTargetReferences();
            if (surfaceBindingDistance < 0f)
                surfaceBindingDistance = 0f;
            if (fingerRadius < 0f)
                fingerRadius = 0f;
            if (headAccessoryHeight < 0f)
                headAccessoryHeight = 0f;
            if (headAccessoryRadius < 0f)
                headAccessoryRadius = 0f;
            if (bodyColliderDistance < 0f)
                bodyColliderDistance = 0f;
        }

        public void RefreshTargetReferences()
        {
            ResolveTargetReferences();
        }
#endif
    }
}
