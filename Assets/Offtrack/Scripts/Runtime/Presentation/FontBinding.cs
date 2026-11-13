using UnityEngine;
using UnityEngine.UI;

namespace Offtrack.Runtime.Presentation
{
    [RequireComponent(typeof(Text))]
    public sealed class FontBinding : MonoBehaviour
    {
        [Tooltip("Machine print: digits and Latin printing on the turntable use a system monospace font.")]
        public bool Monospace;

        private void OnEnable() => FontProvider.Apply(GetComponent<Text>(), Monospace);
    }
}
