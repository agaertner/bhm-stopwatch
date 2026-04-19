using System;
using Blish_HUD;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Nekres.Stopwatch.Core.Controls;
using Stopwatch;

namespace Nekres.Stopwatch.Core.Controllers
{
    internal class StopwatchController : IDisposable
    {
        private System.Diagnostics.Stopwatch _stopwatch;

        private TimeSpan _startTime;

        private StopwatchDisplay _display;

        private SoundEffect[] _rewindSfx;
        private SoundEffect RewindSfx => _rewindSfx[RandomUtil.GetRandom(0, _rewindSfx.Length - 1)];

        private SoundEffect _startSfx;

        private SoundEffect _beepSfx;
        private SoundEffect _longBeepSfx;
        private TimeSpan _prevBeep;

        private SoundEffect _tickSfx;
        private TimeSpan _prevTick;

        private Color _fontColor;
        public Color FontColor
        {
            get => _fontColor;
            set
            {
                _fontColor = value;
                if (_display == null) {
                    return;
                }

                _display.Color = value;
            }
        }

        private ContentService.FontSize _fontSize;
        public ContentService.FontSize FontSize
        {
            get => _fontSize;
            set
            {
                _fontSize = value;
                if (_display == null) {
                    return;
                }

                _display.FontSize = value;
            }
        }

        private Point _position;
        public Point Position
        {
            get => _position;
            set
            {
                _position = value;
                if (_display == null) {
                    return;
                }

                _display.Location = value;
            }
        }

        private float _backgroundOpacity;
        public float BackgroundOpacity
        {
            get => _backgroundOpacity;
            set
            {
                _backgroundOpacity = value;
                if (_display == null) {
                    return;
                }

                _display.BackgroundOpacity = value;
            }
        }

        public bool IsRunning => _stopwatch.IsRunning;

        public float AudioVolume { get; set; }

        private Color _redShift;

        private bool _inInputPrompt;

        private Vector3 PlayerPosition;
        public StopwatchController()
        {
            _rewindSfx = new[]
            {
                StopwatchModule.ModuleInstance.ContentsManager.GetSound(@"audio\stopwatch-rewind-1.wav"),
                StopwatchModule.ModuleInstance.ContentsManager.GetSound(@"audio\stopwatch-rewind-2.wav"),
                StopwatchModule.ModuleInstance.ContentsManager.GetSound(@"audio\stopwatch-rewind-3.wav"),
                StopwatchModule.ModuleInstance.ContentsManager.GetSound(@"audio\stopwatch-rewind-4.wav")
            };
            _startSfx = StopwatchModule.ModuleInstance.ContentsManager.GetSound(@"audio\stopwatch-start.wav");
            _tickSfx = StopwatchModule.ModuleInstance.ContentsManager.GetSound(@"audio\stopwatch-tick.wav");
            _beepSfx = StopwatchModule.ModuleInstance.ContentsManager.GetSound(@"audio\beep.wav");
            _longBeepSfx = StopwatchModule.ModuleInstance.ContentsManager.GetSound(@"audio\long-beep.wav");

            _redShift = new Color(255, 57, 57);
            _stopwatch = new System.Diagnostics.Stopwatch();
        }

        public void StartAt()
        {
            var prevValue = StopwatchModule.ModuleInstance.StartTime.Value;
            _inInputPrompt = true;
            
            string prefill = string.Empty;
            if (prevValue > TimeSpan.Zero) {
                string frac = prevValue.Milliseconds > 0 ? @"\.fff" : "";
                if (Math.Abs(prevValue.Hours) > 0)
                    prefill = prevValue.ToString($@"h\:mm\:ss{frac}");
                else if (Math.Abs(prevValue.Minutes) > 0)
                    prefill = prevValue.ToString($@"m\:ss{frac}");
                else
                    prefill = prevValue.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            }

            TimeSpanInputPrompt.ShowPrompt(TimeSpanInputPromptCallback, "Enter a start time:", prefill);
        }

        private void TimeSpanInputPromptCallback(bool confirmed, TimeSpan time)
        {
            _inInputPrompt = false;
            if (!confirmed) {
                return;
            }

            StopwatchModule.ModuleInstance.StartTime.Value = time;
            Reset();
        }

        private void OnSetGoalTimeClicked(object sender, EventArgs e)
        {
            StartAt();
        }

        public void Start(TimeSpan? start = null)
        {
            if (_inInputPrompt || !PlayerPosition.Equals(Vector3.Zero)) {
                return;
            }

            _startSfx.Play(AudioVolume, 0, 0);

            // Resume if user did not rewind.
            if (_display != null && !_stopwatch.IsRunning) {
                this.Start();
                return;
            }

            if (_display != null) {
                _display.Dragged -= OnDisplayMoved;
                _display.SetGoalTimeClicked -= OnSetGoalTimeClicked;
                _display.Dispose();
            }

            _display = new StopwatchDisplay
            {
                Parent = GameService.Graphics.SpriteScreen,
                Location = this.Position,
                Color = this.FontColor,
                FontSize = this.FontSize,
                BackgroundOpacity = this.BackgroundOpacity
            };
            _display.Dragged += OnDisplayMoved;
            _display.SetGoalTimeClicked += OnSetGoalTimeClicked;

            if (start.HasValue)
            {
                _startTime = start.Value;
                _prevBeep = TimeSpan.Zero;
            }

            this.Start();
        }

        private void Start() {
            _prevTick = TimeSpan.Zero;

            if (StopwatchModule.ModuleInstance.StartOnMovementEnabled.Value) {
                PlayerPosition = GameService.Gw2Mumble.CurrentMap.IsCompetitiveMode ? GameService.Gw2Mumble.PlayerCamera.Position : GameService.Gw2Mumble.PlayerCharacter.Position;
                _display.IsStatusText = true;
                _display.Text = "Waiting...";
                return;
            }
            _stopwatch.Start();
        }

        public void Stop()
        {
            if (_inInputPrompt) {
                return;
            }
            _startSfx.Play(AudioVolume, 0, 0);
            _stopwatch.Stop();
        }

        public void Reset()
        {
            RewindSfx.Play(AudioVolume, 0, 0);
            if (_display != null) {
                _display.Dragged -= OnDisplayMoved;
                _display.SetGoalTimeClicked -= OnSetGoalTimeClicked;
                _display.Dispose();
                _display = null;
            }
            _stopwatch.Reset();
        }

        public void Update()
        {
            if (!PlayerPosition.Equals(Vector3.Zero) && !PlayerPosition.Equals(GameService.Gw2Mumble.PlayerCharacter.Position))
            {
                PlayerPosition = Vector3.Zero;
                _display.IsStatusText = false;
                _stopwatch.Start();
            }

            if (_display == null || !_stopwatch.IsRunning) {
                return;
            }

            if (!StopwatchModule.ModuleInstance.TickingSoundDisabledSetting.Value && _stopwatch.Elapsed.Subtract(_prevTick).TotalMilliseconds > 500)
            {
                _tickSfx.Play(AudioVolume, 0, 0);
                _prevTick = _stopwatch.Elapsed;
            }

            // Count-up mode (no start time set)
            if (_startTime.Equals(TimeSpan.Zero))
            {
                _display.IsStatusText = false;
                _display.Text = _stopwatch.Elapsed.Hours > 0 
                    ? _stopwatch.Elapsed.ToString(@"hh\:mm\:ss\.fff") 
                    : _stopwatch.Elapsed.ToString(@"mm\:ss\.fff");
                _display.Progress = 0f;
                return;
            }

            // Countdown mode
            var current = _startTime.Subtract(_stopwatch.Elapsed);
            var progress = (float)(_stopwatch.ElapsedMilliseconds / _startTime.TotalMilliseconds);

            _display.IsStatusText = false;
            _display.Text = (current.Ticks < 0 ? "-" : "") + 
                (Math.Abs(current.Hours) > 0 ? current.ToString(@"hh\:mm\:ss\.fff") : current.ToString(@"mm\:ss\.fff"));
            _display.Progress = Math.Min(progress, 1f);
            _display.Color = Color.Lerp(Color.White, _redShift, Math.Min(progress, 1f));

            if (StopwatchModule.ModuleInstance.BeepSoundDisabledSetting.Value) {
                return;
            }

            if (current.TotalSeconds is > -0.1 and < 3 && Math.Abs(current.TotalSeconds - _prevBeep.TotalSeconds) > 1)
            {
                _prevBeep = current;
                if (current.Ticks > 0) {
                    _beepSfx.Play(AudioVolume, 0, 0);
                } else
                {
                    _longBeepSfx.Play(AudioVolume, 0, 0);
                }
            }
        }

        private void OnDisplayMoved(object sender, EventArgs e)
        {
            var display = (StopwatchDisplay)sender;
            _position = display.Location;
            StopwatchModule.ModuleInstance.Position.Value = display.Location;
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            if (_display != null) {
                _display.Dragged -= OnDisplayMoved;
                _display.SetGoalTimeClicked -= OnSetGoalTimeClicked;
                _display.Dispose();
            }
            
            foreach (var sfx in _rewindSfx) {
                sfx.Dispose();
            }

            _startSfx.Dispose();
            _tickSfx.Dispose();
            _beepSfx.Dispose();
            _longBeepSfx.Dispose();
        }
    }
}
