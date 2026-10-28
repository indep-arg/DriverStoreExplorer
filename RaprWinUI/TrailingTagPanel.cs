using System;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using Windows.Foundation;

namespace Rapr.WinUI
{
    /// <summary>
    /// Lays out a name followed by a tag right after it. When there is not enough room the name is
    /// the one that shrinks (so a TextBlock can trim with an ellipsis) and the tag stays visible.
    /// The first child is the name, the second one is the tag.
    /// </summary>
    public sealed class TrailingTagPanel : Panel
    {
        public double Spacing { get; set; } = 8;

        protected override Size MeasureOverride(Size availableSize)
        {
            if (this.Children.Count < 2)
            {
                return this.MeasureSingle(availableSize);
            }

            UIElement name = this.Children[0];
            UIElement tag = this.Children[1];

            tag.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            double tagWidth = tag.DesiredSize.Width;
            double gap = tagWidth > 0 ? this.Spacing : 0;

            name.Measure(new Size(Math.Max(0, availableSize.Width - tagWidth - gap), availableSize.Height));

            return new Size(
                name.DesiredSize.Width + gap + tagWidth,
                Math.Max(name.DesiredSize.Height, tag.DesiredSize.Height));
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (this.Children.Count < 2)
            {
                foreach (UIElement child in this.Children)
                {
                    child.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
                }

                return finalSize;
            }

            UIElement name = this.Children[0];
            UIElement tag = this.Children[1];
            double gap = tag.DesiredSize.Width > 0 ? this.Spacing : 0;

            name.Arrange(new Rect(0, (finalSize.Height - name.DesiredSize.Height) / 2, name.DesiredSize.Width, name.DesiredSize.Height));
            tag.Arrange(new Rect(name.DesiredSize.Width + gap, (finalSize.Height - tag.DesiredSize.Height) / 2, tag.DesiredSize.Width, tag.DesiredSize.Height));
            return finalSize;
        }

        private Size MeasureSingle(Size availableSize)
        {
            var size = new Size(0, 0);
            foreach (UIElement child in this.Children)
            {
                child.Measure(availableSize);
                size = new Size(Math.Max(size.Width, child.DesiredSize.Width), Math.Max(size.Height, child.DesiredSize.Height));
            }

            return size;
        }
    }
}
