using UnityEngine;

namespace Offtrack.Runtime.Presentation
{
    public abstract class FlowView : MonoBehaviour
    {
        [SerializeField] protected GameFlow flow;

        protected void Bind(GameFlow value)
        {
            if (flow != null) flow.Changed -= Refresh;
            flow = value;
            if (isActiveAndEnabled && flow != null) flow.Changed += Refresh;
            Refresh();
        }

        protected virtual void OnEnable()
        {
            if (flow == null) return;
            flow.Changed += Refresh;
            Refresh();
        }

        protected virtual void OnDisable()
        {
            if (flow != null) flow.Changed -= Refresh;
        }

        public abstract void Refresh();
    }
}
