using UnityEngine;

namespace RuntimeMeshSimplification.Sample
{
    public class SimplifyToSample : MonoBehaviour
    {
        [SerializeField] private Transform _parent;
        [SerializeField, Range(0, 1)] private float _quality = .1f;

        private async void Start()
        {
            var copy = await RuntimeSimplifier.Simplify(_parent.gameObject, _quality);
            copy.transform.position = transform.position;
        }
    }
}