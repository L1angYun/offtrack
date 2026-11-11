using UnityEngine;
using UnityEngine.UI;

namespace Offtrack.Runtime.Presentation
{
    [RequireComponent(typeof(Text))]
    public sealed class FontBinding : MonoBehaviour
    {
        private void OnEnable() => FontProvider.Apply(GetComponent<Text>());
    }
}
