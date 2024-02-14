using UnityEngine;

namespace RuntimeMeshSimplification.Sample
{
    public class SimplifyItSample : MonoBehaviour
    {
        [SerializeField] private Transform _parent;
        [SerializeField, Range(0, 1)] private float _quality = .1f;

        private async void Start()
        {
            var filters = _parent.GetComponentsInChildren<MeshFilter>(true);
            foreach (MeshFilter filter in filters)
            {
                RuntimeSimplifier simplifier = new(filter.mesh);
                filter.mesh = await simplifier.Simplify(_quality);
            }
        }
    }
}