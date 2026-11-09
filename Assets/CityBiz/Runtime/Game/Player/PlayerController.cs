using System.Collections.Generic;
using CityBiz.Core;
using CityBiz.Core.Net;
using UnityEngine;

namespace CityBiz.Game
{
    /// <summary>Игрок от третьего лица (V — от первого): ходьба, бег, прыжок, орбитальная камера с коллизией, взаимодействие.</summary>
    public sealed class PlayerController : MonoBehaviour
    {
        public static PlayerController Instance { get; private set; }

        public float WalkSpeed = 2.3f, RunSpeed = 5.6f, JumpSpeed = 4.6f, Gravity = -18f;
        public float MouseSensitivity = 0.15f;

        CharacterController _cc;
        NpcBody _body;
        Camera _cam;
        float _yaw, _pitch = 12f, _distance = 4.2f, _vy;
        bool _firstPerson;
        float _poseTimer;
        Vector3 _lastSentPos;
        public IInteractable Focus { get; private set; }
        public float CurrentSpeed { get; private set; }

        public void Init(Camera cam, Vector3 spawn, float yaw, Color shirt)
        {
            Instance = this;
            _cam = cam;
            _yaw = yaw;
            transform.position = spawn;
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            _cc = gameObject.AddComponent<CharacterController>();
            _cc.height = 1.8f;
            _cc.radius = 0.32f;
            _cc.center = new Vector3(0, 0.9f, 0);
            _cc.stepOffset = 0.45f;
            _cc.slopeLimit = 50f;
            _body = NpcBody.Create(transform, NpcLook.Player(shirt));
            Agents.All.Add(transform);
        }

        void OnDestroy() => Agents.All.Remove(transform);

        bool Blocked => CityGame.Instance == null || CityGame.Instance.UI == null || CityGame.Instance.UI.CapturesInput;

        void Update()
        {
            if (_cam == null) return;
            float dt = Time.deltaTime;
            bool blocked = Blocked;
            Cursor.lockState = blocked ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = blocked;

            if (!blocked)
            {
                var look = InputAdapter.Look;
                _yaw += look.x * MouseSensitivity * 10f;
                _pitch = Mathf.Clamp(_pitch - look.y * MouseSensitivity * 10f, -35f, 70f);
                _distance = Mathf.Clamp(_distance - InputAdapter.Scroll * 0.6f, 1.8f, 10f);
                if (InputAdapter.Down(GameKey.CameraMode)) _firstPerson = !_firstPerson;
            }

            var input = blocked ? Vector2.zero : InputAdapter.Move;
            bool run = !blocked && InputAdapter.Held(GameKey.Sprint);
            var camYaw = Quaternion.Euler(0, _yaw, 0);
            var move = camYaw * new Vector3(input.x, 0, input.y);
            float speed = input.sqrMagnitude > 0.01f ? (run ? RunSpeed : WalkSpeed) : 0f;
            if (move.sqrMagnitude > 0.01f && !_firstPerson)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(move), dt * 12f);
            if (_firstPerson) transform.rotation = camYaw;

            if (_cc.isGrounded)
            {
                _vy = -1f;
                if (!blocked && InputAdapter.Down(GameKey.Jump)) _vy = JumpSpeed;
            }
            else _vy += Gravity * dt;
            _cc.Move((move * speed + Vector3.up * _vy) * dt);
            CurrentSpeed = speed;

            if (speed > 0.1f) _body.Walk(speed, dt); else _body.Idle(dt);
            _body.SetVisible(!_firstPerson);

            // Взаимодействие
            Focus = Interaction.FindBest(transform.position, transform.forward, 3.2f);
            if (!blocked && Focus != null && InputAdapter.Down(GameKey.Interact)) Focus.Interact();

            // Отправка позы на сервер (10 Гц, только при движении)
            _poseTimer -= dt;
            if (_poseTimer <= 0f && CityGame.Instance != null && CityGame.Instance.Ready)
            {
                _poseTimer = 0.1f;
                if ((transform.position - _lastSentPos).sqrMagnitude > 0.0025f || speed > 0f)
                {
                    _lastSentPos = transform.position;
                    var p = transform.position;
                    CityGame.Instance.Client.SendPose(new PlayerPose { X = p.x, Y = p.y, Z = p.z, Yaw = transform.eulerAngles.y, Speed = speed });
                }
            }
        }

        void LateUpdate()
        {
            if (_cam == null) return;
            var head = transform.position + Vector3.up * 1.65f;
            var rot = Quaternion.Euler(_pitch, _yaw, 0);
            if (_firstPerson)
            {
                _cam.transform.SetPositionAndRotation(head + transform.forward * 0.1f, rot);
                return;
            }
            var pivot = transform.position + Vector3.up * 1.55f + rot * new Vector3(0.45f, 0, 0);
            var desired = pivot - rot * Vector3.forward * _distance;
            // Камера не проходит сквозь стены
            float dist = _distance;
            if (Physics.SphereCast(pivot, 0.25f, (desired - pivot).normalized, out var hit, _distance, ~0, QueryTriggerInteraction.Ignore) && hit.collider.transform.root != transform.root)
                dist = Mathf.Max(0.5f, hit.distance - 0.1f);
            _cam.transform.SetPositionAndRotation(pivot - rot * Vector3.forward * dist, rot);
        }
    }

    /// <summary>Аватары других игроков: интерполяция поз, табличка с именем.</summary>
    public sealed class RemotePlayers : MonoBehaviour
    {
        sealed class Avatar
        {
            public NpcBody Body;
            public TextMesh Name;
            public Vector3 Target;
            public float Yaw, Speed, LastUpdate;
        }

        readonly Dictionary<int, Avatar> _avatars = new Dictionary<int, Avatar>();
        SimClient _client;

        public void Init(SimClient client)
        {
            _client = client;
            client.PoseReceived += OnPose;
            client.PlayerLeft += Remove;
        }

        void OnDestroy()
        {
            if (_client == null) return;
            _client.PoseReceived -= OnPose;
            _client.PlayerLeft -= Remove;
        }

        void OnPose(PlayerPose pose)
        {
            if (pose.PlayerId == _client.LocalPlayerId) return;
            if (!_avatars.TryGetValue(pose.PlayerId, out var a))
            {
                var player = _client.State.GetPlayer(pose.PlayerId);
                var color = player != null ? ColorFrom(player.Color) : Color.white;
                a = new Avatar { Body = NpcBody.Create(transform, NpcLook.Player(color)) };
                a.Body.transform.position = new Vector3(pose.X, pose.Y, pose.Z);
                a.Name = WorldText.Create(a.Body.transform, "Name", player != null ? player.Name : "Игрок", new Vector3(0, 2.15f, 0), 0.22f, color);
                _avatars[pose.PlayerId] = a;
            }
            a.Target = new Vector3(pose.X, pose.Y, pose.Z);
            a.Yaw = pose.Yaw;
            a.Speed = pose.Speed;
            a.LastUpdate = Time.time;
        }

        void Remove(int playerId)
        {
            if (!_avatars.TryGetValue(playerId, out var a)) return;
            Destroy(a.Body.gameObject);
            _avatars.Remove(playerId);
        }

        public static Color ColorFrom(uint rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

        void Update()
        {
            var cam = Camera.main;
            float dt = Time.deltaTime;
            foreach (var a in _avatars.Values)
            {
                var t = a.Body.transform;
                t.position = Vector3.Lerp(t.position, a.Target, dt * 10f);
                t.rotation = Quaternion.Slerp(t.rotation, Quaternion.Euler(0, a.Yaw, 0), dt * 10f);
                if (a.Speed > 0.1f && Time.time - a.LastUpdate < 0.5f) a.Body.Walk(a.Speed, dt); else a.Body.Idle(dt);
                if (cam != null) a.Name.transform.rotation = Quaternion.LookRotation(a.Name.transform.position - cam.transform.position);
            }
        }
    }
}
