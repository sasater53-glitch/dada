using System.Text;
using CityBiz.Core;
using UnityEngine;

namespace CityBiz.Game
{
    /// <summary>Домофон жилого дома, ресепшен бизнес-центра, паркомат — точка доступа к нескольким объектам.</summary>
    public sealed class DirectoryPoint : MonoBehaviour, IInteractable
    {
        [SerializeField] string _title;
        [SerializeField] string _prompt;
        [SerializeField] string[] _lots;
        [SerializeField] Vector3 _localInteract;
        [SerializeField] TextMesh _board;
        int[] _propertyIds;
        float _boardTimer;

        public string Title => _title;

        public void Configure(string title, string prompt, string[] lots, Vector3 localInteract)
        {
            _title = title; _prompt = prompt; _lots = lots; _localInteract = localInteract;
        }

        public void SetDirectoryBoard(TextMesh board) => _board = board;

        public int[] PropertyIds
        {
            get
            {
                if (_propertyIds != null) return _propertyIds;
                var game = CityGame.Instance;
                var district = GetComponentInParent<DistrictInstance>();
                if (game == null || game.World == null || district == null) return new int[0];
                _propertyIds = new int[_lots.Length];
                for (int i = 0; i < _lots.Length; i++)
                {
                    var def = game.World.FindByLot(district.DistrictId, _lots[i]);
                    _propertyIds[i] = def != null ? def.Id : 0;
                }
                return _propertyIds;
            }
        }

        public Vector3 InteractPosition => transform.TransformPoint(_localInteract);
        public string Prompt => $"[E] {_title} — {_prompt}";
        public bool CanInteract => CityGame.Instance != null && CityGame.Instance.Ready && _lots != null && _lots.Length > 0;
        public void Interact() => CityGame.Instance.UI.OpenDirectory(this);

        void OnEnable() => Interaction.Register(this);
        void OnDisable() => Interaction.Unregister(this);

        void Update()
        {
            if (_board == null) return;
            _boardTimer -= Time.deltaTime;
            if (_boardTimer > 0f) return;
            _boardTimer = 2f;
            var game = CityGame.Instance;
            if (game == null || !game.Ready) return;
            var sb = new StringBuilder("АРЕНДАТОРЫ\n");
            foreach (int id in PropertyIds)
            {
                var prop = game.State.GetProperty(id);
                if (prop == null) continue;
                var b = game.State.GetBusiness(prop.BusinessId);
                string office = prop.Def.Address.Contains("оф.") ? prop.Def.Address.Substring(prop.Def.Address.IndexOf("оф.")) : prop.Def.Name;
                sb.Append(office).Append("  ").Append(b != null ? b.Name : "свободен").Append('\n');
            }
            WorldText.SetText(_board, sb.ToString());
        }
    }
}
