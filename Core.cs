using System;
using ExileCore;
using System.Drawing;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.Shared.Enums;
using System.Numerics;
using ExileCore.Shared;
using SharpDX;
using Vector2 = System.Numerics.Vector2;
using RectangleF = SharpDX.RectangleF;
using System.Linq;
using System.Collections.Generic;

namespace LevelHelper
{
    public class Core : BaseSettingsPlugin<Settings>
    {
        private class MapRun
        {
            public string AreaName { get; set; }
            public uint StartXp { get; set; }
            public uint EndXp { get; set; }
            public int Level { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }

            private double GetPercentage(uint finalXp)
            {
                if (Level < 1 || Level >= ExpTable.Length) return 0;

                uint totalXpForLevel = ExpTable[Level] - ExpTable[Level - 1];
                if (totalXpForLevel == 0) return 0;

                long gainedXp = (long)finalXp - StartXp;
                return (double)gainedXp / totalXpForLevel * 100;
            }

            public double GetLivePercentageGain(uint currentPlayerXp)
            {
                return GetPercentage(currentPlayerXp);
            }

            public double FinalPercentageGain => EndXp > 0 ? GetPercentage(EndXp) : 0;
            public TimeSpan FinalRunTime => EndTime > StartTime ? EndTime - StartTime : TimeSpan.Zero;
            public TimeSpan GetLiveRunTime() => DateTime.Now - StartTime;
        }

        private const string DEFAULT_TIME_DISPLAY = "00:00:00";
        private const string MAX_TIME_DISPLAY = ">99:59:59";
        private const int MAX_LEVEL = 100;

        public static readonly uint[] ExpTable =
        {
            0, 525, 1760, 3781, 7184, 12186, 19324, 29377, 43181, 61693, 85990,
            117506, 157384, 207736, 269997, 346462, 439268, 551295, 685171,
            843709, 1030734, 1249629, 1504995, 1800847, 2142652, 2535122,
            2984677, 3496798, 4080655, 4742836, 5490247, 6334393, 7283446,
            8384398, 9541110, 10874351, 12361842, 14018289, 15859432, 17905634,
            20171471, 22679999, 25456123, 28517857, 31897771, 35621447, 39721017,
            44225461, 49176560, 54607467, 60565335, 67094245, 74247659, 82075627,
            90631041, 99984974, 110197515, 121340161, 133497202, 146749362,
            161191120, 176922628, 194049893, 212684946, 232956711, 255001620,
            278952403, 304972236, 333233648, 363906163, 397194041, 433312945,
            472476370, 514937180, 560961898, 610815862, 664824416, 723298169,
            786612664, 855129128, 929261318, 1009443795, 1096169525, 1189918242,
            1291270350, 1400795257, 1519130326, 1646943474, 1784977296,
            1934009687, 2094900291, 2268549086, 2455921256, 2658074992,
            2876116901, 3111280300, 3364828162, 3638186694, 3932818530,
            4250334444
        };

        private readonly List<MapRun> _mapHistory = new List<MapRun>();
        private uint _activeMapHash;
        private int _persistentDeathCounter;
        private bool _isPaused;
        private DateTime _pauseTimeStart;
        private Player _playerComponent;

        private DateTime sessionStart;
        private uint sessionStartXp, lastXpAmount;
        private double xpPerSecond, areasToLevelUp;
        private DateTime lastXpGainTime, lastDeathTime, _lastDeathProcessedTime;
        private int lastLevel;

        public override bool Initialise()
        {
            _activeMapHash = 0;
            _persistentDeathCounter = 0;
            _isPaused = false;
            ResetSessionTracking();

            Settings.ResetSessionButton.OnPressed += HandleResetSessionClick;

            return true;
        }

        public override void AreaChange(AreaInstance area)
        {
            var isTownOrHideout = area.IsTown || area.IsHideout;
            var player = GameController.Player?.GetComponent<Player>();
            if (player == null) return;

            _playerComponent = player;

            if (!isTownOrHideout && area.Hash != _activeMapHash)
            {
                FinalizePreviousMapRun(_playerComponent);

                var newRun = new MapRun
                {
                    AreaName = area.Name,
                    StartXp = _playerComponent.XP,
                    Level = _playerComponent.Level,
                    StartTime = DateTime.Now
                };
                _mapHistory.Add(newRun);
                if (_mapHistory.Count > Settings.MapHistoryLimit)
                {
                    _mapHistory.RemoveAt(0);
                }

                ResetSessionTracking();

                _activeMapHash = area.Hash;
                _persistentDeathCounter = 0;
                _isPaused = false;
            }
            else if (!isTownOrHideout && area.Hash == _activeMapHash && _isPaused)
            {
                var pauseDuration = DateTime.Now - _pauseTimeStart;
                sessionStart = sessionStart.Add(pauseDuration);
                lastXpGainTime = lastXpGainTime.Add(pauseDuration);

                var currentRun = _mapHistory.LastOrDefault();
                if (currentRun != null)
                {
                    currentRun.StartTime = currentRun.StartTime.Add(pauseDuration);
                }

                _isPaused = false;
            }
            else if (isTownOrHideout && !_isPaused)
            {
                _isPaused = true;
                _pauseTimeStart = DateTime.Now;
            }
        }

        private void FinalizePreviousMapRun(Player player)
        {
            var lastRun = _mapHistory.LastOrDefault();
            if (lastRun != null && lastRun.EndXp == 0)
            {
                lastRun.EndXp = player.XP;
                lastRun.EndTime = DateTime.Now;
            }

            var completedRuns = _mapHistory.Where(r => r.EndXp > 0 && (r.EndXp - r.StartXp > 0)).ToList();
            if (completedRuns.Any())
            {
                var averageXpGain = completedRuns.Average(r => (double)r.EndXp - r.StartXp);
                if (averageXpGain > 0 && player.Level < MAX_LEVEL)
                {
                    var remainingXp = ExpTable[player.Level] - player.XP;
                    areasToLevelUp = remainingXp / averageXpGain;
                }
            }
        }

        private void ResetSessionTracking()
        {
            sessionStart = DateTime.Now;
            xpPerSecond = 0;
            lastXpGainTime = sessionStart;

            if (_playerComponent != null)
            {
                lastXpAmount = sessionStartXp = _playerComponent.XP;
                lastLevel = _playerComponent.Level;
            }
            else
            {
                lastXpAmount = sessionStartXp = 0;
                lastLevel = 0;
            }
        }

        private void HandleResetSessionClick()
        {
            ResetSessionTracking();
            _mapHistory.Clear();
            _activeMapHash = 0;
            _persistentDeathCounter = 0;
            _isPaused = false;
        }

        private double GetExpPct(int level, uint exp)
        {
            if (level >= MAX_LEVEL || level < 1) return 0.0;

            if (level > lastLevel)
            {
                ResetSessionTracking();
                lastLevel = level;
            }

            var levelStart = ExpTable[level - 1];
            return (exp - levelStart) / (double)(ExpTable[level] - levelStart) * 100;
        }

        private string GetTTL(uint currentXp, int level)
        {
            if (level < 1) return DEFAULT_TIME_DISPLAY;
            var now = DateTime.Now;

            if (currentXp < lastXpAmount && (now - _lastDeathProcessedTime).TotalMilliseconds > 5000)
            {
                lastDeathTime = now;
                _persistentDeathCounter++;
                xpPerSecond = 0;
                _lastDeathProcessedTime = now;
                return DEFAULT_TIME_DISPLAY;
            }

            if (currentXp > lastXpAmount)
            {
                lastXpGainTime = now;
            }
            else if ((now - lastXpGainTime).TotalMinutes > Settings.ResetTimerMinutes)
            {
                ResetSessionTracking();
                return DEFAULT_TIME_DISPLAY;
            }

            lastXpAmount = currentXp;

            var totalTime = (now - sessionStart).TotalSeconds;
            if (totalTime < Settings.MinTimeForCalculation.Value) return DEFAULT_TIME_DISPLAY;

            var gained = (long)currentXp - sessionStartXp;
            if (gained > 0)
            {
                xpPerSecond = gained / totalTime;
            }

            if (xpPerSecond <= 0) return DEFAULT_TIME_DISPLAY;

            var remaining = ExpTable[level] - currentXp;
            var seconds = remaining / xpPerSecond;

            return double.IsInfinity(seconds) || double.IsNaN(seconds)
                ? DEFAULT_TIME_DISPLAY
                : TimeSpan.FromSeconds(seconds).Hours > 99
                    ? MAX_TIME_DISPLAY
                    : $"{TimeSpan.FromSeconds(seconds).Hours:00}:{TimeSpan.FromSeconds(seconds).Minutes:00}:{TimeSpan.FromSeconds(seconds).Seconds:00}";
        }

        public override void Render()
        {
            if (!Settings.Enable) return;

            _playerComponent = GameController.Player?.GetComponent<Player>();
            if (_playerComponent == null || GameController.IsLoading) return;

            if (GameController.Game.IngameState.IngameUi.GameUI?.GetChildAtIndex(11) is not Element expBarElement) return;
            var expBarRect = expBarElement.GetClientRect();

            DrawBarAndBackground(expBarRect, _playerComponent);

            if (_isPaused)
            {
                DrawTextElements(expBarRect, null, 0, null, true);
            }
            else
            {
                var pct = GetExpPct(_playerComponent.Level, _playerComponent.XP);
                var ttl = GetTTL(_playerComponent.XP, _playerComponent.Level);
                DrawTextElements(expBarRect, _playerComponent, pct, ttl, false);
            }

            if (expBarRect.Contains(Input.MousePosition))
            {
                DrawHoverPanel(expBarRect, _playerComponent);
            }
        }

        private void DrawBarAndBackground(RectangleF expBarRect, Player player)
        {
            if (!Settings.ShowXPBar) return;

            var frameWidth = expBarRect.Width;
            var frameHeight = 16;
            var frameX = expBarRect.X + (expBarRect.Width - frameWidth) / 2 + Settings.PositionX;
            var frameY = expBarRect.Center.Y - frameHeight / 2f + Settings.PositionY - 2f;

            var framePosition = new Vector2(frameX, frameY);
            var barHeight = frameHeight - 2;
            var barPosition = new Vector2(framePosition.X + 1, framePosition.Y + 1);
            var adjustedWidth = frameWidth - 2;

            var outerColor = (DateTime.Now - lastDeathTime).TotalMilliseconds < Settings.DeathFlashDurationMs.Value && !_isPaused
                ? Settings.DeathFlashColor
                : Settings.OuterBarColor;

            Graphics.DrawFrame(new RectangleF(framePosition.X, framePosition.Y, frameWidth, frameHeight), outerColor, 1);

            var currentLevel = player.Level;
            if (currentLevel < 1 || currentLevel >= MAX_LEVEL)
            {
                Graphics.DrawBox(new RectangleF(barPosition.X, barPosition.Y, adjustedWidth, barHeight), Settings.BackgroundColor);
                return;
            }

            uint xpForCurrentLevelStart = ExpTable[currentLevel - 1];
            uint xpForCurrentLevelEnd = ExpTable[currentLevel];
            uint totalXpForCurrentLevel = xpForCurrentLevelEnd - xpForCurrentLevelStart;

            // Calculate total XP progress within the current level
            float currentLevelProgress = (player.XP - xpForCurrentLevelStart) / (float)totalXpForCurrentLevel;
            float currentLevelWidth = adjustedWidth * currentLevelProgress;

            // Calculate XP gained in current session within the current level
            uint sessionXpInCurrentLevel = 0;
            if (player.Level == lastLevel)
            {
                sessionXpInCurrentLevel = player.XP - sessionStartXp;
            }
            else if (player.Level > lastLevel)
            {
                sessionXpInCurrentLevel = player.XP - xpForCurrentLevelStart;
            }

            float sessionProgressWidth = adjustedWidth * (sessionXpInCurrentLevel / (float)totalXpForCurrentLevel);

            // Draw background for remaining XP in current level
            Graphics.DrawBox(new RectangleF(barPosition.X, barPosition.Y, adjustedWidth, barHeight), Settings.BackgroundColor);

            // Draw XP gained in current session (NewXPBarColor)
            Graphics.DrawBox(new RectangleF(barPosition.X + currentLevelWidth - sessionProgressWidth, barPosition.Y, sessionProgressWidth, barHeight), Settings.NewXPBarColor);

            // Draw total XP progress (BarColor) 
            Graphics.DrawBox(new RectangleF(barPosition.X, barPosition.Y, currentLevelWidth - sessionProgressWidth, barHeight), Settings.BarColor);
        }

        private void DrawTextElements(RectangleF expBarRect, Player player, double pct, string ttl, bool isPaused)
        {
            using (Graphics.SetTextScale(Settings.TextScaleSize.Value))
            {
                var frameWidth = expBarRect.Width;
                var frameHeight = 16;
                var frameX = expBarRect.X + (expBarRect.Width - frameWidth) / 2 + Settings.PositionX;
                var frameY = expBarRect.Center.Y - frameHeight / 2f + Settings.PositionY - 5f;

                if (isPaused)
                {
                    var pausedText = "Paused";
                    var pausedTextSize = Graphics.MeasureText(pausedText);
                    var textPosition = new Vector2(
                        frameX + frameWidth / 2f - pausedTextSize.X / 2f,
                        frameY + frameHeight / 2f - pausedTextSize.Y / 2f + 2f
                    );
                    Graphics.DrawText(pausedText, textPosition, Settings.TextColor);
                }
                else
                {
                    if (player == null) return; // Defensive check
                    var xpText = $"{player.Level} ({Math.Round(pct, Settings.DecimalPlaces.Value)}%)";
                    var xpTextSize = Graphics.MeasureText(xpText);
                    var xpTextY = frameY + (frameHeight / 2f) - (xpTextSize.Y / 2f) + 2f;
                    Graphics.DrawText(xpText, new Vector2(frameX + 3, xpTextY), Settings.TextColor);

                    if (Settings.DisplayMode == "Minimal") return;

                    string additionalText = "";
                    if (Settings.ShowTTL)
                    {
                        additionalText = $"TTL: {ttl}";
                        if (Settings.DisplayMode == "Full")
                        {
                            additionalText += $" - Areas: {Math.Ceiling(areasToLevelUp)} - Deaths: {_persistentDeathCounter}";
                        }
                    }

                    var additionalTextSize = Graphics.MeasureText(additionalText);
                    var additionalTextY = frameY + (frameHeight / 2f) - (additionalTextSize.Y / 2f) + 2f;
                    Graphics.DrawText(additionalText, new Vector2(frameX + frameWidth - additionalTextSize.X - 3, additionalTextY), Settings.TextColor);
                }
            }
        }

        private void DrawHoverPanel(RectangleF barRect, Player player)
        {
            if (!_mapHistory.Any()) return;

            var lineHeight = 18f;
            var panelPadding = 5f;
            var panelHeight = (lineHeight * _mapHistory.Count) + (panelPadding * 2);
            var panelWidth = 400f;
            var panelX = barRect.Center.X - panelWidth / 2f;
            var panelY = barRect.Top - panelHeight - 5;

            var panelBg = new SharpDX.Color(0, 0, 0, 220);
            Graphics.DrawBox(new RectangleF(panelX, panelY, panelWidth, panelHeight), panelBg);
            Graphics.DrawFrame(new RectangleF(panelX, panelY, panelWidth, panelHeight), SharpDX.Color.White, 1);

            var textY = panelY + panelPadding;

            for (int i = 0; i < _mapHistory.Count; i++)
            {
                var run = _mapHistory[i];
                var isLive = i == _mapHistory.Count - 1 && !_isPaused;

                var pctGain = isLive ? run.GetLivePercentageGain(player.XP) : run.FinalPercentageGain;
                var runTime = isLive ? run.GetLiveRunTime() : run.FinalRunTime;
                var formattedTime = runTime.ToString(@"mm\:ss");

                var gainSign = pctGain >= 0 ? "+" : "";
                var mapText = $"{i + 1}. {run.AreaName}: {gainSign}{pctGain:F2}% - {formattedTime} Runtime";

                Graphics.DrawText(mapText, new Vector2(panelX + 5, textY), Settings.TextColor);
                textY += lineHeight;
            }
        }
    }
}