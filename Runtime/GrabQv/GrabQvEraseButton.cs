using QvPen.UdonScript;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.Udon.Common;
using VRC.Udon.Common.Interfaces;
using Utilities = VRC.SDKBase.Utilities;

namespace Maaaaa.EXQv
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class GrabQvEraseButton : QvPen_PenCallbackListener
    {
        private const float SplitSeconds = 0.31f;
        private const float SelfSeconds = 1f;
        private const float AllSeconds = 2f;
        private const float TextSeconds = 5f;

        [SerializeField] private GrabQvManager manager;
        [SerializeField] private QvPen_PenManager penManager;
        [SerializeField] private GameObject splitUi;
        [SerializeField] private Image splitIndicator;
        [SerializeField] private Image ownIndicator;
        [SerializeField] private Image allIndicator;
        [SerializeField] private Graphic splitText;
        [SerializeField] private Graphic ownTextImage;
        [SerializeField] private Graphic allTextImage;

        private bool isInteracting;
        private bool isPickedUp;
        private bool splitRan;
        private bool selfRan;
        private bool allRan;
        private bool waitingForUndo;
        private bool waitingForSelf;
        private float startedAt;
        private float textUntil;
        private bool textLoopActive;

        public GrabQvManager Manager => manager;
        public QvPen_PenManager Pen => penManager;
        public GameObject SplitUi => splitUi;
        public GameObject SplitIndicatorObject => Utilities.IsValid(splitIndicator) ? splitIndicator.gameObject : null;
        public GameObject SplitTextObject => Utilities.IsValid(splitText) ? splitText.gameObject : null;

        private void Start()
        {
            SetIndicators(false);
            SetTexts(false);
            if (Utilities.IsValid(penManager))
                penManager.Register(this);
        }

        public override void Interact()
        {
            isInteracting = true;
            splitRan = false;
            selfRan = false;
            allRan = false;
            waitingForUndo = false;
            waitingForSelf = false;
            startedAt = Time.time;
            textUntil = Time.time + TextSeconds;
            SetIndicators(true);
            EnterTextLoop();
            SendCustomEventDelayedFrames(nameof(UpdateHold), 0);
        }

        public override void InputUse(bool value, UdonInputEventArgs args)
        {
            if (value)
                return;
            if (isInteracting && splitRan && !selfRan && Time.time - startedAt >= SelfSeconds)
                waitingForSelf = true;
            if (isInteracting && !splitRan)
            {
                if (Utilities.IsValid(manager) && !manager.CanUndoAfterSplitErase(penManager))
                {
                    waitingForUndo = true;
                    SendCustomEventDelayedFrames(nameof(TryUndo), 0);
                }
                else
                {
                    UndoDraw();
                }
            }
            isInteracting = false;
            if (waitingForSelf)
            {
                SetIndicators(true);
                SetFill(0f, 1f, 0f);
                SendCustomEventDelayedFrames(nameof(UpdateHold), 0);
            }
            else
            {
                SetIndicators(false);
            }
        }

        public void TryUndo()
        {
            if (!waitingForUndo)
                return;
            if (Utilities.IsValid(manager) && !manager.CanUndoAfterSplitErase(penManager))
            {
                SendCustomEventDelayedFrames(nameof(TryUndo), 0);
                return;
            }
            waitingForUndo = false;
            UndoDraw();
        }

        public void UpdateHold()
        {
            if (!isInteracting && !waitingForSelf)
                return;
            float elapsed = Time.time - startedAt;
            if (isInteracting && !allRan && elapsed >= AllSeconds)
            {
                allRan = true;
                waitingForSelf = false;
                if (Utilities.IsValid(manager))
                    manager.StopSplitEraseForAll(penManager);
                Clear();
                SetFill(0f, 0f, 0f);
                return;
            }
            if (isInteracting && !splitRan && elapsed >= SplitSeconds)
            {
                splitRan = true;
                if (Utilities.IsValid(manager))
                    manager.EraseLatestGroup(penManager);
            }
            if (!selfRan && (waitingForSelf || isInteracting && elapsed >= SelfSeconds))
            {
                if (!Utilities.IsValid(manager) || manager.CanRunSelfAfterSplitErase(penManager))
                {
                    selfRan = true;
                    waitingForSelf = false;
                    if (Utilities.IsValid(manager))
                        manager.StopSplitEraseForSelf(penManager);
                    EraseOwnInk();
                    if (!isInteracting)
                    {
                        SetIndicators(false);
                        return;
                    }
                }
                else
                {
                    waitingForSelf = true;
                }
            }
            if (waitingForSelf && !isInteracting)
                SetFill(0f, 1f, 0f);
            else
                SetFill(splitRan ? 0f : elapsed / SplitSeconds,
                    selfRan ? 0f : elapsed / SelfSeconds, elapsed / AllSeconds);
            SendCustomEventDelayedFrames(nameof(UpdateHold), 0);
        }

        public override void OnPenPickup()
        {
            isPickedUp = true;
            EnterTextLoop();
        }

        public override void OnPenDrop()
        {
            isPickedUp = false;
            textUntil = Time.time + TextSeconds;
            EnterTextLoop();
        }

        private void SetIndicators(bool active)
        {
            SetIndicator(splitIndicator, active);
            SetIndicator(ownIndicator, active);
            SetIndicator(allIndicator, active);
        }

        private void SetIndicator(Image indicator, bool active)
        {
            if (!Utilities.IsValid(indicator))
                return;
            indicator.gameObject.SetActive(active);
            indicator.fillAmount = 0f;
        }

        private void SetFill(float split, float own, float all)
        {
            if (Utilities.IsValid(splitIndicator)) splitIndicator.fillAmount = Mathf.Clamp01(split);
            if (Utilities.IsValid(ownIndicator)) ownIndicator.fillAmount = Mathf.Clamp01(own);
            if (Utilities.IsValid(allIndicator)) allIndicator.fillAmount = Mathf.Clamp01(all);
        }

        private void SetTexts(bool active)
        {
            if (Utilities.IsValid(splitText)) splitText.gameObject.SetActive(active);
            if (Utilities.IsValid(ownTextImage)) ownTextImage.gameObject.SetActive(active);
            if (Utilities.IsValid(allTextImage)) allTextImage.gameObject.SetActive(active);
        }

        private void EnterTextLoop()
        {
            if (textLoopActive)
                return;
            textLoopActive = true;
            if (isPickedUp)
            {
                ExitTextLoop();
                return;
            }
            SetTexts(true);
            UpdateTextLoop();
        }

        private void ExitTextLoop()
        {
            textLoopActive = false;
            SetTexts(false);
        }

        public void UpdateTextLoop()
        {
            if (!textLoopActive)
                return;
            if (isPickedUp || Time.time >= textUntil)
            {
                ExitTextLoop();
                return;
            }
            SendCustomEventDelayedSeconds(nameof(UpdateTextLoop), (textUntil - Time.time) * 0.5f);
        }

        private void UndoDraw()
        {
            if (Utilities.IsValid(penManager)) penManager.UndoDraw();
        }

        private void EraseOwnInk()
        {
            if (Utilities.IsValid(penManager)) penManager.EraseOwnInk();
        }

        private void Clear()
        {
            if (Utilities.IsValid(penManager))
                penManager.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(QvPen_PenManager.Clear));
        }
    }
}
