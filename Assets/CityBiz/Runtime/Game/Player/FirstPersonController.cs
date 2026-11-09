using System.Collections.Generic;
using CityBiz.Core;
using CityBiz.Core.Net;
using UnityEngine;

namespace CityBiz.Game
{
    /// <summary>
    /// Игрок от первого лица: CharacterController, мышь, WASD, Shift — бег, Пробел — прыжок, Ctrl — присесть,
    /// E — взаимодействие с тем, на что смотрит камера. Едет на ленте эскалатора, пользуется лифтом (телепорт
    /// к двери лифта на выбранном этаже), шаги звучат по-разному в галерее и в магазинах.
    /// Поза отправляется на сервер 10 раз в секунду — её видят другие игроки.
    /// </summary>
    public sealed class FirstPersonController : MonoBehaviour
    {
        public static FirstPersonController Instance { get; private set; }
        /// <summary>Чувствительность мыши (настраивается в меню паузы).</summary>
        public static float MouseSensitivity = 0.15f;

        public float WalkSpeed = 2.6f, RunSpeed = 5.4f, CrouchSpeed = 1.3f, JumpSpeed = 4.3f, Gravity = -19f;
        public float InteractDistance = 3.2f;

        const float StandHeight = 1.8f, CrouchHeight = 1.15f, Radius = 0.3f, EyeBelowTop = 0.12f;

        CharacterController _cc;
        Camera _cam;
        AudioSource _feet;
        AudioClip[] _concrete, _carpet;
        float _yaw, _pitch, _vy, _height = StandHeight, _stepAccum, _bob, _poseTimer, _lastSentYaw;
        Vector3 _lastSentPos, _belt, _spawn;
        float _spawnYaw;
        Collider _ground;
        int _groundFrame = -10;
        bool _wasGrounded = true;
        float _fallSpeed;

        public IInteractable Focus { get; private set; }
        public float CurrentSpeed { get; private set; }
        public bool Crouching { get; private set; }
        public bool OnEscalator { get; private set; }
        public float Yaw => _yaw;

        public void Init(Camera cam, Vector3 spawn, float yaw)
        {
            Instance = this;
            _cam = cam;
            _yaw = yaw;
            _pitch = 0f;
            _spawn = spawn;
            _spawnYaw = yaw;
            transform.SetPositionAndRotation(spawn, Quaternion.Euler(0, yaw, 0));
            _cc = gameObject.AddComponent<CharacterController>();
            _cc.height = StandHeight;
            _cc.radius = Radius;
            _cc.center = new Vector3(0, StandHeight * 0.5f, 0);
            _cc.stepOffset = 0.4f;
            _cc.slopeLimit = 50f;
            _cc.skinWidth = 0.04f;
            _cc.minMoveDistance = 0f;
            _feet = gameObject.AddComponent<AudioSource>();
            _feet.playOnAwake = false;
            _feet.spatialBlend = 0f;
            _concrete = AudioLibrary.Footsteps("concrete");
            _carpet = AudioLibrary.Footsteps("carpet");
            Agents.All.Add(transform);
        }

        void OnDestroy()
        {
            Agents.All.Remove(transform);
            if (Instance == this) Instance = null;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>Мгновенное перемещение (лифт, возврат после падения).</summary>
        public void Teleport(Vector3 position, float yaw)
        {
            if (_cc != null) _cc.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
            _yaw = yaw;
            _vy = 0f;
            _belt = Vector3.zero;
            _lastSentPos = Vector3.positiveInfinity;
            if (_cc != null) _cc.enabled = true;
        }

        /// <summary>Поездка на лифте: к двери лифта на нужном этаже.</summary>
        public bool RideElevator(MallInstance mall, int floor)
        {
            if (mall == null) return false;
            foreach (var stop in mall.ElevatorStops)
            {
                if (stop.Floor != floor) continue;
                Teleport(mall.transform.TransformPoint(stop.Position), stop.Yaw + mall.transform.eulerAngles.y);
                MallAudio.Instance?.Announce();
                return true;
            }
            return false;
        }

        static bool Blocked
        {
            get
            {
                var game = CityGame.Instance;
                return game == null || game.UI == null || game.UI.CapturesInput;
            }
        }

        void Update()
        {
            if (_cam == null || _cc == null) return;
            float dt = Time.deltaTime;
            bool blocked = Blocked;
            bool locked = !blocked && Application.isFocused;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;

            if (!blocked)
            {
                var look = InputAdapter.Look;
                _yaw += look.x * MouseSensitivity * 10f;
                _pitch = Mathf.Clamp(_pitch - look.y * MouseSensitivity * 10f, -85f, 85f);
            }

            // Присесть (Ctrl); встаём, только если над головой есть место
            bool wantCrouch = !blocked && InputAdapter.Held(GameKey.Crouch);
            if (!wantCrouch && Crouching && !HasHeadroom()) wantCrouch = true;
            Crouching = wantCrouch;
            float targetHeight = Crouching ? CrouchHeight : StandHeight;
            if (!Mathf.Approximately(_height, targetHeight))
            {
                _height = Mathf.MoveTowards(_height, targetHeight, dt * 6f);
                _cc.height = _height;
                _cc.center = new Vector3(0, _height * 0.5f, 0);
            }

            var input = blocked ? Vector2.zero : InputAdapter.Move;
            bool sprint = !blocked && !Crouching && InputAdapter.Held(GameKey.Sprint) && input.y > 0.1f;
            float speed = Crouching ? CrouchSpeed : sprint ? RunSpeed : WalkSpeed;
            var rot = Quaternion.Euler(0, _yaw, 0);
            transform.rotation = rot;
            var move = rot * new Vector3(input.x, 0, input.y) * speed;

            // Лента эскалатора: скорость берётся у полотна, на котором стоим (по последнему касанию CharacterController)
            Escalator escalator = null;
            if (_ground != null && Time.frameCount - _groundFrame <= 2)
            {
                var surface = _ground.GetComponent<EscalatorSurface>();
                if (surface != null) escalator = surface.Owner;
            }
            OnEscalator = escalator != null;
            var belt = escalator != null ? escalator.transform.TransformDirection(escalator.Direction) * Escalator.Speed : Vector3.zero;
            _belt = Vector3.Lerp(_belt, belt, Mathf.Clamp01(dt * 10f));

            bool grounded = _cc.isGrounded;
            if (grounded)
            {
                if (!_wasGrounded && _fallSpeed < -4f) PlayStep(0.6f);
                if (_vy < 0f) _vy = -2f;
                if (!blocked && !Crouching && InputAdapter.Down(GameKey.Jump)) _vy = JumpSpeed;
            }
            else _vy += Gravity * dt;
            _fallSpeed = _vy;
            _wasGrounded = grounded;
            _cc.Move((move + _belt + Vector3.up * _vy) * dt);

            float moving = grounded ? move.magnitude : 0f;
            CurrentSpeed = moving;
            Footsteps(moving, sprint, dt);

            // Взаимодействие — с тем, на что смотрит камера (или ближайшим объектом перед игроком)
            Focus = FindFocus();
            if (!blocked && Focus != null && InputAdapter.Down(GameKey.Interact))
            {
                MallAudio.Instance?.Ui(UiSound.Select);
                Focus.Interact();
            }

            // Страховка: если игрок провалился (баг коллизий) — возвращаем к входу
            if (transform.position.y < _spawn.y - 25f) Teleport(_spawn, _spawnYaw);

            SendPose(dt);
        }

        bool HasHeadroom()
        {
            var origin = transform.position + Vector3.up * (Radius + 0.05f);
            return !Physics.SphereCast(origin, Radius * 0.9f, Vector3.up, out _, StandHeight - Radius * 2f, ~0, QueryTriggerInteraction.Ignore);
        }

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.normal.y > 0.5f)
            {
                _ground = hit.collider;
                _groundFrame = Time.frameCount;
            }
        }

        void Footsteps(float moving, bool sprint, float dt)
        {
            float stride = sprint ? 0.85f : Crouching ? 0.5f : 0.68f;
            if (moving < 0.3f) { _stepAccum = stride * 0.6f; return; }
            _bob += moving * dt * (Mathf.PI / stride);
            _stepAccum += moving * dt;
            if (_stepAccum < stride) return;
            _stepAccum = 0f;
            PlayStep(sprint ? 0.55f : Crouching ? 0.18f : 0.38f);
        }

        void PlayStep(float volume)
        {
            if (_feet == null) return;
            // Внутри помещения — ковролин/мягкий пол, в галерее — камень
            bool inShop = _ground != null && _ground.GetComponentInParent<StorefrontView>() != null;
            var clips = inShop && _carpet.Length > 0 ? _carpet : _concrete;
            if (clips == null || clips.Length == 0) return;
            _feet.pitch = Random.Range(0.92f, 1.08f);
            _feet.PlayOneShot(clips[Random.Range(0, clips.Length)], volume);
        }

        IInteractable FindFocus()
        {
            var origin = _cam.transform.position;
            var dir = _cam.transform.forward;
            if (Physics.Raycast(origin, dir, out var hit, InteractDistance, ~0, QueryTriggerInteraction.Collide))
            {
                var target = hit.collider.GetComponentInParent<IInteractable>();
                if (target != null && target.CanInteract) return target;
            }
            return Interaction.FindBest(origin - Vector3.up * 0.4f, dir, 2.4f);
        }

        void SendPose(float dt)
        {
            _poseTimer -= dt;
            var game = CityGame.Instance;
            if (_poseTimer > 0f || game == null || !game.Ready || game.Client == null) return;
            _poseTimer = 0.1f;
            var p = transform.position;
            if ((p - _lastSentPos).sqrMagnitude < 0.0025f && Mathf.Abs(Mathf.DeltaAngle(_yaw, _lastSentYaw)) < 2f && CurrentSpeed <= 0f) return;
            _lastSentPos = p;
            _lastSentYaw = _yaw;
            game.Client.SendPose(new PlayerPose { X = p.x, Y = p.y, Z = p.z, Yaw = _yaw, Speed = CurrentSpeed });
        }

        void LateUpdate()
        {
            if (_cam == null) return;
            float bob = CurrentSpeed > 0.3f ? Mathf.Sin(_bob) * 0.03f * Mathf.Clamp01(CurrentSpeed / RunSpeed + 0.4f) : 0f;
            var eye = transform.position + Vector3.up * (_height - EyeBelowTop + bob);
            _cam.transform.SetPositionAndRotation(eye, Quaternion.Euler(_pitch, _yaw, 0f));
        }
    }

    /// <summary>Аватары других игроков: персонажи Kenney, интерполяция поз, анимация шага, табличка с именем.</summary>
    public sealed class RemotePlayers : MonoBehaviour
    {
        sealed class Avatar
        {
            public CharacterRig Rig;
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
            foreach (var a in _avatars.Values) if (a.Rig != null) Agents.All.Remove(a.Rig.transform);
            if (_client == null) return;
            _client.PoseReceived -= OnPose;
            _client.PlayerLeft -= Remove;
        }

        void OnPose(PlayerPose pose)
        {
            if (_client == null || pose.PlayerId == _client.LocalPlayerId) return;
            if (!_avatars.TryGetValue(pose.PlayerId, out var a) || a.Rig == null)
            {
                var player = _client.State?.GetPlayer(pose.PlayerId);
                var color = player != null ? ColorFrom(player.Color) : Color.white;
                var rig = CharacterRig.Create(transform, CharacterRig.RandomModel(pose.PlayerId * 7919 + 13));
                rig.name = "RemotePlayer_" + pose.PlayerId;
                rig.transform.position = new Vector3(pose.X, pose.Y, pose.Z);
                a = new Avatar { Rig = rig };
                a.Name = WorldText.Create(rig.transform, "Name", player != null ? player.Name : "Игрок", new Vector3(0, 2.1f, 0), 0.2f, color);
                Agents.All.Add(rig.transform);
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
            if (a.Rig != null)
            {
                Agents.All.Remove(a.Rig.transform);
                Destroy(a.Rig.gameObject);
            }
            _avatars.Remove(playerId);
        }

        public static Color ColorFrom(uint rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

        void Update()
        {
            var game = CityGame.Instance;
            var cam = game != null ? game.ViewPosition : Vector3.zero;
            float dt = Time.deltaTime;
            foreach (var a in _avatars.Values)
            {
                if (a.Rig == null) continue;
                var t = a.Rig.transform;
                // Резкий скачок (лифт) — без интерполяции
                if ((t.position - a.Target).sqrMagnitude > 25f) t.position = a.Target;
                else t.position = Vector3.Lerp(t.position, a.Target, dt * 10f);
                t.rotation = Quaternion.Slerp(t.rotation, Quaternion.Euler(0, a.Yaw, 0), dt * 10f);
                bool moving = a.Speed > 0.1f && Time.time - a.LastUpdate < 0.5f;
                a.Rig.Play(moving ? (a.Speed > 3.5f ? CharacterAnim.Sprint : CharacterAnim.Walk) : CharacterAnim.Idle, 0.2f, moving ? Mathf.Clamp(a.Speed / 2.4f, 0.6f, 1.6f) : 1f);
                float dist = (t.position - cam).magnitude;
                a.Rig.SetVisible(dist < 90f);
                a.Rig.Tick(dt);
                if (a.Name != null)
                {
                    a.Name.gameObject.SetActive(dist < 40f);
                    a.Name.transform.rotation = Quaternion.LookRotation(a.Name.transform.position - cam);
                }
            }
        }
    }
}
