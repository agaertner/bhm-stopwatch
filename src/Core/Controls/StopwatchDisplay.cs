using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.BitmapFonts;
using Stopwatch;
using System;

namespace Nekres.Stopwatch.Core.Controls
{
    internal class StopwatchDisplay : Control
    {
        // --- Constants ---
        private const int TRACK_THICKNESS    = 2;
        private const int ARC_THICKNESS      = 6;
        private const int RING_PADDING       = 25;
        private const int OUT_RING_PADDING   = 4;
        private const int LOCK_BUTTON_SIZE   = 18;
        private const int ARC_SEGMENTS       = 90;
        private const float TRACK_OPACITY    = 0.25f;

        // --- Fields ---
        private BitmapFontEx _font;
        private BitmapFont   _statusFont;
        private Texture2D    _circleTexture;
        private int          _diameter;

        // Drag state
        private bool  _isDragging;
        private Point _grabOffset;

        // --- Properties ---

        private string _text;
        public string Text
        {
            get => _text;
            set => SetProperty(ref _text, value);
        }

        private bool _isStatusText;
        /// <summary>
        /// When true, renders text with a regular font instead of the monospace digit font.
        /// Used for status messages like "Waiting..." that contain letters.
        /// </summary>
        public bool IsStatusText
        {
            get => _isStatusText;
            set => SetProperty(ref _isStatusText, value);
        }

        private ContentService.FontSize _fontSize;
        public ContentService.FontSize FontSize
        {
            get => _fontSize;
            set
            {
                if (SetProperty(ref _fontSize, value))
                {
                    _font?.Dispose();
                    _font = StopwatchModule.ModuleInstance.ContentsManager
                        .GetBitmapFont("fonts/RobotoMono-Regular.ttf", (int)value, Gw2FontRanges.DigitsOnly);

                    _statusFont = GameService.Content.GetFont(
                        ContentService.FontFace.Menomonia,
                        ContentService.FontSize.Size18,
                        ContentService.FontStyle.Regular);

                    RecalculateSize();
                }
            }
        }

        private Color _color;
        public Color Color
        {
            get => _color;
            set => SetProperty(ref _color, value);
        }

        private float _backgroundOpacity;
        public float BackgroundOpacity
        {
            get => _backgroundOpacity;
            set => SetProperty(ref _backgroundOpacity, value);
        }

        private float _progress;
        /// <summary>
        /// Progress arc fill amount (0.0 to 1.0). Fills clockwise from 12 o'clock.
        /// </summary>
        public float Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, MathHelper.Clamp(value, 0f, 1f));
        }

        private bool _isLocked = true;
        /// <summary>
        /// When locked (default), the control cannot be dragged.
        /// Toggle via the lock button visible on hover.
        /// </summary>
        public bool IsLocked
        {
            get => _isLocked;
            set => SetProperty(ref _isLocked, value);
        }

        // --- Events ---

        /// <summary>
        /// Fired when the user finishes dragging the display to a new position.
        /// Read <see cref="Control.Location"/> from the sender to get the new position.
        /// </summary>
        public event EventHandler Dragged;

        /// <summary>
        /// Fired when the user clicks the 'Set Goal Time' button.
        /// </summary>
        public event EventHandler SetGoalTimeClicked;

        // --- Constructor ---

        public StopwatchDisplay()
        {
            // Font is loaded when FontSize property is set via the object initializer.
        }

        // --- Sizing ---

        private void RecalculateSize()
        {
            if (_font == null) return;

            // Size for the longest expected timer string
            var textSize = _font.MeasureString("-00:00:00.000");
            int textHalfW = (int)Math.Ceiling(textSize.Width / 2f);
            int textHalfH = (int)Math.Ceiling(textSize.Height / 2f);

            // Minimum circle radius to contain the text bounding box
            float textRadius = (float)Math.Sqrt(textHalfW * textHalfW + textHalfH * textHalfH);
            int innerRadius = (int)Math.Ceiling(textRadius) + RING_PADDING;

            _diameter = (int)Math.Ceiling((innerRadius + ARC_THICKNESS / 2f + OUT_RING_PADDING) * 2);

            // Round to even for clean centering
            if (_diameter % 2 != 0) _diameter++;
            _diameter = Math.Max(_diameter, 100);

            this.Size = new Point(_diameter, _diameter);

            RegenerateCircleTexture();
        }

        private void RegenerateCircleTexture()
        {
            _circleTexture?.Dispose();
            _circleTexture = null;

            if (_diameter <= 0) return;

            try
            {
                using var ctx = GameService.Graphics.LendGraphicsDeviceContext();
                var texture = new Texture2D(ctx.GraphicsDevice, _diameter, _diameter);
                var data = new Color[_diameter * _diameter];
                float centerOffset = _diameter / 2f;
                float fillRadius = centerOffset - OUT_RING_PADDING;

                for (int y = 0; y < _diameter; y++)
                {
                    for (int x = 0; x < _diameter; x++)
                    {
                        float dx = x - centerOffset + 0.5f;
                        float dy = y - centerOffset + 0.5f;
                        float dist = (float)Math.Sqrt(dx * dx + dy * dy);

                        if (dist <= fillRadius)
                        {
                            // Anti-aliased edge (1px soft edge)
                            float alpha = MathHelper.Clamp(fillRadius - dist, 0f, 1f);
                            byte a = (byte)(alpha * 255);
                            // Premultiplied alpha (white * alpha)
                            data[y * _diameter + x] = new Color(a, a, a, a);
                        }
                    }
                }

                texture.SetData(data);
                _circleTexture = texture;
            }
            catch (Exception ex)
            {
                StopwatchModule.Logger.Warn(ex, "Failed to create circle texture.");
            }
        }

        // --- Input Handling ---

        protected override CaptureType CapturesInput() => CaptureType.Mouse;

        private Rectangle GetLockButtonBounds()
        {
            float radius = _diameter / 2f;
            float cx = radius;
            float cy = radius;

            // Place inside the ring at 45 degrees towards top-right
            float buttonDist = radius - ARC_THICKNESS - LOCK_BUTTON_SIZE / 2f - 4;
            float angle = -MathHelper.PiOver4;

            int x = (int)(cx + (float)Math.Cos(angle) * buttonDist - LOCK_BUTTON_SIZE / 2f);
            int y = (int)(cy + (float)Math.Sin(angle) * buttonDist - LOCK_BUTTON_SIZE / 2f);

            return new Rectangle(x, y, LOCK_BUTTON_SIZE, LOCK_BUTTON_SIZE);
        }

        private bool IsOverLockButton()
        {
            return this.MouseOver && GetLockButtonBounds().Contains(this.RelativeMousePosition);
        }

        private Rectangle GetGoalButtonBounds()
        {
            float radius = _diameter / 2f;
            float cx = radius;
            float cy = radius;

            // Place inside the ring at 45 degrees towards top-left
            float buttonDist = radius - ARC_THICKNESS - LOCK_BUTTON_SIZE / 2f - 4;
            float angle = -MathHelper.Pi * 3 / 4;

            int x = (int)(cx + (float)Math.Cos(angle) * buttonDist - LOCK_BUTTON_SIZE / 2f);
            int y = (int)(cy + (float)Math.Sin(angle) * buttonDist - LOCK_BUTTON_SIZE / 2f);

            return new Rectangle(x, y, LOCK_BUTTON_SIZE, LOCK_BUTTON_SIZE);
        }

        private bool IsOverGoalButton()
        {
            return this.MouseOver && GetGoalButtonBounds().Contains(this.RelativeMousePosition);
        }

        protected override void OnLeftMouseButtonPressed(MouseEventArgs e)
        {
            // Safety: end any in-progress drag
            if (_isDragging)
            {
                _isDragging = false;
                Dragged?.Invoke(this, EventArgs.Empty);
            }

            // Lock button toggle
            if (IsOverLockButton())
            {
                _isLocked = !_isLocked;
                GameService.Content.PlaySoundEffectByName("button-click");
                return;
            }

            // Goal time button
            if (IsOverGoalButton())
            {
                GameService.Content.PlaySoundEffectByName("button-click");
                SetGoalTimeClicked?.Invoke(this, EventArgs.Empty);
                return;
            }

            // Start drag if unlocked
            if (!_isLocked)
            {
                _isDragging = true;
                _grabOffset = this.RelativeMousePosition;
            }

            base.OnLeftMouseButtonPressed(e);
        }

        protected override void OnLeftMouseButtonReleased(MouseEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                Dragged?.Invoke(this, EventArgs.Empty);
            }

            base.OnLeftMouseButtonReleased(e);
        }

        protected override void OnMouseMoved(MouseEventArgs e)
        {
            if (_isDragging)
            {
                var delta = this.RelativeMousePosition - _grabOffset;
                if (delta != Point.Zero)
                {
                    this.Location += delta;
                }
            }

            base.OnMouseMoved(e);
        }

        // --- Rendering ---

        protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds)
        {
            if (_font == null || _diameter <= 0) return;

            var center = new Vector2(bounds.Width / 2f, bounds.Height / 2f);
            float fillRadius = (bounds.Width / 2f) - OUT_RING_PADDING;
            float ringRadius = fillRadius - ARC_THICKNESS / 2f;

            // 1. Circular background
            if (_circleTexture != null && _backgroundOpacity > 0)
            {
                spriteBatch.DrawOnCtrl(this, _circleTexture, bounds, Color.Black * _backgroundOpacity);
            }

            // 2. Track ring (full circle, thin, subtle)
            DrawArc(spriteBatch, center, ringRadius,
                0f, MathHelper.TwoPi,
                TRACK_THICKNESS, Color.White * TRACK_OPACITY, 128);

            // 3. Progress arc (clockwise from 12 o'clock)
            if (_progress > 0.001f)
            {
                float startAngle = -MathHelper.PiOver2; // 12 o'clock
                float sweepAngle = MathHelper.TwoPi * _progress;
                int segments = Math.Max(4, (int)(ARC_SEGMENTS * _progress));

                DrawArc(spriteBatch, center, ringRadius,
                    startAngle, sweepAngle,
                    ARC_THICKNESS, _color, segments);
            }

            // 4. Timer text centered
            if (!string.IsNullOrEmpty(_text))
            {
                DrawText(spriteBatch, center, bounds);
            }

            // 5. Buttons (visible on hover)
            if (this.MouseOver)
            {
                DrawLockIcon(spriteBatch, GetLockButtonBounds(), _isLocked);
                DrawGoalIcon(spriteBatch, GetGoalButtonBounds());
            }

            // 6. Unlocked indicator (subtle outer ring)
            if (!_isLocked)
            {
                DrawArc(spriteBatch, center, ringRadius + ARC_THICKNESS / 2f + 3,
                    0f, MathHelper.TwoPi,
                    1, Color.White * 0.35f, 128);
            }
        }

        private void DrawText(SpriteBatch spriteBatch, Vector2 center, Rectangle bounds)
        {
            // Choose the right font based on text type
            if (_isStatusText && _statusFont != null)
            {
                spriteBatch.DrawStringOnCtrl(this, _text, _statusFont, bounds,
                    _color, false, HorizontalAlignment.Center, VerticalAlignment.Middle);
            }
            else if (_font != null)
            {
                // Anchor the text using a fixed reference to prevent jittering due to changing width of fractional digits!
                // Dynamically pick the reference shape if hours are dropped
                bool hasHours = _text.Contains(":") && _text.IndexOf(':') != _text.LastIndexOf(':');
                string refString = hasHours ? "00:00:00.000" : "00:00.000";

                var refSize = _font.MeasureString(refString);
                float fixedX = center.X - refSize.Width / 2f;
                float fixedY = center.Y - refSize.Height / 2f;

                string drawText = _text;
                if (drawText.StartsWith("-")) {
                    // Shift starting X left if there is a negative sign, keeping digits aligned
                    var minusSize = _font.MeasureString("-");
                    fixedX -= minusSize.Width;
                }

                var fixedRect = new Rectangle((int)fixedX, (int)fixedY, bounds.Width, bounds.Height);
                spriteBatch.DrawStringOnCtrl(this, drawText, _font, fixedRect,
                    _color, false, true, 2, HorizontalAlignment.Left, VerticalAlignment.Top);
            }
        }

        private void DrawArc(SpriteBatch spriteBatch, Vector2 center, float radius,
            float startAngle, float sweepAngle, int thickness, Color color, int segments)
        {
            if (Math.Abs(sweepAngle) < 0.001f || segments <= 0) return;

            float step = sweepAngle / segments;
            var pixel = ContentService.Textures.Pixel;

            for (int i = 0; i < segments; i++)
            {
                float a1 = startAngle + step * i;
                float a2 = a1 + step;

                var p1 = center + new Vector2((float)Math.Cos(a1), (float)Math.Sin(a1)) * radius;
                var p2 = center + new Vector2((float)Math.Cos(a2), (float)Math.Sin(a2)) * radius;

                var edge = p2 - p1;
                float angle = (float)Math.Atan2(edge.Y, edge.X);
                int length = (int)Math.Ceiling(edge.Length());

                if (length <= 0) continue;

                // Rotates from origin (0, 0.5f) in 1x1 texture space to perfectly center the stroke on the edge.
                spriteBatch.DrawOnCtrl(this, pixel,
                    new Rectangle((int)p1.X, (int)p1.Y, length + 1, thickness),
                    null, color, angle, new Vector2(0, 0.5f));
            }
        }

        private void DrawLockIcon(SpriteBatch spriteBatch, Rectangle area, bool locked)
        {
            var pixel = ContentService.Textures.Pixel;
            bool hovering = area.Contains(this.RelativeMousePosition);

            // Hover highlight
            if (hovering)
            {
                spriteBatch.DrawOnCtrl(this, pixel, area, Color.White * 0.2f);
            }

            // Padlock body
            int bodyW = (int)(area.Width * 0.75f);
            int bodyH = (int)(area.Height * 0.45f);
            int bodyX = area.X + (area.Width - bodyW) / 2;
            int bodyY = area.Bottom - bodyH - 1;

            // Shackle dimensions
            int shackleW = (int)(bodyW * 0.6f);
            int shackleH = (int)(area.Height * 0.4f);
            int shackleThick = Math.Max(2, (int)(bodyW * 0.2f));

            Color iconColor = (locked ? Color.White : new Color(100, 255, 100)) * 0.85f;

            // Body (filled rectangle)
            spriteBatch.DrawOnCtrl(this, pixel,
                new Rectangle(bodyX, bodyY, bodyW, bodyH), iconColor);

            // Shackle (U-shape): centered when locked, shifted right when unlocked
            int shackleX = locked
                ? bodyX + (bodyW - shackleW) / 2
                : bodyX + bodyW - shackleW - 1;
            int shackleTop = bodyY - shackleH + shackleThick;

            // Left arm
            spriteBatch.DrawOnCtrl(this, pixel,
                new Rectangle(shackleX, shackleTop, shackleThick, shackleH), iconColor);

            // Right arm
            spriteBatch.DrawOnCtrl(this, pixel,
                new Rectangle(shackleX + shackleW - shackleThick, shackleTop, shackleThick, shackleH), iconColor);

            // Top bar
            spriteBatch.DrawOnCtrl(this, pixel,
                new Rectangle(shackleX, shackleTop, shackleW, shackleThick), iconColor);
        }

        private void DrawGoalIcon(SpriteBatch spriteBatch, Rectangle area)
        {
            var pixel = ContentService.Textures.Pixel;
            bool hovering = area.Contains(this.RelativeMousePosition);

            // Hover highlight
            if (hovering)
            {
                spriteBatch.DrawOnCtrl(this, pixel, area, Color.White * 0.2f);
            }

            Color iconColor = Color.White * 0.85f;
            var center = new Vector2(area.Center.X, area.Center.Y);
            float radius = area.Width * 0.35f;

            // Draw clock face outline
            DrawArc(spriteBatch, center, radius, 0, MathHelper.TwoPi, 2, iconColor, 32);

            // Minute hand (pointing up)
            spriteBatch.DrawOnCtrl(this, pixel, new Rectangle((int)center.X - 1, (int)center.Y - (int)radius + 2, 2, (int)radius - 2), iconColor);
            
            // Hour hand (pointing right)
            spriteBatch.DrawOnCtrl(this, pixel, new Rectangle((int)center.X, (int)center.Y - 1, (int)(radius * 0.7f), 2), iconColor);
        }

        protected override void DisposeControl()
        {
            _font?.Dispose();
            _circleTexture?.Dispose();
            // _statusFont is from GameService.Content cache — not ours to dispose
            base.DisposeControl();
        }
    }
}
