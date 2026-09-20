using System;
using System.Collections;
using UnityEngine;

namespace RossPortals.Game.Portals
{
    /// <summary>
    /// A tiny "do this a few frames later" helper, hung on the mod's host
    /// GameObject. Two things genuinely need it: a server-side ZDO write for a
    /// just-placed portal can arrive before the ZDO has fully materialised (so we
    /// retry next frame), and a freshly-shown input field only takes focus a
    /// frame after it's enabled. Kept deliberately small — this is not a job
    /// queue, just deferred callbacks.
    /// </summary>
    internal sealed class Scheduler : MonoBehaviour
    {
        public static Scheduler Instance { get; private set; }

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void NextFrame(Action action) => AfterFrames(1, action);

        public void AfterFrames(int frames, Action action)
        {
            if (action == null) return;
            if (!isActiveAndEnabled) { action(); return; }
            StartCoroutine(Run(frames, action));
        }

        private static IEnumerator Run(int frames, Action action)
        {
            for (int i = 0; i < frames; i++) yield return null;
            action();
        }
    }
}
