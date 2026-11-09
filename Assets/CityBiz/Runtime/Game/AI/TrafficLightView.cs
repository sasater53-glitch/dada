using System.Collections.Generic;
using CityBiz.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityBiz.Game
{
    /// <summary>Светофор: три секции для машин + пешеходная секция для перпендикулярного перехода.</summary>
    public sealed class TrafficLightView : MonoBehaviour
    {
        [SerializeField] MeshRenderer _red, _yellow, _green, _pedRed, _pedGreen;
        public int VehicleGroup;
        SignalState _shown = (SignalState)(-1);
        int _pedShown = -1;

        public static TrafficLightView Create(MaterialLibrary M, Transform parent, TrafficLightSpec spec)
        {
            var root = new GameObject("TrafficLight");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = spec.Position;
            root.transform.localRotation = Quaternion.Euler(0, spec.Yaw, 0);
            var view = root.AddComponent<TrafficLightView>();
            view.VehicleGroup = spec.VehicleGroup;

            var mb = new MeshBuilder();
            int metal = 0;
            mb.AddCylinder(metal, Vector3.zero, 0.08f, 3.6f, 8, false);
            mb.AddBox(metal, new Vector3(0, 3.05f, 0.05f), new Vector3(0.35f, 1.05f, 0.28f));
            mb.AddBox(metal, new Vector3(0, 3.05f, -0.1f), new Vector3(0.45f, 1.15f, 0.03f)); // задний экран
            mb.AddBox(metal, new Vector3(0.3f, 2.2f, 0), new Vector3(0.24f, 0.55f, 0.22f));
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            body.AddComponent<MeshFilter>().sharedMesh = mb.Build("TrafficLight");
            body.AddComponent<MeshRenderer>().sharedMaterial = M.Paint("signal_body", new Color(0.12f, 0.13f, 0.12f), null, 1f, 0.4f);

            MeshRenderer Lens(string name, Vector3 pos, float yaw, float size)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.name = name;
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = pos;
                go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
                go.transform.localScale = new Vector3(size, size, size * 0.4f);
                var r = go.GetComponent<MeshRenderer>();
                r.shadowCastingMode = ShadowCastingMode.Off;
                return r;
            }
            // Лицевая сторона светофора смотрит на +Z (навстречу машинам, см. TrafficLightSpec.Yaw)
            view._red = Lens("Red", new Vector3(0, 3.38f, 0.19f), 0, 0.2f);
            view._yellow = Lens("Yellow", new Vector3(0, 3.05f, 0.19f), 0, 0.2f);
            view._green = Lens("Green", new Vector3(0, 2.72f, 0.19f), 0, 0.2f);
            view._pedRed = Lens("PedRed", new Vector3(0.3f, 2.33f, 0.11f), 0, 0.14f);
            view._pedGreen = Lens("PedGreen", new Vector3(0.3f, 2.07f, 0.11f), 0, 0.14f);
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0, 1.8f, 0); col.size = new Vector3(0.25f, 3.6f, 0.25f);
            return view;
        }

        public void Show(SignalState vehicle, bool pedWalk)
        {
            var M = MaterialLibrary.Instance;
            if (vehicle != _shown)
            {
                _shown = vehicle;
                _red.sharedMaterial = vehicle == SignalState.Red ? M.Emission("sig_red_on", new Color(1f, 0.1f, 0.05f), 4f) : M.Paint("sig_red_off", new Color(0.25f, 0.05f, 0.04f), null, 1f, 0.8f);
                _yellow.sharedMaterial = vehicle == SignalState.Yellow ? M.Emission("sig_yellow_on", new Color(1f, 0.7f, 0.05f), 4f) : M.Paint("sig_yellow_off", new Color(0.25f, 0.18f, 0.03f), null, 1f, 0.8f);
                _green.sharedMaterial = vehicle == SignalState.Green ? M.Emission("sig_green_on", new Color(0.1f, 1f, 0.45f), 4f) : M.Paint("sig_green_off", new Color(0.03f, 0.2f, 0.08f), null, 1f, 0.8f);
            }
            int ped = pedWalk ? 1 : 0;
            if (ped != _pedShown)
            {
                _pedShown = ped;
                _pedRed.sharedMaterial = !pedWalk ? M.Emission("sig_red_on", new Color(1f, 0.1f, 0.05f), 4f) : M.Paint("sig_red_off", new Color(0.25f, 0.05f, 0.04f), null, 1f, 0.8f);
                _pedGreen.sharedMaterial = pedWalk ? M.Emission("sig_green_on", new Color(0.1f, 1f, 0.45f), 4f) : M.Paint("sig_green_off", new Color(0.03f, 0.2f, 0.08f), null, 1f, 0.8f);
            }
        }
    }
}
