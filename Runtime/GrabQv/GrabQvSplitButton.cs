using QvPen.UdonScript;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace Maaaaa.EXQv
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class GrabQvSplitButton : UdonSharpBehaviour
    {
        [SerializeField] private GrabQvManager manager;
        [SerializeField] private QvPen_PenManager pen;
        [SerializeField] private Image background;
        [SerializeField] private Color normalColor = Color.white;
        [SerializeField] private Color pressedColor = new Color(1f, 0.69f, 0.38f, 1f);

        public GrabQvManager Manager => manager;
        public QvPen_PenManager Pen => pen;

        public override void Interact()
        {
            if (manager != null && pen != null)
                manager.SplitPen(pen);

            if (background != null)
            {
                background.color = pressedColor;
                SendCustomEventDelayedSeconds(nameof(ResetFeedback), 0.3f);
            }
        }

        public void ResetFeedback()
        {
            if (background != null)
                background.color = normalColor;
        }
    }
}
