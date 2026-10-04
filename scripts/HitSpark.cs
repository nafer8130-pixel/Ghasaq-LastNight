using Godot;

namespace Ghasaq.Game
{
    /// <summary>
    /// The spark of a landed blow: a small unshaded body that appears at the
    /// point of impact, expands for a moment and hides itself again.
    ///
    /// Built in code and pooled by <see cref="GameRoot"/>, like the rest of the
    /// presentation - there is no particle asset to drift, and no allocation
    /// per hit: a pool of sparks is cycled through, and the fades happen on the
    /// material the spark already owns. Nothing here writes to the core; the
    /// spark only reports a blow that has already been resolved.
    /// </summary>
    public partial class HitSpark : Node3D
    {
        private const float LifetimeSeconds = 0.16f;
        private const float Expansion = 0.9f;

        private MeshInstance3D _mesh;
        private StandardMaterial3D _material;
        private Color _color = Colors.White;
        private float _size = 1f;
        private float _age;

        public override void _Ready()
        {
            _material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = Colors.White
            };

            _mesh = new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.12f, Height = 0.24f },
                MaterialOverride = _material,
                Visible = false
            };

            AddChild(_mesh);
            SetProcess(false);
        }

        /// <summary>Starts a spark at a world position. Restarts it if it is already running.</summary>
        public void Play(Vector3 position, Color color, float size = 1f)
        {
            if (_mesh == null)
            {
                return;
            }

            Position = position;
            _color = color;
            _size = size;
            _age = 0f;

            _mesh.Visible = true;
            _mesh.Scale = Vector3.One * size;
            _material.AlbedoColor = color;
            SetProcess(true);
        }

        public override void _Process(double deltaSeconds)
        {
            _age += (float)deltaSeconds;
            float t = _age / LifetimeSeconds;

            if (t >= 1f)
            {
                _mesh.Visible = false;
                SetProcess(false);
                return;
            }

            // Expand and fade: a blow reads as a burst, not as a ball.
            _mesh.Scale = Vector3.One * (_size * (1f + (Expansion * t)));

            Color color = _color;
            color.A = 1f - t;
            _material.AlbedoColor = color;
        }
    }
}
