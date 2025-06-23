using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using ExileCore.Shared.Attributes;
using SharpDX;

namespace LevelHelper
{
    public class Settings : ISettings
    {
        [Menu("Enabled")]
        public ToggleNode Enable { get; set; } = new ToggleNode(true);

        [Menu("Show XP Bar")]
        public ToggleNode ShowXPBar { get; set; } = new ToggleNode(true);

        [Menu("Show Time To Level")]
        public ToggleNode ShowTTL { get; set; } = new ToggleNode(true);

        [Menu("Display Mode")]
        public ListNode DisplayMode { get; set; } = new ListNode { Value = "Full", Values = { "Full", "Simple", "Minimal" } };

        [Menu("Reset Timer On Idle (Minutes)")]
        public RangeNode<int> ResetTimerMinutes { get; set; } = new RangeNode<int>(1, 1, 10);

        [Menu("Text Scale")]
        public RangeNode<float> TextScaleSize { get; set; } = new RangeNode<float>(1, 0.5f, 5);

        [Menu("Decimal Places")]
        public RangeNode<int> DecimalPlaces { get; set; } = new RangeNode<int>(2, 1, 4);
        
        [Menu("X Offset")]
        public RangeNode<int> PositionX { get; set; } = new RangeNode<int>(0, -2000, 2000);

        [Menu("Y Offset")]
        public RangeNode<int> PositionY { get; set; } = new RangeNode<int>(0, -2000, 2000);

        [Menu("Text Color")]
        public ColorNode TextColor { get; set; } = new ColorNode(Color.White);

        [Menu("Background Color")]
        public ColorNode BackgroundColor { get; set; } = new ColorNode(new Color(0, 0, 0, 25));

        [Menu("Bar Color")]
        public ColorNode BarColor { get; set; } = new ColorNode(new Color(0, 100, 130, 200));
        
        [Menu("New XP Bar Color")]
        public ColorNode NewXPBarColor { get; set; } = new ColorNode(new Color(255, 135, 0, 130));

        [Menu("Outer Bar Color")]
        public ColorNode OuterBarColor { get; set; } = new ColorNode(Color.White);

        [Menu("Death Flash Color")]
        public ColorNode DeathFlashColor { get; set; } = new ColorNode(new Color(210, 0, 0, 220));
    }
}