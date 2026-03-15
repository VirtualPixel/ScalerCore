using UnityEngine;

namespace ScalerCore
{
    // Attached to PlayerAvatarCollision.CollisionTransform so that a raycast
    // hitting the player's capsule can walk back to the correct ScaleController.
    // PlayerAvatar GO (where ScaleController lives) is not a parent of the
    // collision capsule GO, so GetComponentInParent won't find it from the hit.
    public class PlayerShrinkLink : MonoBehaviour
    {
        public ScaleController Controller = null!;
    }
}
