using System;
using System.Collections.Generic;
using Godot;
using Ghasaq.Core.Combat;
using Ghasaq.Core.Items;
using Ghasaq.Core.Progression;
using Ghasaq.Core.Quests;
using Ghasaq.Core.Simulation;

namespace Ghasaq.Game
{
    /// <summary>
    /// The heads-up display and the on-screen controls, drawn directly with
    /// Control primitives.
    ///
    /// The project has always built its HUD in code rather than compiled UI: the
    /// layout is numbers that can be reviewed, and there is no binary asset to
    /// drift out of step. The on-screen controls are the reason this exists at
    /// all - Android has no keyboard, so the twin-stick layout is the difference
    /// between a build that launches and a game that can be played.
    ///
    /// Ability buttons are drawn and hit-tested from the SAME rectangles, so a
    /// button can never be somewhere other than where it looks.
    /// </summary>
    public partial class Hud : Control
    {
        private enum TouchRole
        {
            None,
            Move,
            Look,
            Button
        }

        private struct TouchSlot
        {
            public TouchRole Role;
            public Vector2 Origin;
            public Vector2 Last;
        }

        public event Action MenuRequested;

        // ---- layout (canvas units; the project stretches a 1920x1080 canvas) ----
        private const float Margin = 28f;

        /// <summary>The vitals bar's width in canvas units; the Soot bar shares it. Public so the smoke test can hold a label to it.</summary>
        public const float BarWidth = 560f;

        private const float BarHeight = 30f;

        /// <summary>The size the bar labels are drawn at. Public for the same reason as <see cref="BarWidth"/>.</summary>
        public const int BarLabelSize = 18;
        private const float StickRadius = 160f;
        private const float ButtonSize = 132f;
        private const float ButtonGap = 16f;
        private const float MoveDeadZonePixels = 10f;
        public const int AbilityButtonCount = 5;

        private const float DamageNumberLifetime = 0.9f;
        private const float DamageNumberRise = 46f;
        private const int MousePointerId = -999;

        // ---- the Sigil surface (the Price is visible at all times) ----

        /// <summary>The card's width in canvas units. Public so the smoke test can hold every authored line to it.</summary>
        public const float SigilCardWidth = 660f;

        /// <summary>The body size the verb and Price lines are drawn at.</summary>
        public const int SigilLineSize = 17;

        private const float SigilTitleSize = 19f;
        private const float SigilTitleAscent = 27f;
        private const float SigilLineAdvance = 38f;
        private static readonly Color SigilVerbColor = new Color(0.86f, 0.86f, 0.9f, 0.92f);
        private static readonly Color SigilPriceColor = new Color(0.95f, 0.73f, 0.42f, 0.96f);
        private static readonly Color SigilLiveColor = new Color(0.95f, 0.52f, 0.44f, 0.96f);
        private static readonly Color SigilLockedColor = new Color(0.9f, 0.32f, 0.28f, 0.95f);

        private Font _font;
        private GameSession _session;
        private PlayerInputReader _input;
        private Camera3D _camera;

        private readonly ExperienceCurve _curve = new ExperienceCurve();
        private readonly Dictionary<int, TouchSlot> _touches = new Dictionary<int, TouchSlot>(4);

        private struct DamageNumber
        {
            public Vector3 World;
            public float Amount;
            public float Age;
            public bool Critical;
        }

        private readonly List<DamageNumber> _damageNumbers = new List<DamageNumber>(16);

        private string _message = "";
        private float _messageTimer;

        private float _fontScale = 1f;
        private float _vignettePhase;

        /// <summary>
        /// The text-size multiplier from the accessibility settings (plan
        /// section 6). It scales the drawn text and the blocks around it, so
        /// nothing overlaps its neighbour at any step; hit targets are left
        /// alone, because a thumb's target is not a text size.
        /// </summary>
        public float FontScale
        {
            get => _fontScale;
            set => _fontScale = Mathf.Clamp(value, 0.75f, 2f);
        }

        /// <summary>The Dimming's light visual distortion, on unless switched off in the accessibility settings.</summary>
        public bool DimmingDistortion { get; set; } = true;

        /// <summary>Swaps the HUD's state colours for the colour-blind-safe palette (plan section 6).</summary>
        public bool ColorblindSafe { get; set; }

        /// <summary>A base text size at a given scale. Public so the smoke test can hold labels to their bars at every step.</summary>
        public static int ScaledSize(int baseSize, float scale)
        {
            return Mathf.Max(1, Mathf.RoundToInt(baseSize * scale));
        }

        /// <summary>
        /// The Dimming's vignette alpha: 0 when the distortion is switched
        /// off, when the meter is below the threshold, or at the bottom of
        /// its pulse; otherwise it deepens with the meter and breathes with
        /// the frame. Public for the smoke test: this arithmetic is exactly
        /// what the accessibility switch changes (plan section 3.6).
        /// </summary>
        public static float DimmingVignetteAlpha(bool enabled, bool isDimming, float dimmingFraction, float pulse)
        {
            if (!enabled || !isDimming)
            {
                return 0f;
            }

            float depth = Mathf.Clamp(dimmingFraction, 0f, 1f);
            return (0.10f + (0.18f * depth)) * Mathf.Lerp(0.7f, 1f, Mathf.Clamp(pulse, 0f, 1f));
        }

        /// <summary>Whether the on-screen controls respond to touches. Off while the menu is open.</summary>
        public bool InputEnabled { get; set; } = true;

        public override void _Ready()
        {
            // The project's UI font chains Noto Sans with Noto Sans Arabic. If a
            // build ever loses it, the Arabic Price lines would silently draw as
            // nothing, so that is said out loud once rather than discovered on a
            // device.
            _font = ThemeDB.FallbackFont;

            if (_font != null && !_font.HasChar(0x0645))
            {
                GD.PushWarning("The UI font has no Arabic glyphs; the Sigil lines will not draw.");
            }

            MouseFilter = MouseFilterEnum.Ignore;
            SetProcess(true);
        }

        public void Bind(GameSession session, PlayerInputReader input, Camera3D camera)
        {
            _session = session;
            _input = input;
            _camera = camera;
        }

        public void ReportDamage(Vector3 worldPosition, float amount, bool critical)
        {
            if (amount <= 0.5f)
            {
                return;
            }

            if (_damageNumbers.Count >= 24)
            {
                _damageNumbers.RemoveAt(0);
            }

            _damageNumbers.Add(new DamageNumber
            {
                World = worldPosition,
                Amount = amount,
                Age = 0f,
                Critical = critical
            });
        }

        public void ShowMessage(string message)
        {
            _message = message ?? "";
            _messageTimer = 2.5f;
        }

        public override void _Process(double deltaSeconds)
        {
            float delta = (float)deltaSeconds;

            DecayDamageNumbers(delta);

            if (_messageTimer > 0f)
            {
                _messageTimer -= delta;
            }

            // The Dimming's vignette breathes; the phase is wrapped so a long
            // session cannot starve the sine wave.
            _vignettePhase += delta * 2.4f;

            if (_vignettePhase > Mathf.Tau)
            {
                _vignettePhase -= Mathf.Tau;
            }

            PollTouches();

            QueueRedraw();
        }

        private void DecayDamageNumbers(float delta)
        {
            for (int i = _damageNumbers.Count - 1; i >= 0; i--)
            {
                DamageNumber number = _damageNumbers[i];
                number.Age += delta;

                if (number.Age >= DamageNumberLifetime)
                {
                    _damageNumbers.RemoveAt(i);
                    continue;
                }

                _damageNumbers[i] = number;
            }
        }

        // ============================= input =====================================

        public override void _UnhandledInput(InputEvent @event)
        {
            switch (@event)
            {
                case InputEventScreenTouch touch:
                    if (touch.Pressed) { BeginPointer(touch.Index, touch.Position); }
                    else { EndPointer(touch.Index); }
                    break;

                case InputEventScreenDrag drag:
                    MovePointer(drag.Index, drag.Position);
                    break;

                case InputEventMouseButton button when button.ButtonIndex == MouseButton.Left
                    && _input != null && !_input.DeviceInputEnabled:
                    if (button.Pressed) { BeginPointer(MousePointerId, button.Position); }
                    else { EndPointer(MousePointerId); }
                    break;

                case InputEventMouseMotion motion when _input != null && !_input.DeviceInputEnabled
                    && _touches.ContainsKey(MousePointerId):
                    MovePointer(MousePointerId, motion.Position);
                    break;
            }
        }

        private void BeginPointer(int id, Vector2 position)
        {
            if (!InputEnabled || _input == null)
            {
                return;
            }

            // The menu button wins over everything, then ability buttons, then the
            // screen halves.
            if (MenuButtonRect().HasPoint(position))
            {
                _touches[id] = new TouchSlot { Role = TouchRole.Button };
                MenuRequested?.Invoke();
                return;
            }

            for (int i = 0; i < AbilityButtonCount; i++)
            {
                if (!AbilityRect(i).HasPoint(position))
                {
                    continue;
                }

                _touches[id] = new TouchSlot { Role = TouchRole.Button };

                // A locked button explains itself instead of doing nothing: the
                // Price is the point, so tapping it shows the Price line. The
                // core would refuse the activation anyway; this is the reason.
                if (i == LockedAbilityIndex())
                {
                    SigilDefinition sigil = _session.EquippedSigil;

                    if (sigil != null)
                    {
                        ShowMessage(sigil.DisplayName + " — " + sigil.PriceLine);
                    }

                    return;
                }

                _input.RequestAbility(i);
                return;
            }

            var slot = new TouchSlot
            {
                Origin = position,
                Last = position
            };

            if (position.X <= Size.X * 0.5f)
            {
                slot.Role = TouchRole.Move;

                // The stick origin is wherever the thumb landed, so it is usable
                // however the phone is held.
                _input.SetHeldAbility(-1);
            }
            else
            {
                slot.Role = TouchRole.Look;
            }

            _touches[id] = slot;
        }

        private void MovePointer(int id, Vector2 position)
        {
            if (!_touches.TryGetValue(id, out TouchSlot slot))
            {
                return;
            }

            slot.Last = position;
            _touches[id] = slot;
        }

        private void EndPointer(int id)
        {
            if (!_touches.TryGetValue(id, out TouchSlot slot))
            {
                return;
            }

            if (slot.Role == TouchRole.Move)
            {
                _input?.SetTouchMoveAxis(Vector2.Zero);
            }

            _touches.Remove(id);
        }

        private void PollTouches()
        {
            if (_input == null)
            {
                return;
            }

            if (!InputEnabled)
            {
                if (_touches.Count > 0)
                {
                    _touches.Clear();
                    _input.SetTouchMoveAxis(Vector2.Zero);
                }

                return;
            }

            bool anyMove = false;

            foreach (KeyValuePair<int, TouchSlot> entry in _touches)
            {
                TouchSlot slot = entry.Value;

                if (slot.Role == TouchRole.Look)
                {
                    _input.AddLookDelta(slot.Last - slot.Origin);
                    slot.Origin = slot.Last;
                    _touches[entry.Key] = slot;
                }
                else if (slot.Role == TouchRole.Move)
                {
                    anyMove = true;
                    Vector2 offset = slot.Last - slot.Origin;
                    Vector2 axis = offset.Length() <= MoveDeadZonePixels
                        ? Vector2.Zero
                        : (offset / StickRadius).LimitLength(1f);
                    _input.SetTouchMoveAxis(axis);
                }
            }

            if (!anyMove)
            {
                _input.SetTouchMoveAxis(Vector2.Zero);
            }
        }

        // ============================= drawing ===================================

        public override void _Draw()
        {
            if (_session == null || _font == null)
            {
                return;
            }

            DrawDimmingVignette();
            DrawVitals();
            DrawExperience();
            DrawAbilityButtons();
            DrawMoveStick();
            DrawFloatingDamage();
            DrawStatusLine();
            DrawSigilSurface();
            DrawMenuButton();
            DrawMessage();
        }

        private void DrawVitals()
        {
            Combatant player = _session.Player;
            if (player?.Vitals == null)
            {
                return;
            }

            float healthFraction = Mathf.Clamp(player.Vitals.HealthFraction, 0f, 1f);
            float staminaFraction = Mathf.Clamp(player.Vitals.StaminaFraction, 0f, 1f);

            float barHeight = BarHeight * _fontScale;
            float gap = 6f * _fontScale;

            float healthY = Margin;
            float staminaY = Margin + barHeight + gap;

            DrawBar(Margin, healthY, BarWidth * _fontScale, barHeight, new Color(0.05f, 0.04f, 0.04f, 0.75f),
                AccessibilityPalette.HealthFill(ColorblindSafe), healthFraction,
                Mathf.RoundToInt(player.Vitals.Health) + " / " + Mathf.RoundToInt(player.Vitals.MaxHealth));

            DrawBar(Margin, staminaY, BarWidth * 0.8f * _fontScale, barHeight, new Color(0.04f, 0.05f, 0.06f, 0.75f),
                AccessibilityPalette.StaminaFill(ColorblindSafe), staminaFraction,
                Mathf.RoundToInt(player.Vitals.Stamina) + " / " + Mathf.RoundToInt(player.Vitals.MaxStamina));

            DrawSootBar(player, barHeight, gap);
        }

        /// <summary>
        /// The السُّخام / Soot meter (plan section 3.6), drawn under the vitals
        /// because that is what it is: the Price the Ghasaq is charging the
        /// body right now. Grey while the bearer is clear, dimming toward red
        /// as the meter climbs, and one word - عَتْمة - the moment the
        /// Dimming begins, so the state is never something the player has to
        /// infer from the numbers. The light visual distortion the plan gates
        /// on accessibility settings lives in <see cref="DrawDimmingVignette"/>;
        /// the bar and the mark carry the state whether or not it is on.
        /// </summary>
        private void DrawSootBar(Combatant player, float barHeight, float gap)
        {
            SootMeter soot = player.Soot;
            if (soot == null)
            {
                return;
            }

            float y = Margin + ((barHeight + gap) * 2f);
            string label = "السُّخام: " + Mathf.RoundToInt(soot.Soot) + " / " + Mathf.RoundToInt(SootTuning.Max);
            Color clear = new Color(0.36f, 0.33f, 0.30f, 0.9f);
            Color fill = clear;

            if (soot.IsDimming)
            {
                fill = AccessibilityPalette.SootFill(clear, soot.DimmingFraction, ColorblindSafe);
                label += "    عَتْمة";
            }

            DrawBar(Margin, y, BarWidth * 0.8f * _fontScale, barHeight,
                new Color(0.04f, 0.04f, 0.05f, 0.75f), fill, soot.Fraction, label);
        }

        /// <summary>
        /// The Dimming's light visual distortion (plan section 3.6): a pulsing
        /// red edge that deepens as the meter fills. One accessibility row
        /// switches it off; the bar and the word عَتْمة carry the state either
        /// way, so nothing is lost when it is gone.
        /// </summary>
        private void DrawDimmingVignette()
        {
            SootMeter soot = _session?.Player?.Soot;
            float pulse = 0.5f + (0.5f * Mathf.Sin(_vignettePhase));
            float alpha = DimmingVignetteAlpha(
                DimmingDistortion,
                soot != null && soot.IsDimming,
                soot?.DimmingFraction ?? 0f,
                pulse);

            if (alpha <= 0.002f)
            {
                return;
            }

            const int Bands = 5;
            float band = 54f * _fontScale;
            Color color = new Color(0.42f, 0.06f, 0.08f);

            for (int i = 0; i < Bands; i++)
            {
                float inset = band * i;
                Color bandColor = new Color(color.R, color.G, color.B, alpha * (1f - (i / (float)Bands)));

                DrawRect(new Rect2(inset, inset, Size.X - (inset * 2f), band), bandColor);
                DrawRect(new Rect2(inset, Size.Y - inset - band, Size.X - (inset * 2f), band), bandColor);
                DrawRect(new Rect2(inset, inset + band, band, Size.Y - (inset * 2f) - (band * 2f)), bandColor);
                DrawRect(new Rect2(Size.X - inset - band, inset + band, band, Size.Y - (inset * 2f) - (band * 2f)), bandColor);
            }
        }

        private void DrawBar(float x, float y, float width, float height, Color back, Color fill, float fraction, string label)
        {
            DrawRect(new Rect2(x, y, width, height), back);
            DrawRect(new Rect2(x, y, width * fraction, height), fill);
            DrawString(_font, new Vector2(x + 8f, y + height - 8f), label, HorizontalAlignment.Left, -1, ScaledSize(BarLabelSize, _fontScale), new Color(1f, 1f, 1f, 0.92f));
        }

        private void DrawExperience()
        {
            int level = _session.Progression.Level;
            int total = _session.Progression.TotalExperience;
            int levelStart = _curve.TotalExperienceAtLevel(level);
            int needed = _curve.ExperienceForNextLevel(level);

            float fraction = needed <= 0 ? 0f : Mathf.Clamp((total - levelStart) / (float)needed, 0f, 1f);

            DrawRect(new Rect2(0f, 0f, Size.X, 8f), new Color(0f, 0f, 0f, 0.5f));
            DrawRect(new Rect2(0f, 0f, Size.X * fraction, 8f), new Color(0.78f, 0.64f, 0.32f, 0.95f));
        }

        private Rect2 AbilityRect(int index)
        {
            float size = ButtonSize * (index == 0 ? 1.25f : 1f);
            float offset = 0f;

            for (int i = 0; i < index; i++)
            {
                offset += (ButtonSize * (i == 0 ? 1.25f : 1f)) + ButtonGap;
            }

            float x = Size.X - Margin - size - offset;
            float y = Size.Y - Margin - size;
            return new Rect2(x, y, size, size);
        }

        private void DrawAbilityButtons()
        {
            Participant participant = _session.Encounter.Find(_session.Player.Id);
            if (participant == null)
            {
                return;
            }

            int lockedIndex = participant.Abilities.LockedAbilityIndex;

            for (int i = 0; i < AbilityButtonCount; i++)
            {
                Rect2 rect = AbilityRect(i);
                float cooldown = Mathf.Clamp(participant.Abilities.CooldownFraction(i), 0f, 1f);
                bool ready = participant.Abilities.IsReady(i) && !participant.Abilities.IsBusy;
                bool locked = i == lockedIndex;

                Color back = ready
                    ? new Color(0.22f, 0.20f, 0.26f, 0.8f)
                    : new Color(0.10f, 0.09f, 0.12f, 0.8f);

                if (locked)
                {
                    back = new Color(0.18f, 0.09f, 0.10f, 0.85f);
                }

                DrawRect(rect, back);

                if (cooldown > 0f)
                {
                    DrawRect(new Rect2(rect.Position.X, rect.Position.Y + rect.Size.Y * (1f - cooldown), rect.Size.X, rect.Size.Y * cooldown),
                        new Color(0f, 0f, 0f, 0.55f));
                }

                AbilityDefinition ability = participant.Abilities[i];
                string label = ability != null && !string.IsNullOrEmpty(ability.DisplayName)
                    ? ability.DisplayName
                    : (i + 1).ToString();

                Color textColor = ready ? Colors.White : new Color(0.6f, 0.6f, 0.6f);

                if (locked)
                {
                    // The Silence Price is shown on the button it takes away:
                    // a red frame, the × mark, and the reason in the Sigil
                    // surface below.
                    DrawRect(rect, SigilLockedColor, false, 3f);
                    DrawString(_font, new Vector2(rect.Position.X + rect.Size.X - 24f, rect.Position.Y + 24f),
                        "\u00d7", HorizontalAlignment.Left, -1, ScaledSize(22, _fontScale), SigilLockedColor);
                    textColor = new Color(0.72f, 0.5f, 0.48f);
                }

                DrawString(_font, new Vector2(rect.Position.X + 8f, rect.Position.Y + rect.Size.Y * 0.5f),
                    label, HorizontalAlignment.Left, rect.Size.X - 12f, ScaledSize(16, _fontScale), textColor);
            }
        }

        private Rect2 MenuButtonRect()
        {
            float size = 96f;
            return new Rect2(Size.X - Margin - size, Margin, size, size);
        }

        private void DrawMenuButton()
        {
            Rect2 rect = MenuButtonRect();
            DrawRect(rect, new Color(0.16f, 0.15f, 0.20f, 0.7f));
            DrawString(_font, new Vector2(rect.Position.X, rect.Position.Y + rect.Size.Y * 0.5f + 8f),
                "MENU", HorizontalAlignment.Center, rect.Size.X, ScaledSize(20, _fontScale), new Color(0.9f, 0.9f, 0.94f, 0.92f));
        }

        private void DrawMoveStick()
        {
            Vector2 baseCentre = new Vector2(Margin + StickRadius, Size.Y - Margin - StickRadius);
            var knob = baseCentre;

            foreach (KeyValuePair<int, TouchSlot> entry in _touches)
            {
                if (entry.Value.Role != TouchRole.Move)
                {
                    continue;
                }

                baseCentre = entry.Value.Origin;
                knob = entry.Value.Last;
            }

            DrawRect(new Rect2(baseCentre.X - StickRadius, baseCentre.Y - StickRadius, StickRadius * 2f, StickRadius * 2f),
                new Color(0.14f, 0.14f, 0.18f, 0.35f));

            Vector2 delta = knob - baseCentre;
            if (delta.Length() > StickRadius)
            {
                knob = baseCentre + delta.Normalized() * StickRadius;
            }

            float knobRadius = StickRadius * 0.35f;
            DrawRect(new Rect2(knob.X - knobRadius, knob.Y - knobRadius, knobRadius * 2f, knobRadius * 2f),
                new Color(0.85f, 0.85f, 0.9f, 0.55f));
        }

        private void DrawFloatingDamage()
        {
            for (int i = 0; i < _damageNumbers.Count; i++)
            {
                DamageNumber number = _damageNumbers[i];

                if (_camera == null || _camera.IsPositionBehind(number.World))
                {
                    continue;
                }

                Vector2 screen = _camera.UnprojectPosition(number.World);
                float alpha = Mathf.Clamp(1f - (number.Age / DamageNumberLifetime), 0f, 1f);
                float rise = DamageNumberRise * (number.Age / DamageNumberLifetime);

                Color color = AccessibilityPalette.Damage(number.Critical, ColorblindSafe);
                color.A = alpha;

                DrawString(_font, new Vector2(screen.X, screen.Y - rise),
                    AccessibilityPalette.DamageText(number.Amount, number.Critical, ColorblindSafe),
                    HorizontalAlignment.Left, -1, ScaledSize(number.Critical ? 26 : 20, _fontScale), color);
            }
        }

        private void DrawStatusLine()
        {
            int hostiles = _session.Encounter.HostilesRemaining;
            string line = _session.RegionId + "    LEVEL " + _session.Progression.Level + "    HOSTILES " + hostiles;

            if (_session.Progression.UnspentAttributePoints > 0)
            {
                line += "    POINTS " + _session.Progression.UnspentAttributePoints;
            }

            DrawString(_font, new Vector2(Margin, Margin + ((BarHeight * _fontScale + 6f * _fontScale) * 3f) + (26f * _fontScale)), line,
                HorizontalAlignment.Left, -1, ScaledSize(18, _fontScale), new Color(0.8f, 0.8f, 0.85f, 0.9f));

            DrawString(_font, new Vector2(Size.X - Margin, Margin + (110f * _fontScale)), CurrentObjectiveText(),
                HorizontalAlignment.Right, 700f * _fontScale, ScaledSize(17, _fontScale), new Color(0.88f, 0.86f, 0.78f, 0.9f));
        }

        private string CurrentObjectiveText()
        {
            IReadOnlyList<QuestState> all = _session.Quests.All;

            for (int i = 0; i < all.Count; i++)
            {
                QuestState state = all[i];
                if (state.Status != QuestStatus.Active)
                {
                    continue;
                }

                string title = state.Definition.Title;
                ObjectiveDefinition[] objectives = state.Definition.Objectives;

                if (objectives.Length == 0)
                {
                    return title;
                }

                ObjectiveDefinition objective = objectives[0];
                int progress = state.ProgressOf(objective.Id);

                return objective.RequiredCount > 1
                    ? title + "\n" + objective.Description + "  " + progress + " / " + objective.RequiredCount
                    : title + "\n" + objective.Description;
            }

            return _session.EncounterCleared ? "The field is quiet." : "";
        }

        // ------------------------------ the sigil surface ------------------------

        /// <summary>
        /// The carried Sigil, its verb and its Price, drawn every frame while one
        /// is carried.
        ///
        /// The contract is explicit: every Price is shown on the HUD at all times
        /// (plan section 3.2, Documentation/Sigils.md). This is that surface. The
        /// static lines come straight from the Sigil's definition, so what is on
        /// screen is what content validation signed off; below them, where the
        /// loadout can say so, the Price is shown as it is actually being paid.
        /// </summary>
        private void DrawSigilSurface()
        {
            float x = Margin;
            float y = Margin + ((BarHeight * _fontScale + 6f * _fontScale) * 3f) + (72f * _fontScale);

            SigilDefinition sigil = _session?.EquippedSigil;
            Combatant player = _session?.Player;

            float cardWidth = SigilCardWidth * _fontScale;
            int lineSize = ScaledSize(SigilLineSize, _fontScale);
            float titleAscent = SigilTitleAscent * _fontScale;
            float advance = SigilLineAdvance * _fontScale;

            if (sigil == null || player == null)
            {
                DrawString(_font, new Vector2(x, y + titleAscent), "بلا وَسْم — لا فعل جديد ولا ثمن.",
                    HorizontalAlignment.Left, cardWidth, lineSize, new Color(0.62f, 0.6f, 0.66f, 0.85f));
                return;
            }

            string live = LivePriceLine(player, sigil);
            bool hasLive = !string.IsNullOrEmpty(live);

            float titleBaseline = y + titleAscent;
            float verbBaseline = titleBaseline + advance + 4f;
            float priceBaseline = verbBaseline + advance;
            float liveBaseline = priceBaseline + advance;
            float bottom = hasLive ? liveBaseline : priceBaseline;

            DrawRect(new Rect2(x - 12f, y - 12f, cardWidth + 24f, (bottom - y) + 24f),
                new Color(0.05f, 0.05f, 0.07f, 0.55f));

            DrawString(_font, new Vector2(x, titleBaseline), sigil.DisplayName + "  \u00b7  " + sigil.EnglishName,
                HorizontalAlignment.Left, cardWidth, ScaledSize((int)SigilTitleSize, _fontScale), new Color(0.93f, 0.88f, 0.72f, 0.96f));

            DrawString(_font, new Vector2(x, verbBaseline), "الفعل: " + sigil.VerbLine,
                HorizontalAlignment.Left, cardWidth, lineSize, SigilVerbColor);

            DrawString(_font, new Vector2(x, priceBaseline), "الثمن: " + sigil.PriceLine,
                HorizontalAlignment.Left, cardWidth, lineSize, SigilPriceColor);

            if (hasLive)
            {
                DrawString(_font, new Vector2(x, liveBaseline), live,
                    HorizontalAlignment.Left, cardWidth, lineSize, SigilLiveColor);
            }
        }

        /// <summary>
        /// The Price as it is being paid at this moment, for the Sigils whose
        /// state the loadout records. Empty when there is nothing live to add:
        /// the Lantern's and Ash's Prices are moments, not states that linger.
        /// </summary>
        private string LivePriceLine(Combatant player, SigilDefinition sigil)
        {
            SigilLoadout loadout = player.Sigil;

            if (loadout == null)
            {
                return "";
            }

            switch (sigil.Kind)
            {
                case SigilId.Silence:
                {
                    int locked = LockedAbilityIndex();
                    if (locked < 0)
                    {
                        return "";
                    }

                    Participant participant = _session.Encounter.Find(player.Id);
                    AbilityDefinition ability = participant?.Abilities[locked];
                    string name = ability != null && !string.IsNullOrEmpty(ability.DisplayName)
                        ? ability.DisplayName
                        : (locked + 1).ToString();

                    return "مقفل الآن: " + name;
                }

                case SigilId.Hunger:
                {
                    if (loadout.IsFamineActive)
                    {
                        return "جوع الغَسَق نشط — سدّد ضربة ليوقف.";
                    }

                    float toFamine = SigilTuning.HungerFamineSeconds - loadout.SecondsSinceLandedHit;
                    return toFamine > 0f ? "الجفاف بعد " + toFamine.ToString("0.0") + " ث" : "";
                }

                case SigilId.Glass:
                {
                    if (loadout.ShieldActive)
                    {
                        return "الدرع: " + Mathf.RoundToInt(loadout.ShieldRemaining) + " / " +
                            Mathf.RoundToInt(loadout.ShieldCapacity);
                    }

                    string reform = "الدرع يعود بعد " + loadout.ShieldReformRemaining.ToString("0.0") + " ث";

                    if (player.Statuses.TryGet(StatusKind.Marked, out StatusEffect exposed))
                    {
                        return "مكشوف " + exposed.Remaining.ToString("0.0") + " ث — " + reform;
                    }

                    return loadout.ShieldReformRemaining > 0f ? reform : "";
                }

                default:
                    return "";
            }
        }

        /// <summary>The player's locked ability index, or -1 when nothing is locked.</summary>
        private int LockedAbilityIndex()
        {
            if (_session?.Player == null)
            {
                return -1;
            }

            Participant participant = _session.Encounter.Find(_session.Player.Id);
            return participant == null ? -1 : participant.Abilities.LockedAbilityIndex;
        }

        private void DrawMessage()
        {
            if (_messageTimer <= 0f || string.IsNullOrEmpty(_message))
            {
                return;
            }

            float alpha = Mathf.Clamp(_messageTimer, 0f, 1f);
            DrawString(_font, new Vector2(Size.X * 0.5f, 150f * _fontScale), _message,
                HorizontalAlignment.Center, Size.X, ScaledSize(28, _fontScale), new Color(0.95f, 0.86f, 0.6f, alpha));
        }
    }
}
