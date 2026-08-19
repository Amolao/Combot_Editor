using System;
using System.Collections.Generic;
using UnityEngine;

namespace NewCombatSystem.CombatEditor
{
    public sealed class CombatSequencePlayer : MonoBehaviour
    {
        [SerializeField] private CombatSequenceAsset sequence;
        [SerializeField] private CombatSequencePreviewBindings previewBindings;
        [SerializeField] private bool playOnStart;
        [SerializeField] private bool loop;
        [SerializeField] private bool restoreBasePositionOnStop;
        [SerializeField] private float timeScale = 1f;
        [SerializeField] private bool logTriggeredClips = true;
        [SerializeField] private bool logMovementTrack = true;

        private readonly HashSet<string> activeClipIds = new HashSet<string>();
        private readonly List<GameObject> spawnedEffects = new List<GameObject>();

        private float currentTime;
        private Vector3 baseMovementPosition;
        private Vector3 pendingMovementTarget;
        private bool hasPendingMovementTarget;
        private bool isPlaying;

        public event Action<CombatTrack, CombatClip> ClipStarted;
        public event Action<CombatTrack, CombatClip> ClipFinished;
        public event Action<CombatClip> GameplayEventFired;

        public CombatSequenceAsset Sequence => sequence;

        public float CurrentTime => currentTime;

        public bool IsPlaying => isPlaying;

        public void Play(CombatSequenceAsset asset)
        {
            if (asset == null)
            {
                return;
            }

            sequence = asset;
            Play();
        }

        private void Start()
        {
            if (playOnStart && sequence != null)
            {
                Play();
            }
        }

        private void Update()
        {
            if (!isPlaying || sequence == null)
            {
                return;
            }

            float previousTime = currentTime;
            currentTime += Time.deltaTime * Mathf.Max(0f, timeScale);

            if (currentTime >= sequence.Duration)
            {
                Evaluate(previousTime, sequence.Duration);

                if (loop)
                {
                    StopInternal(false);
                    currentTime = 0f;
                    isPlaying = true;
                    Evaluate(0f, 0f);
                    return;
                }

                StopInternal(true);
                return;
            }

            Evaluate(previousTime, currentTime);
        }

        private void LateUpdate()
        {
            if (!hasPendingMovementTarget)
            {
                return;
            }

            ApplyMovementTarget(pendingMovementTarget);
            hasPendingMovementTarget = false;
        }

        public void Play()
        {
            if (sequence == null)
            {
                return;
            }

            sequence.EnsureValid();
            currentTime = 0f;
            activeClipIds.Clear();
            baseMovementPosition = GetMovementRoot().position;
            ClearSpawnedEffects();
            isPlaying = true;
            Evaluate(0f, 0f);
            ApplyFrameState();
        }

        public void Stop()
        {
            StopInternal(true);
        }

        public void SetTime(float time)
        {
            if (sequence == null)
            {
                currentTime = 0f;
                activeClipIds.Clear();
                return;
            }

            sequence.EnsureValid();
            currentTime = Mathf.Clamp(time, 0f, sequence.Duration);
            activeClipIds.Clear();
            Evaluate(currentTime, currentTime);
            ApplyFrameState();
        }

        private void StopInternal(bool notifyFinished)
        {
            if (notifyFinished && sequence != null)
            {
                NotifyFinishedActiveClips();
            }

            isPlaying = false;
            activeClipIds.Clear();

            if (restoreBasePositionOnStop)
            {
                ResetMovementRoot();
            }

            hasPendingMovementTarget = false;
            ClearSpawnedEffects();
        }

        private void NotifyFinishedActiveClips()
        {
            foreach (CombatTrack track in sequence.Tracks)
            {
                if (track == null || track.muted)
                {
                    continue;
                }

                foreach (CombatClip clip in track.clips)
                {
                    if (clip != null && activeClipIds.Contains(clip.guid))
                    {
                        ClipFinished?.Invoke(track, clip);
                    }
                }
            }
        }

        private void Evaluate(float previousTime, float newTime)
        {
            foreach (CombatTrack track in sequence.Tracks)
            {
                if (track == null || track.muted)
                {
                    continue;
                }

                foreach (CombatClip clip in track.clips)
                {
                    if (clip == null)
                    {
                        continue;
                    }

                    bool wasActive = activeClipIds.Contains(clip.guid);
                    bool isActive = clip.startTime <= newTime && clip.EndTime >= newTime;
                    bool startsNow = clip.startTime > previousTime && clip.startTime <= newTime;
                    bool firstFrame = Mathf.Approximately(previousTime, 0f) &&
                        Mathf.Approximately(newTime, 0f) &&
                        Mathf.Approximately(clip.startTime, 0f);

                    if (!wasActive && (isActive || startsNow || firstFrame))
                    {
                        activeClipIds.Add(clip.guid);
                        ClipStarted?.Invoke(track, clip);

                        if (logTriggeredClips)
                        {
                            Debug.Log($"[CombatSequence] Start {track.trackType} -> {clip.displayName}", this);
                        }

                        HandleClipStarted(track, clip);
                    }

                    if (wasActive && clip.EndTime < newTime)
                    {
                        activeClipIds.Remove(clip.guid);
                        ClipFinished?.Invoke(track, clip);

                        if (logTriggeredClips)
                        {
                            Debug.Log($"[CombatSequence] End {track.trackType} -> {clip.displayName}", this);
                        }
                    }
                }
            }

            ApplyFrameState();
        }

        private void HandleClipStarted(CombatTrack track, CombatClip clip)
        {
            switch (track.trackType)
            {
                case CombatTrackType.Animation:
                    PlayAnimationClip(clip);
                    break;
                case CombatTrackType.Effect:
                    SpawnEffect(clip);
                    break;
                case CombatTrackType.Audio:
                    PlayAudio(clip);
                    break;
                case CombatTrackType.Event:
                    GameplayEventFired?.Invoke(clip);
                    break;
            }
        }

        private void ApplyFrameState()
        {
            if (sequence == null)
            {
                return;
            }

            QueueMovement();
            UpdatePreviewGizmos();
        }

        private void QueueMovement()
        {
            Transform movementRoot = GetMovementRoot();
            if (movementRoot == null)
            {
                return;
            }

            Vector3 worldOffset = Vector3.zero;
            foreach (CombatTrack track in sequence.Tracks)
            {
                if (track == null || track.trackType != CombatTrackType.Movement || track.muted)
                {
                    continue;
                }

                foreach (CombatClip clip in track.clips)
                {
                    if (clip == null || currentTime < clip.startTime || currentTime > clip.EndTime)
                    {
                        continue;
                    }

                    float progress = Mathf.InverseLerp(clip.startTime, clip.EndTime, currentTime);
                    float weight = clip.moveCurve == null ? progress : clip.moveCurve.Evaluate(progress);
                    worldOffset += GetOwnerRoot().TransformDirection(clip.moveOffset) * weight;
                }
            }

            Vector3 targetPosition = baseMovementPosition + worldOffset;

            if (logMovementTrack && worldOffset.sqrMagnitude > 0.000001f)
            {
                Debug.Log(
                    $"[CombatSequence][Movement] time={currentTime:F3}, root={movementRoot.name}, " +
                    $"base={baseMovementPosition}, offset={worldOffset}, target={targetPosition}",
                    this);
            }

            pendingMovementTarget = targetPosition;
            hasPendingMovementTarget = true;
        }

        private void PlayAnimationClip(CombatClip clip)
        {
            if (previewBindings == null || previewBindings.Animator == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(clip.animationState))
            {
                previewBindings.Animator.Play(clip.animationState, 0, 0f);
                previewBindings.Animator.speed = Mathf.Max(0.01f, clip.animationSpeed);
            }
        }

        private void SpawnEffect(CombatClip clip)
        {
            if (clip.effectPrefab == null)
            {
                return;
            }

            Transform effectRoot = previewBindings != null ? previewBindings.EffectRoot : transform;
            Vector3 position = GetOwnerRoot().TransformPoint(clip.effectOffset);
            Quaternion rotation = GetOwnerRoot().rotation * Quaternion.Euler(clip.effectRotation);
            GameObject instance = Instantiate(clip.effectPrefab, position, rotation, effectRoot);
            instance.transform.localScale = clip.effectScale;
            spawnedEffects.Add(instance);
            Destroy(instance, Mathf.Max(clip.duration, 0.05f));
        }

        private void PlayAudio(CombatClip clip)
        {
            if (clip.audioClip == null || previewBindings == null || previewBindings.AudioSource == null)
            {
                return;
            }

            previewBindings.AudioSource.PlayOneShot(clip.audioClip, clip.audioVolume);
        }

        private void ResetMovementRoot()
        {
            ApplyMovementTarget(baseMovementPosition);
        }

        private void ApplyMovementTarget(Vector3 targetPosition)
        {
            Transform movementRoot = GetMovementRoot();
            if (movementRoot == null)
            {
                return;
            }

            CharacterController characterController = movementRoot.GetComponent<CharacterController>();
            if (characterController != null && characterController.enabled)
            {
                Vector3 delta = targetPosition - movementRoot.position;
                characterController.Move(delta);
                return;
            }

            Rigidbody rigidbody = movementRoot.GetComponent<Rigidbody>();
            if (rigidbody != null && !rigidbody.isKinematic)
            {
                rigidbody.MovePosition(targetPosition);
                return;
            }

            movementRoot.position = targetPosition;
        }

        private void ClearSpawnedEffects()
        {
            for (int i = spawnedEffects.Count - 1; i >= 0; i--)
            {
                if (spawnedEffects[i] != null)
                {
                    Destroy(spawnedEffects[i]);
                }
            }

            spawnedEffects.Clear();
        }

        private void UpdatePreviewGizmos()
        {
            if (previewBindings != null)
            {
                previewBindings.SetPreviewState(sequence, currentTime);
            }
        }

        private Transform GetOwnerRoot()
        {
            return previewBindings != null ? previewBindings.OwnerRoot : transform;
        }

        private Transform GetMovementRoot()
        {
            return previewBindings != null ? previewBindings.RuntimeMovementRoot : transform;
        }
    }
}
