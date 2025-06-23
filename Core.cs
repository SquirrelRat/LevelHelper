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

                uint gainedXp = finalXp - StartXp;
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
        private const int MIN_TIME_FOR_CALCULATION = 10;
        private const int MAX_LEVEL = 100;
        private const int DEATH_FLASH_DURATION_MS = 1000;

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

        private DateTime sessionStart;
        private uint sessionStartXp, lastXpAmount;
        private double xpPerSecond, areasToLevelUp;
        private DateTime lastXpGainTime, lastDeathTime;
        private int lastLevel;

        public override bool Initialise()
        {
            _activeMapHash = 0;
            _persistentDeathCounter = 0;
            _isPaused = false;
            ResetSessionTracking();

            return true;
        }

        public override void AreaChange(AreaInstance area)
        {
            var isTownOrHideout = area.IsTown || area.IsHideout;
            var player = GameController.Player?.GetComponent<Player>();
            if (player == null) return;

            if (!isTownOrHideout && area.Hash != _activeMapHash)
            {
                FinalizePreviousMapRun(player);

                var newRun = new MapRun
                {
                    AreaName = area.Name,
                    StartXp = player.XP,
                    Level = player.Level,
                    StartTime = DateTime.Now
                };
                _mapHistory.Add(newRun);
                if (_mapHistory.Count > 5)
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
            var player = GameController.Player?.GetComponent<Player>();
            sessionStart = DateTime.Now;
            xpPerSecond = 0;
            lastXpGainTime = sessionStart;

            if (player != null)
            {
                lastXpAmount = sessionStartXp = player.XP;
                lastLevel = player.Level;
            }
        }

        private double GetExpPct(int level, uint exp)
        {
            if (level >= MAX_LEVEL || level < 1) return 0.0;

            if (level > lastLevel)
            {
                ResetSessionTracking();
            }

            var levelStart = ExpTable[level - 1];
            return (exp - levelStart) / (double)(ExpTable[level] - levelStart) * 100;
        }

        private string GetTTL(uint currentXp, int level)
        {
            if (level < 1) return DEFAULT_TIME_DISPLAY;
            var now = DateTime.Now;

            if (currentXp < lastXpAmount)
            {
                lastDeathTime = now;
                _persistentDeathCounter++;
                ResetSessionTracking();
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

            if (sessionStart == default || sessionStartXp == 0)
            {
                ResetSessionTracking();
                return DEFAULT_TIME_DISPLAY;
            }

            var totalTime = (now - sessionStart).TotalSeconds;
            if (totalTime < MIN_TIME_FOR_CALCULATION) return DEFAULT_TIME_DISPLAY;

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

            var player = GameController.Player?.GetComponent<Player>();
            if (player == null || GameController.IsLoading) return;

            if (GameController.Game.IngameState.IngameUi.GameUI?.GetChildAtIndex(11) is not Element expBarElement) return;
            var expBarRect = expBarElement.GetClientRect();

            DrawBarAndBackground(expBarRect, player);

            if (_isPaused)
            {
                DrawTextElements(expBarRect, null, 0, null, true);
            }
            else
            {
                var pct = GetExpPct(player.Level, player.XP);
                var ttl = GetTTL(player.XP, player.Level);
                DrawTextElements(expBarRect, player, pct, ttl, false);
            }

            if (expBarRect.Contains(Input.MousePosition))
            {
                DrawHoverPanel(expBarRect, player);
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

            var outerColor = (DateTime.Now - lastDeathTime).TotalMilliseconds < DEATH_FLASH_DURATION_MS && !_isPaused
                ? Settings.DeathFlashColor
                : Settings.OuterBarColor;

            Graphics.DrawFrame(new RectangleF(framePosition.X, framePosition.Y, frameWidth, frameHeight), outerColor, 1);

            var currentLevel = player.Level;
            if (currentLevel < 1 || currentLevel >= MAX_LEVEL)
            {
                Graphics.DrawBox(new RectangleF(barPosition.X, barPosition.Y, adjustedWidth, barHeight), Settings.BackgroundColor);
                return;
            }

            var initialXPWidth = adjustedWidth * ((sessionStartXp - ExpTable[currentLevel - 1]) / (float)(ExpTable[currentLevel] - ExpTable[currentLevel - 1]));
            Graphics.DrawBox(new RectangleF(barPosition.X, barPosition.Y, initialXPWidth, barHeight), Settings.BarColor);

            var newXPWidth = adjustedWidth * ((player.XP - sessionStartXp) / (float)(ExpTable[currentLevel] - ExpTable[currentLevel - 1]));
            var newXPPosition = new Vector2(barPosition.X + initialXPWidth, barPosition.Y);
            Graphics.DrawBox(new RectangleF(newXPPosition.X, newXPPosition.Y, newXPWidth, barHeight), Settings.NewXPBarColor);

            var remainingWidth = adjustedWidth - initialXPWidth - newXPWidth;
            var remainingPosition = new Vector2(newXPPosition.X + newXPWidth, barPosition.Y);
            Graphics.DrawBox(new RectangleF(remainingPosition.X, remainingPosition.Y, remainingWidth, barHeight), Settings.BackgroundColor);
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
            var graphHeight = 40f;
            var panelHeight = (lineHeight * _mapHistory.Count) + (panelPadding * 3) + graphHeight;
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

                var mapText = $"{i + 1}. {run.AreaName}: +{pctGain:F2}% - {formattedTime} Runtime";

                Graphics.DrawText(mapText, new Vector2(panelX + 5, textY), Settings.TextColor);
                textY += lineHeight;
            }

            textY += panelPadding;
            var graphRect = new RectangleF(panelX + panelPadding, textY, panelWidth - (panelPadding * 2), graphHeight);
            DrawHistoryGraph(graphRect, _mapHistory);
        }

        private void DrawHistoryGraph(RectangleF bounds, List<MapRun> history)
        {
            var dataPoints = history.Where(r => r.EndXp > 0).Select(r => r.FinalPercentageGain).ToList();
            if (dataPoints.Count == 0) return;

            var maxValue = dataPoints.Max();
            var minValue = dataPoints.Count > 1 ? dataPoints.Min() : 0;
            var valueRange = maxValue - minValue;

            Graphics.DrawBox(bounds, new SharpDX.Color(255, 255, 255, 10));

            var totalBarWidth = bounds.Width / 5;
            var barWidth = totalBarWidth * 0.8f;
            var barSpacing = totalBarWidth * 0.2f;

            for (int i = 0; i < dataPoints.Count; i++)
            {
                var dataValue = dataPoints[i];
                var percentage = valueRange == 0 ? 1.0 : (dataValue - minValue) / valueRange;
                var barHeight = (float)percentage * bounds.Height;

                var barX = bounds.X + (i * totalBarWidth) + (barSpacing / 2);
                var barY = bounds.Bottom - barHeight;

                var color = GetColorForValue(dataValue, minValue, maxValue);
                Graphics.DrawBox(new RectangleF(barX, barY, barWidth, barHeight), color);
            }
        }

        private SharpDX.Color GetColorForValue(double value, double min, double max)
        {
            if (min >= max) return SharpDX.Color.Yellow;

            var percentage = (value - min) / (max - min);

            byte r, g;
            if (percentage < 0.5)
            {
                r = 255;
                g = (byte)(255 * (percentage * 2));
            }
            else
            {
                r = (byte)(255 * (1 - (percentage - 0.5) * 2));
                g = 255;
            }
            return new SharpDX.Color(r, g, (byte)0, (byte)220);
        }
    }
}