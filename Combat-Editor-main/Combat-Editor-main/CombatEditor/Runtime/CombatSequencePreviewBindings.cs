using UnityEngine;

namespace NewCombatSystem.CombatEditor
{
    public sealed class CombatSequencePreviewBindings : MonoBehaviour
    {
        [SerializeField] private Transform ownerRoot;
        [SerializeField] private Transform animationRoot;
        [SerializeField] private Transform movementRoot;
        [SerializeField] private Transform runtimeMovementRoot;
        [SerializeField] private Transform effectRoot;
        [SerializeField] private Animator animator;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private bool drawHitboxGizmos = true;
        [SerializeField] private bool drawMovementGizmos = true;

        [SerializeField, HideInInspector] private CombatSequenceAsset previewSequence;
        [SerializeField, HideInInspector] private float previewTime;
        [SerializeField, HideInInspector] private Vector3 previewBasePosition;
        [SerializeField, HideInInspector] private bool hasPreviewBasePosition;

        public Transform OwnerRoot => ownerRoot != null ? ownerRoot : transform;

        public Transform AnimationRoot => animationRoot != null ? animationRoot : OwnerRoot;

        public Transform MovementRoot => movementRoot != null ? movementRoot : OwnerRoot;

        public Transform RuntimeMovementRoot => runtimeMovementRoot != null ? runtimeMovementRoot : MovementRoot;

        public Transform EffectRoot => effectRoot != null ? effectRoot : OwnerRoot;

        public Animator Animator => animator;

        public AudioSource AudioSource => audioSource;

        public void CapturePreviewBasePose()
        {
            if (hasPreviewBasePosition)
            {
                return;
            }

            previewBasePosition = MovementRoot.position;
            hasPreviewBasePosition = true;
        }

        public void RestorePreviewBasePose()
        {
            if (!hasPreviewBasePosition)
            {
                return;
            }

            MovementRoot.position = previewBasePosition;
            hasPreviewBasePosition = false;
        }

        public Vector3 GetPreviewBasePosition()
        {
            if (!hasPreviewBasePosition)
            {
                CapturePreviewBasePose();
            }

            return previewBasePosition;
        }

        public Vector3 ResolveWorldPoint(Vector3 localOffset)
        {
            return OwnerRoot.TransformPoint(localOffset);
        }

        public Vector3 ResolveWorldDirection(Vector3 localDirection)
        {
            return OwnerRoot.TransformDirection(localDirection);
        }

        public void SetPreviewState(CombatSequenceAsset sequence, float time)
        {
            previewSequence = sequence;
            previewTime = time;
        }

        private void OnDrawGizmosSelected()
        {
            if (previewSequence == null)
            {
                return;
            }

            if (drawMovementGizmos)
            {
                DrawMovementGizmos();
            }

            if (drawHitboxGizmos)
            {
                DrawHitboxGizmos();
            }
        }

        private void DrawMovementGizmos()
        {
            foreach (CombatTrack track in previewSequence.Tracks)
            {
                if (track == null || track.trackType != CombatTrackType.Movement || track.muted)
                {
                    continue;
                }

                foreach (CombatClip clip in track.clips)
                {
                    if (clip == null)
                    {
                        continue;
                    }

                    Vector3 from = GetPreviewBasePosition();
                    Vector3 to = from + ResolveWorldDirection(clip.moveOffset);
                    Color color = Color.Lerp(track.color, Color.white, 0.15f);
                    Gizmos.color = new Color(color.r, color.g, color.b, 0.6f);
                    Gizmos.DrawLine(from, to);

                    if (IsClipActive(clip, previewTime))
                    {
                        Gizmos.DrawSphere(MovementRoot.position, 0.08f);
                    }
                }
            }
        }

        private void DrawHitboxGizmos()
        {
            foreach (CombatTrack track in previewSequence.Tracks)
            {
                if (track == null || track.trackType != CombatTrackType.Hitbox || track.muted)
                {
                    continue;
                }

                foreach (CombatClip clip in track.clips)
                {
                    if (clip == null || !IsClipActive(clip, previewTime))
                    {
                        continue;
                    }

                    Vector3 center = ResolveWorldPoint(clip.hitboxOffset);
                    Color color = Color.Lerp(track.color, Color.white, 0.1f);
                    Gizmos.color = new Color(color.r, color.g, color.b, 0.45f);
                    Gizmos.DrawSphere(center, clip.hitboxRadius);
                    Gizmos.color = new Color(color.r, color.g, color.b, 0.9f);
                    Gizmos.DrawWireSphere(center, clip.hitboxRadius);
                }
            }
        }

        private static bool IsClipActive(CombatClip clip, float time)
        {
            return clip.startTime <= time && clip.EndTime >= time;
        }
    }
}
