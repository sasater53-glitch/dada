using UnityEngine;

namespace CityBiz.Game
{
    /// <summary>Навигационный стенд «Схема ТЦ»: E открывает поэтажный план на текущем этаже.</summary>
    public sealed class MallDirectoryPoint : MonoBehaviour, IInteractable
    {
        [SerializeField] Vector3 _localInteract;

        public void Configure(Vector3 localInteract) => _localInteract = localInteract;

        public MallInstance Mall => GetComponentInParent<MallInstance>();
        public int Floor
        {
            get
            {
                var mall = Mall;
                return mall != null ? mall.FloorOfWorld(transform.position) : 0;
            }
        }

        public Vector3 InteractPosition => transform.TransformPoint(_localInteract);
        public string Prompt => "[E] Схема ТЦ — найти свободные помещения";
        public bool CanInteract => CityGame.Instance != null && CityGame.Instance.Ready;
        public void Interact() => CityGame.Instance?.UI?.OpenMap(Mall, Floor);

        void OnEnable() => Interaction.Register(this);
        void OnDisable() => Interaction.Unregister(this);
    }

    /// <summary>Кнопка вызова панорамного лифта: E — выбор этажа, игрок переносится к двери лифта на нужном этаже.</summary>
    public sealed class ElevatorPanel : MonoBehaviour, IInteractable
    {
        [SerializeField] int _floor;
        [SerializeField] Vector3 _localInteract;

        public void Configure(int floor, Vector3 localInteract)
        {
            _floor = floor;
            _localInteract = localInteract;
        }

        public int Floor => _floor;
        public MallInstance Mall => GetComponentInParent<MallInstance>();

        public Vector3 InteractPosition => transform.TransformPoint(_localInteract);
        public string Prompt => $"[E] Лифт — вы на {_floor + 1} этаже";
        public bool CanInteract => CityGame.Instance != null && CityGame.Instance.Ready && Mall != null && Mall.ElevatorStops.Count > 1;
        public void Interact() => CityGame.Instance?.UI?.OpenElevator(this);

        void OnEnable() => Interaction.Register(this);
        void OnDisable() => Interaction.Unregister(this);
    }
}
