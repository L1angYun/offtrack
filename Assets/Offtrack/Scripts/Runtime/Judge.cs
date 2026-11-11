using System;
using Offtrack.Core;
using UnityEngine;

namespace Offtrack.Runtime
{
    /// <summary>Relays rule-core hit data for consumers and diagnostics; it has no visual effects.</summary>
    [DisallowMultipleComponent]
    public sealed class Judge : MonoBehaviour
    {
        [SerializeField] private GameFlow flow;
        private bool subscribed;

        public event Action<DamageEvent> DamageTaken;

        public void Configure(GameFlow gameFlow)
        {
            Unsubscribe();
            flow = gameFlow;
            if (isActiveAndEnabled) Subscribe();
        }

        private void OnEnable() => Subscribe();
        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            if (subscribed || flow == null) return;
            flow.DamageTaken += OnDamageTaken;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            if (flow != null) flow.DamageTaken -= OnDamageTaken;
            subscribed = false;
        }

        private void OnDamageTaken(DamageEvent damage)
        {
            Debug.Log($"[OFFTRACK] scratch={damage.ScratchId} lane={damage.Lane} kind={damage.Kind} lives={damage.LivesAfter}", this);
            DamageTaken?.Invoke(damage);
        }
    }
}
