using UnityEngine;

namespace GravityFlip
{
    /// <summary>
    /// Unity renamed several Rigidbody properties in Unity 6. Routing every access
    /// through here means the project compiles on both 2021/2022/2023 LTS and Unity 6
    /// without you having to hunt down warnings.
    ///
    /// This is a normal thing to do in real projects: isolate the parts of an API that
    /// churn between versions into one small file, so a rename is a one-line fix
    /// instead of a find-and-replace across the whole codebase.
    /// </summary>
    public static class RigidbodyCompat
    {
        public static Vector3 GetVelocity(this Rigidbody rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }

        public static void SetVelocity(this Rigidbody rb, Vector3 value)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = value;
#else
            rb.velocity = value;
#endif
        }

        public static void SetLinearDamping(this Rigidbody rb, float value)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearDamping = value;
#else
            rb.drag = value;
#endif
        }

        public static void SetAngularDamping(this Rigidbody rb, float value)
        {
#if UNITY_6000_0_OR_NEWER
            rb.angularDamping = value;
#else
            rb.angularDrag = value;
#endif
        }
    }
}
