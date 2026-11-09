using System.Collections.Generic;
using UnityEngine;

namespace CityBiz.Game
{
    /// <summary>Объект, с которым игрок взаимодействует клавишей E.</summary>
    public interface IInteractable
    {
        Vector3 InteractPosition { get; }
        string Prompt { get; }
        bool CanInteract { get; }
        void Interact();
    }

    public static class Interaction
    {
        public static readonly List<IInteractable> All = new List<IInteractable>();

        public static void Register(IInteractable i) { if (!All.Contains(i)) All.Add(i); }
        public static void Unregister(IInteractable i) => All.Remove(i);

        /// <summary>Ближайший доступный объект в радиусе, предпочтительно перед игроком.</summary>
        public static IInteractable FindBest(Vector3 position, Vector3 forward, float radius)
        {
            IInteractable best = null;
            float bestScore = float.MaxValue;
            foreach (var i in All)
            {
                if (!i.CanInteract) continue;
                var delta = i.InteractPosition - position;
                delta.y = 0f;
                float d = delta.magnitude;
                if (d > radius) continue;
                float facing = d > 0.01f ? Vector3.Dot(forward, delta / d) : 1f;
                float score = d * (1.6f - facing * 0.6f);
                if (score < bestScore) { bestScore = score; best = i; }
            }
            return best;
        }
    }

    /// <summary>Реестр «агентов» (игрок, NPC, машины не входят) — для автоматических дверей.</summary>
    public static class Agents
    {
        public static readonly List<Transform> All = new List<Transform>(128);

        public static bool AnyWithin(Vector3 p, float radius)
        {
            float r2 = radius * radius;
            for (int i = 0; i < All.Count; i++)
            {
                var t = All[i];
                if (t == null) continue;
                var d = t.position - p;
                if (d.x * d.x + d.z * d.z < r2 && Mathf.Abs(d.y) < 2.5f) return true;
            }
            return false;
        }
    }
}
